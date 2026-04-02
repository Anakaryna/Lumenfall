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
    public enum PrecipitationType
    {
        Rain = 0,
        Snow = 1
    }

    [System.Serializable]
    public class PrecipitationSettings
    {
        [Header("Spawn")]
        public int spawnPerFrame = 1200;
        public float spawnLife = 2.8f;
        public float fallSpeed = 18f;
        public float spawnJitterY = 0.15f;
        public float speedRandomness = 2.5f;

        [Header("Motion")]
        public Vector3 wind = new Vector3(1.0f, 0f, 0.25f);
        public Vector3 gravity = new Vector3(0f, -28f, 0f);
        [Range(0f, 10f)] public float damping = 0.05f;
        public float lateralRandomness = 0.35f;

        [Header("Ground")]
        [Range(0f, 1f)] public float restitution = 0.02f;
        [Range(0f, 1f)] public float groundFriction = 0.35f;

        [Header("Rendering")]
        public Color color = new Color(0.72f, 0.82f, 0.95f, 0.22f);
        public float width = 0.012f;
        public float length = 0.20f;
        public float alphaBoost = 1.0f;
    }

    [Header("Refs")]
    public ComputeShader simCS;
    public Material renderMat;

    [Header("Capacity")]
    [Min(1)] public int maxParticles = 100_000;

    [Header("Emitter Zone")]
    public Transform rainZone;
    [Min(0.01f)] public float zoneRadius = 6f;
    public bool useRainZoneScale = true;
    [Min(0f)] public float cloudThickness = 2f;

    [Header("General")]
    public bool emitContinuously = true;
    public bool systemVisible = true;
    public PrecipitationType precipitationType = PrecipitationType.Rain;

    [Header("Recycling")]
    public float killBelowY = -2f;

    [Header("Floor Collision")]
    public bool collideWithFloor = true;
    public float floorY = 0f;

    [Header("Collision Spheres")]
    [Tooltip("Optional simple colliders: each Vector4 = (x,y,z,r)")]
    public Vector4[] spheres = new Vector4[0];

    [Header("Rain Settings")]
    public PrecipitationSettings rain = new PrecipitationSettings
    {
        spawnPerFrame = 1200,
        spawnLife = 2.8f,
        fallSpeed = 18f,
        spawnJitterY = 0.15f,
        speedRandomness = 2.5f,
        wind = new Vector3(1.0f, 0f, 0.25f),
        gravity = new Vector3(0f, -28f, 0f),
        damping = 0.05f,
        lateralRandomness = 0.35f,
        restitution = 0.02f,
        groundFriction = 0.35f,
        color = new Color(0.72f, 0.82f, 0.95f, 0.22f),
        width = 0.012f,
        length = 0.20f,
        alphaBoost = 1.0f
    };

    [Header("Snow Settings")]
    public PrecipitationSettings snow = new PrecipitationSettings
    {
        spawnPerFrame = 900,
        spawnLife = 8.0f,
        fallSpeed = 1.2f,
        spawnJitterY = 0.35f,
        speedRandomness = 0.35f,
        wind = new Vector3(0.35f, 0f, 0.12f),
        gravity = new Vector3(0f, -1.2f, 0f),
        damping = 1.2f,
        lateralRandomness = 0.75f,
        restitution = 0.0f,
        groundFriction = 0.95f,
        color = new Color(1f, 1f, 1f, 0.9f),
        width = 0.035f,
        length = 0.035f,
        alphaBoost = 1.0f
    };

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
        public float type;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Sphere
    {
        public Vector3 center;
        public float radius;
    }

    public void SetVisible(bool visible)
    {
        systemVisible = visible;
    }

    public void SetPrecipitationType(PrecipitationType type)
    {
        precipitationType = type;
    }

    public void StopEmission()
    {
        emitContinuously = false;
    }

    public void StartEmission()
    {
        emitContinuously = true;
    }

    public void ClearParticlesNow()
    {
        if (particleBuffer == null) return;

        var init = new Particle[maxParticles];
        for (int i = 0; i < maxParticles; i++)
        {
            init[i].pos = Vector3.zero;
            init[i].vel = Vector3.zero;
            init[i].life = 0f;
            init[i].type = 0f;
        }
        particleBuffer.SetData(init);
    }

    PrecipitationSettings ActiveSettings =>
        precipitationType == PrecipitationType.Rain ? rain : snow;

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
            init[i].type = 0f;
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
        if (particleBuffer == null || simCS == null || renderMat == null) return;

        simCS.Dispatch(clearKernel, 1, 1, 1);

        Transform t = rainZone ? rainZone : transform;
        Vector3 zoneCenter = t.position;

        float finalRadius = zoneRadius;
        float finalThickness = cloudThickness;

        if (rainZone && useRainZoneScale)
        {
            Vector3 s = rainZone.lossyScale;
            float horizontalScale = (Mathf.Abs(s.x) + Mathf.Abs(s.z)) * 0.5f;
            finalRadius *= horizontalScale;
            finalThickness *= Mathf.Abs(s.y);
        }

        float emitY = zoneCenter.y - finalThickness * 0.5f;

        PrecipitationSettings sActive = ActiveSettings;

        simCS.SetInt("_EmitOn", (systemVisible && emitContinuously) ? 1 : 0);
        simCS.SetInt("_EmitCount", (systemVisible && emitContinuously) ? sActive.spawnPerFrame : 0);

        simCS.SetVector("_EmitCenter", zoneCenter);
        simCS.SetFloat("_EmitRadius", finalRadius);
        simCS.SetFloat("_EmitY", emitY);
        simCS.SetFloat("_EmitLife", sActive.spawnLife);
        simCS.SetFloat("_FallSpeed", sActive.fallSpeed);
        simCS.SetFloat("_SpawnJitterY", sActive.spawnJitterY);
        simCS.SetFloat("_SpeedRandomness", sActive.speedRandomness);
        simCS.SetVector("_Wind", sActive.wind);
        simCS.SetVector("_Gravity", sActive.gravity);
        simCS.SetFloat("_Damping", sActive.damping);
        simCS.SetFloat("_LateralRandomness", sActive.lateralRandomness);
        simCS.SetInt("_FrameIndex", (int)frameIndex++);
        simCS.SetInt("_SpawnType", precipitationType == PrecipitationType.Rain ? 0 : 1);

        simCS.SetFloat("_DeltaTime", Time.deltaTime);

        simCS.SetInt("_UseFloor", collideWithFloor ? 1 : 0);
        simCS.SetFloat("_FloorY", floorY);
        simCS.SetFloat("_Restitution", sActive.restitution);
        simCS.SetFloat("_GroundFriction", sActive.groundFriction);
        simCS.SetFloat("_KillBelowY", killBelowY);

        simCS.SetInt("_SphereCount", (sphereBuffer != null) ? sphereBuffer.count : 0);

        int groups = Mathf.CeilToInt(maxParticles / (float)THREADS_X);
        simCS.Dispatch(kernel, groups, 1, 1);

        if (systemVisible)
        {
            renderMat.SetFloat("_DropWidth", sActive.width);
            renderMat.SetFloat("_DropLength", sActive.length);
            renderMat.SetColor("_Color", sActive.color);
            renderMat.SetFloat("_AlphaBoost", sActive.alphaBoost);

            var bounds = new Bounds(zoneCenter, Vector3.one * 10000f);
            Graphics.DrawProcedural(renderMat, bounds, MeshTopology.Points, 1, maxParticles);
        }
    }

    void OnDrawGizmosSelected()
    {
        if (!drawRainZoneGizmo) return;

        Transform t = rainZone ? rainZone : transform;

        float finalRadius = zoneRadius;
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