using UnityEngine;
using System.Runtime.InteropServices;
using UnityEngine.Rendering;

/// <summary>
/// GPU rain particle system:
/// - 3D rain emission inside a horizontal circular zone
/// - Downward rain with optional wind
/// - Floor + sphere collisions
/// - Procedural rendering as transparent rain streaks
/// </summary>
public class GpuParticles : MonoBehaviour
{
    [Header("Refs")]
    public ComputeShader simCS;
    public Material renderMat;

    [Header("Capacity")]
    [Min(1)] public int maxParticles = 100_000;

    [Header("Rain Emitter (cloud zone)")]
    [Tooltip("Place this transform inside your cloud. Rain spawns below it.")]
    public Transform rainZone;

    [Tooltip("Horizontal radius of the rain emission disc.")]
    [Min(0.01f)] public float rainRadius = 6f;

    [Tooltip("Optional scale multiplier from the rainZone transform X/Z scale.")]
    public bool useRainZoneScale = true;

    [Tooltip("Cloud thickness. Rain emits from the bottom of this thickness.")]
    [Min(0f)] public float cloudThickness = 2f;

    [Tooltip("Automatically emit every frame.")]
    public bool emitContinuously = true;

    [Tooltip("How many new drops to try to spawn per frame.")]
    public int spawnPerFrame = 1200;

    [Tooltip("Lifetime of each rain particle.")]
    public float spawnLife = 2.8f;

    [Tooltip("Base falling speed at spawn.")]
    public float rainFallSpeed = 18f;

    [Tooltip("Random vertical jitter at emission.")]
    public float spawnJitterY = 0.15f;

    [Tooltip("Extra random speed variation.")]
    public float speedRandomness = 2.5f;

    [Header("Weather")]
    public Vector3 wind = new Vector3(1.0f, 0f, 0.25f);
    public Vector3 gravity = new Vector3(0f, -28f, 0f);
    [Range(0f, 10f)] public float damping = 0.05f;

    [Header("Recycling")]
    [Tooltip("When a particle goes below this Y, it dies and can be respawned.")]
    public float killBelowY = -2f;

    [Header("Floor Collision")]
    public bool collideWithFloor = true;
    public float floorY = 0f;
    [Range(0f, 1f)] public float restitution = 0.02f;
    [Range(0f, 1f)] public float groundFriction = 0.35f;

    [Header("Collision Spheres")]
    [Tooltip("Optional simple colliders: each Vector4 = (x,y,z,r)")]
    public Vector4[] spheres = new Vector4[0];

    [Header("Rendering")]
    public Color rainColor = new Color(0.72f, 0.82f, 0.95f, 0.22f);
    public float dropWidth = 0.012f;
    public float dropLength = 0.20f;
    public float alphaBoost = 1.0f;

    [Header("Debug")]
    public bool drawRainZoneGizmo = true;

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

    void Start()
    {
        if (simCS == null)
        {
            Debug.LogError("GpuParticles: Missing ComputeShader.");
            enabled = false;
            return;
        }

        if (renderMat == null)
        {
            Debug.LogError("GpuParticles: Missing render material.");
            enabled = false;
            return;
        }

        kernel = simCS.FindKernel("CSMain");
        clearKernel = simCS.FindKernel("CSClear");

        int stride = Marshal.SizeOf(typeof(Particle));
        particleBuffer = new ComputeBuffer(maxParticles, stride, ComputeBufferType.Structured);

        var init = new Particle[maxParticles];
        for (int i = 0; i < maxParticles; i++)
        {
            init[i].pos = Vector3.zero;
            init[i].vel = Vector3.zero;
            init[i].life = 0f;
            init[i].pad = 0f;
        }
        particleBuffer.SetData(init);

        counterBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Structured);
        counterBuffer.SetData(new uint[] { 0 });

        renderMat.SetBuffer("_Particles", particleBuffer);

        simCS.SetBuffer(kernel, "_Particles", particleBuffer);
        simCS.SetBuffer(clearKernel, "_Counters", counterBuffer);
        simCS.SetBuffer(kernel, "_Counters", counterBuffer);

        simCS.SetInt("_MaxParticles", maxParticles);

