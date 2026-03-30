using UnityEngine;
using UnityEngine.InputSystem;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine.Rendering;

/// <summary>
/// GPU-based particle system with compute shader simulation and procedural rendering.
/// Supports mouse emission, gravity, damping, floor and sphere collisions, and a height-based color gradient.
/// Also includes optional CPU-side analysis of particle data via async GPU readback (min/max/avg height, histogram).
/// </summary>
public class GpuParticles : MonoBehaviour
{
    [Header("Refs")]
    public Camera cam;
    public ComputeShader simCS;
    public Material renderMat;

    [Header("Capacity")]
    [Min(1)] public int maxParticles = 100_000;

    [Header("Spawn (hold left click)")]
    public int spawnPerFrameWhileHeld = 200;
    public float spawnLife = 1.25f;
    public float spawnSpeed = 3.0f;
    public float spawnRadius = 0.05f;

    [Header("Simulation")]
    public Vector3 gravity = new Vector3(0, -2.5f, 0);
    [Range(0f, 10f)] public float damping = 0.15f;

    [Header("Collisions (floor)")]
    public float floorY = 0f;
    [Range(0f, 1f)] public float restitution = 0.6f;
    [Range(0f, 1f)] public float groundFriction = 0.05f;

    [Header("Collisions (spheres)")]
    [Tooltip("Each Vector4 = (x,y,z,r)")]
    public Vector4[] spheres = new Vector4[]
    {
        new Vector4(0f, 0f, 0f, 1.5f),
    };

    [Header("Render - Height Gradient (GPU)")]
    public bool useHeightGradient = false;
    public float gradientMinY = 0f;
    public float gradientMaxY = 3f;
    public Color lowColor = new Color(0f, 0.6f, 1f, 1f);
    public Color highColor = new Color(1f, 0.2f, 0.2f, 1f);

    // =========================
    // CPU ANALYSIS
    // While GPU sim runs, we can also read back a small sample of particles to do CPU-side analysis (min/max/avg, histogram, etc).
    // =========================
    [Header("CPU Analysis")]
    public bool enableCpuAnalysis = true;

    [Tooltip("How often (seconds) we request an async GPU readback for analysis.")]
    [Range(0.05f, 2f)] public float analysisInterval = 0.25f;

    [Tooltip("How many particles to sample for CPU stats.")]
    [Range(128, 8192)] public int analysisSampleCount = 2048;

    [Tooltip("Histogram buckets for particle height (Y).")]
    [Range(8, 64)] public int histogramBins = 16;

    [Tooltip("Height range for histogram (Y).")]
    public float histMinY = 0f;
    public float histMaxY = 3f;

    // Analysis results
    float cpuMinY, cpuMaxY, cpuAvgY;
    int[] cpuHistogram;
    uint lastSpawnedThisFrame;
    int lastSampleUsed;
    float lastAnalysisMs;
    float nextAnalysisTime;
    bool readbackInFlight;

    ComputeBuffer particleBuffer;
    ComputeBuffer counterBuffer;
    ComputeBuffer sphereBuffer;

    int kernel;
    int clearKernel;

    const int THREADS_X = 256;
    uint frameIndex = 0;

    [StructLayout(LayoutKind.Sequential)]
    struct Particle
    {
        public Vector3 pos;
        public Vector3 vel;
        public float life;
        public float pad;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Sphere
    {
        public Vector3 center;
        public float radius;
    }

    void Reset()
    {
        cam = Camera.main;
    }

    /// <summary>
    /// Initialize buffers and shader bindings.
    /// </summary>
    void Start()
    {
        if (cam == null) cam = Camera.main;

        if (simCS == null)
        {
            Debug.LogError("GpuParticles: Missing ComputeShader (simCS).");
            enabled = false;
            return;
        }

        if (renderMat == null)
        {
            Debug.LogError("GpuParticles: Missing render Material (renderMat).");
            enabled = false;
            return;
        }

        kernel = simCS.FindKernel("CSMain");
        clearKernel = simCS.FindKernel("CSClear");

        int stride = Marshal.SizeOf(typeof(Particle));
        particleBuffer = new ComputeBuffer(maxParticles, stride, ComputeBufferType.Structured);

        // Init particles once
        var init = new Particle[maxParticles];
        for (int i = 0; i < maxParticles; i++)
        {
            init[i].pos = Vector3.zero;
            init[i].vel = Vector3.zero;
            init[i].life = 0f;
            init[i].pad = 0f;
        }
        particleBuffer.SetData(init);

        // Counter buffer (1 uint)
        counterBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Structured);
        counterBuffer.SetData(new uint[] { 0 });

