using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace TA.SurfaceDustGpu
{
    /// <summary>Static-mesh surface emission. Particle state stays on the GPU after initialization.</summary>
    [ExecuteAlways]
    public sealed class SurfaceDustEmitter : MonoBehaviour
    {
        public MeshFilter source;
        public ComputeShader simulation;
        public Material dustMaterial;
        [Range(256, 262144)] public int particleCount = 32768;
        public Vector2 lifetime = new Vector2(4, 7);
        public Vector2 particleSize = new Vector2(0.006f, 0.018f);
        [Min(0)] public float outwardSpeed = 0.32f;
        [Min(0)] public float surfaceOffset = 0.025f;
        public Vector3 wind = new Vector3(0.18f, 0.22f, 0);
        [Min(0)] public float turbulence = 0.65f;
        [Min(0.01f)] public float flowFrequency = 1.1f;
        [Min(0)] public float drag = 1.2f;
        [ColorUsage(false, true)] public Color dustColor = new Color(0.85f, 0.68f, 0.40f, 0.45f);
        [Tooltip("Maximum distance from birth position. Also used to conservatively bound rendering.")]
        [Min(0.1f)] public float maxTravelDistance = 6;
        public bool paused;
        [Tooltip("Disable when a separate screen-space fluid renderer consumes this particle buffer.")]
        public bool drawBillboards = true;

        [StructLayout(LayoutKind.Sequential)]
        struct Triangle
        {
            public Vector4 aCdf, b, c, n0, n1, n2;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ParticleSnapshot
        {
            public Vector3 position;
            public float age;
            public Vector3 velocity;
            public float lifetime;
            public float size;
            public uint seed;
            public float travel;
            public float padding;
        }

        GraphicsBuffer triangles, particles;
        ComputeShader compute;
        MaterialPropertyBlock properties;
        int initializeKernel, updateKernel, allocatedCount;
        float simulationTime;
        Bounds birthBounds;
        public bool IsInitialized => particles != null;
        public int AllocatedCount => allocatedCount;
        public GraphicsBuffer ParticleBuffer => particles;

        void OnEnable()
        {
            if (Application.IsPlaying(gameObject)) TryInitialize();
        }

        void TryInitialize()
        {
            try { Initialize(); }
            catch (Exception exception)
            {
                Release();
                Debug.LogError($"Surface dust could not initialize: {exception.Message}", this);
                enabled = false;
            }
        }

        [ContextMenu("Rebuild GPU particles")]
        public void Initialize()
        {
            Release();
            try
            {
                if (!SystemInfo.supportsComputeShaders) throw new InvalidOperationException("Compute shaders are unsupported.");
                if (!source || !source.sharedMesh || !simulation || !dustMaterial)
                    throw new InvalidOperationException("Assign Source, Simulation and Dust Material.");
                Mesh mesh = source.sharedMesh;
                if (!mesh.isReadable) throw new InvalidOperationException("Enable Read/Write on the source mesh import settings.");
                if (!dustMaterial.shader.isSupported) throw new InvalidOperationException("Dust shader is unsupported.");

                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = mesh.normals;
                Matrix4x4 matrix = source.transform.localToWorldMatrix;
                var data = new List<Triangle>();
                double areaSum = 0;
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                {
                    if (mesh.GetTopology(submesh) != MeshTopology.Triangles) continue;
                    int[] indices = mesh.GetTriangles(submesh);
                    for (int i = 0; i < indices.Length; i += 3)
                    {
                        int ia = indices[i], ib = indices[i + 1], ic = indices[i + 2];
                        Vector3 a = vertices[ia], b = vertices[ib], c = vertices[ic];
                        double area = Vector3.Cross(matrix.MultiplyVector(b - a), matrix.MultiplyVector(c - a)).magnitude * 0.5;
                        if (area <= 1e-12) continue;
                        areaSum += area;
                        Vector3 face = Vector3.Cross(b - a, c - a).normalized;
                        data.Add(new Triangle
                        {
                            aCdf = new Vector4(a.x, a.y, a.z, (float)areaSum),
                            b = b, c = c,
                            n0 = normals.Length == vertices.Length ? normals[ia] : face,
                            n1 = normals.Length == vertices.Length ? normals[ib] : face,
                            n2 = normals.Length == vertices.Length ? normals[ic] : face
                        });
                    }
                }
                if (data.Count == 0) throw new InvalidOperationException("The source has no non-degenerate triangles.");
                for (int i = 0; i < data.Count; i++)
                {
                    Triangle triangle = data[i];
                    triangle.aCdf.w /= (float)areaSum;
                    data[i] = triangle;
                }
                allocatedCount = Mathf.Clamp(particleCount, 256, 262144);
                triangles = new GraphicsBuffer(GraphicsBuffer.Target.Structured, data.Count, 96);
                triangles.SetData(data);
                particles = new GraphicsBuffer(GraphicsBuffer.Target.Structured, allocatedCount, 48);
                compute = Instantiate(simulation); // Each emitter owns its compute bindings.
                compute.hideFlags = HideFlags.HideAndDontSave;
                initializeKernel = compute.FindKernel("Initialize");
                updateKernel = compute.FindKernel("Simulate");
                foreach (int kernel in new[] { initializeKernel, updateKernel })
                {
                    compute.SetBuffer(kernel, "_Triangles", triangles);
                    compute.SetBuffer(kernel, "_Particles", particles);
                }
                compute.SetInt("_TriangleCount", data.Count);
                compute.SetInt("_ParticleCount", allocatedCount);
                simulationTime = 0;
                birthBounds = CurrentSourceBounds();
                properties = new MaterialPropertyBlock();
                properties.SetBuffer("_Particles", particles);
                UploadParameters(0);
                compute.Dispatch(initializeKernel, (allocatedCount + 127) / 128, 1, 1);
            }
            catch { Release(); throw; }
        }

        void Update()
        {
            if (!Application.IsPlaying(gameObject) || !IsInitialized) return;
            if (!paused) StepSimulation(Time.deltaTime);
            SubmitDraw();
        }

        public void StepSimulation(float deltaTime)
        {
            if (!IsInitialized || deltaTime <= 0) return;
            // Bound catch-up work after a stall; this is an artistic simulation.
            float remaining = Mathf.Min(deltaTime, 0.1f);
            while (remaining > 0.000001f)
            {
                float step = Mathf.Min(remaining, 1f / 60f);
                simulationTime += step;
                UploadParameters(step);
                compute.Dispatch(updateKernel, (allocatedCount + 127) / 128, 1, 1);
                remaining -= step;
            }
            // Keep old emission positions visible when the source moves.
            birthBounds.Encapsulate(CurrentSourceBounds());
        }

        void UploadParameters(float deltaTime)
        {
            Matrix4x4 matrix = source.transform.localToWorldMatrix;
            compute.SetMatrix("_SourceToWorld", matrix);
            compute.SetMatrix("_NormalToWorld", matrix.inverse.transpose);
            compute.SetVector("_LifetimeSize", new Vector4(Mathf.Max(0.1f, lifetime.x),
                Mathf.Max(Mathf.Max(0.1f, lifetime.x), lifetime.y), Mathf.Max(0.001f, particleSize.x),
                Mathf.Max(Mathf.Max(0.001f, particleSize.x), particleSize.y)));
            compute.SetVector("_Emission", new Vector4(Mathf.Max(0, outwardSpeed), Mathf.Max(0, surfaceOffset),
                Mathf.Max(0.1f, maxTravelDistance), 0));
            compute.SetVector("_WindDrag", new Vector4(wind.x, wind.y, wind.z, Mathf.Max(0, drag)));
            compute.SetVector("_Flow", new Vector4(Mathf.Max(0, turbulence), Mathf.Max(0.01f, flowFrequency), simulationTime, deltaTime));
        }

        Bounds CurrentSourceBounds()
        {
            Bounds local = source.sharedMesh.bounds;
            Matrix4x4 m = source.transform.localToWorldMatrix;
            Vector3 e = local.extents;
            Vector3 x = m.MultiplyVector(new Vector3(e.x, 0, 0));
            Vector3 y = m.MultiplyVector(new Vector3(0, e.y, 0));
            Vector3 z = m.MultiplyVector(new Vector3(0, 0, e.z));
            return new Bounds(m.MultiplyPoint3x4(local.center), new Vector3(
                Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z)) * 2);
        }

        public void SubmitDraw(Camera targetCamera = null)
        {
            if (!IsInitialized || !dustMaterial || !drawBillboards) return;
            Bounds bounds = birthBounds;
            bounds.Expand(2 * (Mathf.Max(0.1f, maxTravelDistance) + surfaceOffset + Mathf.Max(particleSize.x, particleSize.y) * 2));
            properties.SetColor("_DustColor", dustColor);
            var parameters = new RenderParams(dustMaterial)
            {
                worldBounds = bounds, matProps = properties, camera = targetCamera,
                layer = gameObject.layer, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false
            };
            Graphics.RenderPrimitives(parameters, MeshTopology.Triangles, 6, allocatedCount);
        }

        // Explicit diagnostic readback only. Never called by the normal simulation/render loop.
        public ParticleSnapshot[] ReadbackForValidation()
        {
            var result = new ParticleSnapshot[allocatedCount];
            particles.GetData(result);
            return result;
        }

        void OnDisable() => Release();
        void OnDestroy() => Release();
        void Release()
        {
            particles?.Dispose(); particles = null;
            triangles?.Dispose(); triangles = null;
            if (compute)
            {
                if (Application.isPlaying) Destroy(compute); else DestroyImmediate(compute);
            }
            compute = null;
            properties = null;
            allocatedCount = 0;
        }
    }
}