        UploadSpheres();
    }

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
        simCS.Dispatch(clearKernel, 1, 1, 1);

        Transform t = rainZone ? rainZone : transform;
        Vector3 zoneCenter = t.position;

        float finalRadius = rainRadius;
        float finalThickness = cloudThickness;

        if (rainZone && useRainZoneScale)
        {
            Vector3 s = rainZone.lossyScale;
            float horizontalScale = (Mathf.Abs(s.x) + Mathf.Abs(s.z)) * 0.5f;
            finalRadius *= horizontalScale;
            finalThickness *= Mathf.Abs(s.y);
        }

        float emitY = zoneCenter.y - finalThickness * 0.5f;

        simCS.SetInt("_EmitOn", emitContinuously ? 1 : 0);
        simCS.SetInt("_EmitCount", emitContinuously ? spawnPerFrame : 0);

        simCS.SetVector("_EmitCenter", zoneCenter);
        simCS.SetFloat("_EmitRadius", finalRadius);
        simCS.SetFloat("_EmitY", emitY);
        simCS.SetFloat("_EmitLife", spawnLife);
        simCS.SetFloat("_RainFallSpeed", rainFallSpeed);
        simCS.SetFloat("_SpawnJitterY", spawnJitterY);
        simCS.SetFloat("_SpeedRandomness", speedRandomness);
        simCS.SetVector("_Wind", wind);
        simCS.SetInt("_FrameIndex", (int)frameIndex++);

        simCS.SetFloat("_DeltaTime", Time.deltaTime);
        simCS.SetVector("_Gravity", gravity);
        simCS.SetFloat("_Damping", damping);

        simCS.SetInt("_UseFloor", collideWithFloor ? 1 : 0);
        simCS.SetFloat("_FloorY", floorY);
        simCS.SetFloat("_Restitution", restitution);
        simCS.SetFloat("_GroundFriction", groundFriction);
        simCS.SetFloat("_KillBelowY", killBelowY);

        simCS.SetInt("_SphereCount", (sphereBuffer != null) ? sphereBuffer.count : 0);

        int groups = Mathf.CeilToInt(maxParticles / (float)THREADS_X);
        simCS.Dispatch(kernel, groups, 1, 1);

        renderMat.SetFloat("_DropWidth", dropWidth);
        renderMat.SetFloat("_DropLength", dropLength);
        renderMat.SetColor("_Color", rainColor);
        renderMat.SetFloat("_AlphaBoost", alphaBoost);

        var bounds = new Bounds(zoneCenter, Vector3.one * 10000f);
        Graphics.DrawProcedural(renderMat, bounds, MeshTopology.Points, 1, maxParticles);
    }

    void OnDrawGizmosSelected()
    {
        if (!drawRainZoneGizmo) return;

        Transform t = rainZone ? rainZone : transform;

        float finalRadius = rainRadius;
        float finalThickness = cloudThickness;

        if (rainZone && useRainZoneScale)
        {
            Vector3 s = rainZone.lossyScale;
            float horizontalScale = (Mathf.Abs(s.x) + Mathf.Abs(s.z)) * 0.5f;
            finalRadius *= horizontalScale;
            finalThickness *= Mathf.Abs(s.y);
        }

        Vector3 center = t.position;
        float emitY = center.y - finalThickness * 0.5f;
        Vector3 discCenter = new Vector3(center.x, emitY, center.z);

        DrawHorizontalCircle(discCenter, finalRadius, new Color(0.2f, 0.6f, 1f, 0.95f));

        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.2f);
        Gizmos.DrawLine(center, discCenter);
    }

    void DrawHorizontalCircle(Vector3 center, float radius, Color color)
    {
        Gizmos.color = color;
        const int steps = 64;
        Vector3 prev = center + new Vector3(radius, 0f, 0f);

        for (int i = 1; i <= steps; i++)
        {
            float a = (i / (float)steps) * Mathf.PI * 2f;
            Vector3 p = center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            Gizmos.DrawLine(prev, p);
            prev = p;
        }
    }

    void OnDestroy()
    {
        if (particleBuffer != null) { particleBuffer.Release(); particleBuffer = null; }
        if (counterBuffer != null) { counterBuffer.Release(); counterBuffer = null; }
        if (sphereBuffer != null) { sphereBuffer.Release(); sphereBuffer = null; }
    }
}