        // Bind to render
        renderMat.SetBuffer("_Particles", particleBuffer);

        // Bind to compute
        simCS.SetBuffer(kernel, "_Particles", particleBuffer);
        simCS.SetBuffer(clearKernel, "_Counters", counterBuffer);
        simCS.SetBuffer(kernel, "_Counters", counterBuffer);

        simCS.SetInt("_MaxParticles", maxParticles);

        // spheres
        UploadSpheres();

        // CPU analysis init
        cpuHistogram = new int[Mathf.Max(1, histogramBins)];
        nextAnalysisTime = Time.time + analysisInterval;
    }

    /// <summary>
    /// Upload sphere collision data to GPU.
    /// </summary>
    void UploadSpheres()
    {
        if (sphereBuffer != null)
        {
            sphereBuffer.Release();
            sphereBuffer = null;
        }

        int count = (spheres != null) ? spheres.Length : 0;
        if (count <= 0)
        {
            simCS.SetInt("_SphereCount", 0);
            return;
        }

        var sData = new Sphere[count];
        for (int i = 0; i < count; i++)
        {
            sData[i].center = new Vector3(spheres[i].x, spheres[i].y, spheres[i].z);
            sData[i].radius = spheres[i].w;
        }

        int stride = Marshal.SizeOf(typeof(Sphere));
        sphereBuffer = new ComputeBuffer(count, stride, ComputeBufferType.Structured);
        sphereBuffer.SetData(sData);

        simCS.SetBuffer(kernel, "_Spheres", sphereBuffer);
        simCS.SetInt("_SphereCount", count);
    }

    void Update()
    {
        // --- Mouse emit ---
        int emitOn = 0;
        Vector3 emitPos = Vector3.zero;

        var mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.isPressed)
        {
            emitOn = 1;
            Vector2 mouseScreen = mouse.position.ReadValue();
            emitPos = GetMouseWorldOnPlaneZ0(mouseScreen);
        }

        // Reset counter on GPU each frame
        simCS.Dispatch(clearKernel, 1, 1, 1);

        // Emit params
        simCS.SetInt("_EmitOn", emitOn);

        int emitCount = 0;
        if (emitOn == 1)
        {
            bool pressedThisFrame = (mouse != null) && mouse.leftButton.wasPressedThisFrame;
            emitCount = pressedThisFrame ? spawnPerFrameWhileHeld * 5 : spawnPerFrameWhileHeld;
        }
        simCS.SetInt("_EmitCount", emitCount);

        simCS.SetVector("_EmitPos", emitPos);
        simCS.SetFloat("_EmitRadius", spawnRadius);
        simCS.SetFloat("_EmitLife", spawnLife);
        simCS.SetFloat("_EmitSpeed", spawnSpeed);
        simCS.SetInt("_FrameIndex", (int)frameIndex++);

        // Compute simulation params
        simCS.SetFloat("_DeltaTime", Time.deltaTime);
        simCS.SetVector("_Gravity", gravity);
        simCS.SetFloat("_Damping", damping);

        // Floor collision params
        simCS.SetFloat("_FloorY", floorY);
        simCS.SetFloat("_Restitution", restitution);
        simCS.SetFloat("_GroundFriction", groundFriction);

        // Sphere count safety
        simCS.SetInt("_SphereCount", (sphereBuffer != null) ? sphereBuffer.count : 0);

        int groups = Mathf.CeilToInt(maxParticles / (float)THREADS_X);
        simCS.Dispatch(kernel, groups, 1, 1);

        // Render params
        renderMat.SetFloat("_UseHeightGradient", useHeightGradient ? 1f : 0f);
        renderMat.SetFloat("_GradientMinY", gradientMinY);
        renderMat.SetFloat("_GradientMaxY", gradientMaxY);
        renderMat.SetColor("_LowColor", lowColor);
        renderMat.SetColor("_HighColor", highColor);

        // Draw
        var bounds = new Bounds(Vector3.zero, Vector3.one * 5000f);
        Graphics.DrawProcedural(renderMat, bounds, MeshTopology.Points, 1, maxParticles);

