using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace TA.ScreenGridSmoke
{
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(Camera))]
    public sealed class ScreenGridSmokeView : MonoBehaviour
    {
        public enum DisplayMode { Smoke, Density, Velocity, Pressure, Vorticity, SourceMask }
        public MeshFilter source;
        [Range(64, 512)] public int gridHeight = 288;
        [Range(20, 100)] public int pressureIterations = 50;
        [Min(0)] public float emission = 4;
        [Min(0)] public float outwardForce = 0.14f;
        [Min(0)] public float buoyancy = 0.08f;
        [Min(0)] public float vorticity = 4;
        [Min(0)] public float vortexStrength = 1.5f;
        [Min(0)] public float dissipation = 0.28f;
        [Min(0)] public float velocityDrag = 0.12f;
        public Vector2 wind = new Vector2(0.004f, 0.004f);
        public Color smokeColor = new Color(0.18f, 0.7f, 0.82f);
        [Range(0.1f, 5)] public float opacity = 1.3f;
        public DisplayMode display;
        public bool paused, showControls = true;
        public bool resetOnCameraChange = true;
        [HideInInspector] public float validationDeltaTime;

        public RTHandle[] Velocity { get; private set; }
        public RTHandle[] Dye { get; private set; }
        public RTHandle[] Pressure { get; private set; }
        public RTHandle Curl { get; private set; }
        public RTHandle Divergence { get; private set; }
        public RTHandle ProjectedDivergence { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int CurrentDye { get; set; }
        public bool NeedsReset { get; private set; } = true;
        public int ResetCount { get; private set; }
        public float SimulationTime { get; set; }
        Matrix4x4 lastView, lastProjection;
        bool hasCamera;
        static readonly string[] Modes = { "Smoke", "Density", "Velocity", "Pressure", "Vorticity", "Source mask" };

        public void Prepare(Camera camera, int targetWidth, int targetHeight)
        {
            int height = Mathf.Clamp(gridHeight, 64, 512);
            int width = Mathf.Clamp(Mathf.CeilToInt(height * targetWidth / (float)targetHeight / 8) * 8, 64, 1536);
            if (Velocity == null || Width != width || Height != height)
            {
                Release(); Width = width; Height = height;
                Velocity = new[] { Allocate("Velocity A", GraphicsFormat.R32G32_SFloat), Allocate("Velocity B", GraphicsFormat.R32G32_SFloat) };
                Dye = new[] { Allocate("Dye A", GraphicsFormat.R16G16B16A16_SFloat), Allocate("Dye B", GraphicsFormat.R16G16B16A16_SFloat) };
                Pressure = new[] { Allocate("Pressure A", GraphicsFormat.R32_SFloat), Allocate("Pressure B", GraphicsFormat.R32_SFloat) };
                Curl = Allocate("Curl", GraphicsFormat.R32_SFloat);
                Divergence = Allocate("Divergence before", GraphicsFormat.R32_SFloat);
                ProjectedDivergence = Allocate("Divergence after", GraphicsFormat.R32_SFloat);
                ResetSimulation();
            }
            if (resetOnCameraChange && hasCamera &&
                (Different(lastView, camera.worldToCameraMatrix) || Different(lastProjection, camera.projectionMatrix))) ResetSimulation();
            lastView = camera.worldToCameraMatrix; lastProjection = camera.projectionMatrix; hasCamera = true;
        }

        RTHandle Allocate(string label, GraphicsFormat format)
        {
            var texture = new RenderTexture(new RenderTextureDescriptor(Width, Height)
            {
                graphicsFormat = format, depthBufferBits = 0, msaaSamples = 1,
                enableRandomWrite = true, useMipMap = false, autoGenerateMips = false, sRGB = false
            }) { name = "Screen Grid Smoke / " + label, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave };
            texture.Create();
            return RTHandles.Alloc(texture);
        }

        static bool Different(Matrix4x4 a, Matrix4x4 b)
        {
            for (int i = 0; i < 16; i++) if (Mathf.Abs(a[i] - b[i]) > 0.00001f) return true;
            return false;
        }

        public void ResetSimulation() { NeedsReset = true; SimulationTime = 0; CurrentDye = 0; ResetCount++; }
        public void MarkResetRecorded() => NeedsReset = false;
        public float GetDeltaTime() => paused ? 0 : Mathf.Min(validationDeltaTime > 0 ? validationDeltaTime : Time.deltaTime, 1f / 30f);

        void OnDisable() => Release();
        void OnDestroy() => Release();
        void Release()
        {
            void Free(RTHandle handle)
            {
                if (handle == null) return;
                RenderTexture texture = handle.rt;
                handle.Release();
                if (texture)
                {
                    texture.Release();
                    if (Application.isPlaying) Destroy(texture); else DestroyImmediate(texture);
                }
            }
            if (Velocity != null) foreach (var texture in Velocity) Free(texture);
            if (Dye != null) foreach (var texture in Dye) Free(texture);
            if (Pressure != null) foreach (var texture in Pressure) Free(texture);
            Free(Curl); Free(Divergence); Free(ProjectedDivergence);
            Velocity = null; Dye = null; Pressure = null; Curl = null; Divergence = null; ProjectedDivergence = null;
            Width = Height = 0; hasCamera = false; NeedsReset = true;
        }

        void OnGUI()
        {
            if (!Application.IsPlaying(gameObject) || !showControls) return;
            GUILayout.BeginArea(new Rect(18, 18, 290, 360), GUI.skin.box);
            GUILayout.Label("SCREEN GRID SMOKE / 2D FLUID");
            GUILayout.Label($"Grid {Width} x {Height}    Resets: {ResetCount}");
            display = (DisplayMode)GUILayout.SelectionGrid((int)display, Modes, 2);
            GUILayout.Label($"Vorticity confinement  {vorticity:F1}");
            vorticity = GUILayout.HorizontalSlider(vorticity, 0, 30);
            GUILayout.Label($"Vortex force  {vortexStrength:F1}");
            vortexStrength = GUILayout.HorizontalSlider(vortexStrength, 0, 4);
            GUILayout.Label($"Emission  {emission:F1}");
            emission = GUILayout.HorizontalSlider(emission, 0, 10);
            if (GUILayout.Button(paused ? "Resume" : "Pause")) paused = !paused;
            if (GUILayout.Button("Reset smoke")) ResetSimulation();
            if (GUILayout.Button("Rotate camera +15 degrees (resets grid)"))
            {
                Vector3 center = source ? source.transform.TransformPoint(source.sharedMesh.bounds.center) : Vector3.zero;
                transform.RotateAround(center, Vector3.up, 15);
            }
            GUILayout.EndArea();
        }
    }
}