        // CPU analysis while GPU runs (async readback)
        if (enableCpuAnalysis)
        {
            TickCpuAnalysis();
        }
    }

    /// <summary>
    /// Periodically request async GPU readback of a sample of particles to compute CPU-side stats (min/max/avg height, histogram).
    /// </summary>
    void TickCpuAnalysis()
    {
        // Update bins array size if changed in inspector
        if (cpuHistogram == null || cpuHistogram.Length != histogramBins)
            cpuHistogram = new int[Mathf.Max(1, histogramBins)];

        // Do not spam requests
        if (Time.time < nextAnalysisTime) return;
        if (readbackInFlight) return;

        nextAnalysisTime = Time.time + analysisInterval;
        readbackInFlight = true;

        // Sample count clamp
        int sampleCount = Mathf.Clamp(analysisSampleCount, 1, maxParticles);
        int stride = Marshal.SizeOf(typeof(Particle));
        int byteCount = sampleCount * stride;

        // We read the first N particles for simplicity
        var t0 = Time.realtimeSinceStartup;

        // 1) Readback counter
        AsyncGPUReadback.Request(counterBuffer, (req) =>
        {
            if (!req.hasError)
            {
                var data = req.GetData<uint>();
                if (data.Length > 0) lastSpawnedThisFrame = data[0];
            }
        });

        // 2) Readback sample of particles
        AsyncGPUReadback.Request(particleBuffer, (req) =>
        {
            lastAnalysisMs = (Time.realtimeSinceStartup - t0) * 1000f;

            if (req.hasError)
            {
                readbackInFlight = false;
                return;
            }

            var data = req.GetData<Particle>();

            // sampleCount clamp
            int count = Mathf.Min(analysisSampleCount, data.Length);
            lastSampleUsed = count;

            float minY = float.PositiveInfinity;
            float maxY = float.NegativeInfinity;
            double sumY = 0.0;

            for (int i = 0; i < cpuHistogram.Length; i++) cpuHistogram[i] = 0;

            float range = Mathf.Max(1e-5f, histMaxY - histMinY);

            int aliveCount = 0;
            for (int i = 0; i < count; i++)
            {
                float life = data[i].life;
                if (life <= 0f) continue;

                float y = data[i].pos.y;

                minY = Mathf.Min(minY, y);
                maxY = Mathf.Max(maxY, y);
                sumY += y;
                aliveCount++;

                float t = Mathf.Clamp01((y - histMinY) / range);
                int bin = Mathf.Clamp((int)(t * (cpuHistogram.Length - 1)), 0, cpuHistogram.Length - 1);
                cpuHistogram[bin]++;
            }

            if (aliveCount == 0)
            {
                cpuMinY = cpuMaxY = cpuAvgY = 0f;
            }
            else
            {
                cpuMinY = minY;
                cpuMaxY = maxY;
                cpuAvgY = (float)(sumY / aliveCount);
            }

            readbackInFlight = false;
        });
    }

    /// <summary>
    /// Convert mouse screen position to world position on the plane Z=0.
    /// </summary>
    Vector3 GetMouseWorldOnPlaneZ0(Vector2 mouseScreenPos)
    {
        Ray r = cam.ScreenPointToRay(mouseScreenPos);
        Plane plane = new Plane(Vector3.forward, Vector3.zero);
        return plane.Raycast(r, out float t) ? r.GetPoint(t) : Vector3.zero;
    }

    void OnGUI()
    {
        if (!enableCpuAnalysis) return;

        GUILayout.BeginArea(new Rect(10, 10, 360, 220), GUI.skin.box);
        GUILayout.Label("<b>CPU Analysis (async sample)</b>", new GUIStyle(GUI.skin.label) { richText = true });

        GUILayout.Label($"Sample used: {lastSampleUsed} / {maxParticles}");
        GUILayout.Label($"Spawned (counter): {lastSpawnedThisFrame}");
        GUILayout.Label($"Y min/avg/max: {cpuMinY:F2} / {cpuAvgY:F2} / {cpuMaxY:F2}");
        GUILayout.Label($"Last readback+analysis: {lastAnalysisMs:F2} ms");

        // Simple histogram bar text
        if (cpuHistogram != null && cpuHistogram.Length > 0)
        {
            int maxBin = 1;
            for (int i = 0; i < cpuHistogram.Length; i++) maxBin = Mathf.Max(maxBin, cpuHistogram[i]);

            GUILayout.Label("Histogram (alive by Y):");
            for (int i = 0; i < cpuHistogram.Length; i++)
            {
                int bar = Mathf.RoundToInt(20f * cpuHistogram[i] / maxBin);
                GUILayout.Label($"{i:00}: " + new string('|', bar));
            }
        }

        GUILayout.EndArea();
    }

    void OnDestroy()
    {
        if (particleBuffer != null) { particleBuffer.Release(); particleBuffer = null; }
        if (counterBuffer != null) { counterBuffer.Release(); counterBuffer = null; }
        if (sphereBuffer != null) { sphereBuffer.Release(); sphereBuffer = null; }
    }
}