using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace TA.PixelCloudLandscape
{
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(Camera))]
    public sealed class LandscapeClouds : MonoBehaviour
    {
        public Material cloudTemplate;
        [Range(64, 420)] public int pixelHeight = 180;
        [Range(0, 1)] public float cover = 0.53f;
        [Range(0, 1)] public float layered = 0.15f;
        [Range(0, 1)] public float storm = 0.04f;
        [Range(100, 2200)] public float cloudBase = 680;
        [Range(100, 2200)] public float thickness = 780;
        [Range(0.35f, 4)] public float cloudSize = 1.35f;
        [Range(0, 2)] public float erosion = 0.8f;
        [Range(0, 1)] public float shadowStrength = 0.8f;
        [Range(24, 144)] public int samples = 80;
        public Vector2 mapOffset;
        public Vector2 windVelocity = new(10, 4);
        public bool animateWind;
        [Range(0, 600)] public float previewSeconds;

        public const int MaxBands = 32;
        [System.Serializable]
        public struct LightBand
        {
            public float start;
            public Color color;
            public LightBand(float start, Color color) { this.start = start; this.color = color; }
        }

        [System.Serializable]
        public struct ViewBand
        {
            public float start;
            [Range(0, 1)] public float opacity;
            public ViewBand(float start, float opacity) { this.start = start; this.opacity = opacity; }
        }

        public enum ThicknessPreview { Composite, Brightness, ViewThickness }
        public bool useThicknessBands = true;
        public ThicknessPreview thicknessPreview;
        [Range(2, 24)] public int lightSamples = 8;
        [SerializeField, HideInInspector, UnityEngine.Serialization.FormerlySerializedAs("lightBands")]
        LightBand[] previousThicknessColors;
        public LightBand[] brightnessBands = {
            new(0, new Color(0.50f, 0.59f, 0.72f)),
            new(0.31f, new Color(0.66f, 0.71f, 0.77f)),
            new(0.53f, new Color(0.83f, 0.84f, 0.81f)),
            new(0.74f, new Color(1f, 0.965f, 0.86f))
        };
        public bool useBezierLighting;
        [Min(0)] public int colorSteps = 5;
        // x is the evenly spaced band boundary, y is its authored brightness.
        // Endpoints (0,0) and (1,1) are implicit. Both coordinates increase strictly.
        [SerializeField, HideInInspector] Vector2[] bandSpacing = System.Array.Empty<Vector2>();
        public System.ReadOnlySpan<Vector2> BandSpacing => bandSpacing;
        GraphicsBuffer spacingBuffer;
        bool spacingDirty = true;

        public static float MapBandSpacing(System.ReadOnlySpan<Vector2> knots, float value, bool inverse)
        {
            int axis = inverse ? 1 : 0;
            int lo = 0, hi = knots.Length;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (knots[mid][axis] <= value) lo = mid + 1;
                else hi = mid;
            }
            Vector2 a = lo == 0 ? Vector2.zero : knots[lo - 1];
            Vector2 b = lo == knots.Length ? Vector2.one : knots[lo];
            return Mathf.Lerp(a[1-axis], b[1-axis], Mathf.InverseLerp(a[axis], b[axis], value));
        }

        void OnValidate() => spacingDirty = true;

        void BindSpacing(Material material)
        {
            int count = BandSpacing.Length;
            int capacity = Mathf.Max(1, count);
            if (spacingBuffer == null || spacingBuffer.count != capacity)
            {
                spacingBuffer?.Release();
                spacingBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(float)*2);
                spacingDirty = true;
            }
            if (spacingDirty && count > 0) spacingBuffer.SetData(bandSpacing);
            spacingDirty = false;
            material.SetBuffer("_CloudBandSpacing", spacingBuffer);
            material.SetInt("_SpacingCount", count);
        }

        public Color darkControl = new(0.55f, 0.60f, 0.84f);
        public Color lightControl = new(1f, 0.87f, 0.69f);
        static readonly Vector3[] SkyDirection = { Vector3.up };
        readonly Color[] skySample = new Color[1];
        public ViewBand[] viewBands = {
            new(0, 0), new(0.16f, 0.35f), new(0.48f, 0.75f), new(1.1f, 1)
        };
        readonly Vector4[] lightTable = new Vector4[MaxBands];
        readonly Vector4[] viewTable = new Vector4[MaxBands];

        void BindBands(Material material)
        {
            int colors = Mathf.Min(brightnessBands?.Length ?? 0, MaxBands);
            int alphas = Mathf.Min(viewBands?.Length ?? 0, MaxBands);
            for (int i = 0; i < colors; ++i)
            {
                var band = brightnessBands[i];
                lightTable[i] = new Vector4(band.color.r, band.color.g, band.color.b, band.start);
            }
            for (int i = 0; i < alphas; ++i)
                viewTable[i] = new Vector4(viewBands[i].start, Mathf.Clamp01(viewBands[i].opacity), 0, 0);
            if (colors == 0) lightTable[0] = Vector4.one;
            if (alphas == 0) viewTable[0] = new Vector4(0, 1, 0, 0);
            material.SetVectorArray("_LightBands", lightTable);
            material.SetVectorArray("_ViewBands", viewTable);
            material.SetVector("_BandCounts", new Vector4(Mathf.Max(1, colors), Mathf.Max(1, alphas),
                Mathf.Clamp(lightSamples, 2, 24), useThicknessBands ? 1 : 0));
            material.SetInt("_ThicknessPreview", useThicknessBands ? (int)thicknessPreview : 0);
        }

        // The sky-facing irradiance provides one shared fill color for the discrete palette.
        Color SkyFill()
        {
            switch (RenderSettings.ambientMode)
            {
                case AmbientMode.Flat: return RenderSettings.ambientLight.linear;
                case AmbientMode.Trilight: return RenderSettings.ambientSkyColor.linear;
                default:
                    RenderSettings.ambientProbe.Evaluate(SkyDirection, skySample);
                    return skySample[0];
            }
        }

        // Keep the serialized color controls compatible with the original inspector.
        // Outside the old unit interval, store the response directly to preserve signed values.
        public static float DecodeControl(float stored) =>
            stored > 0 && stored < 1 ? Mathf.GammaToLinearSpace(stored) : stored;

        public static float EncodeControl(float linear) =>
            linear > 0 && linear < 1 ? Mathf.LinearToGammaSpace(linear) : linear;

        public static Vector3 CurvePoint(Color stored, bool bright)
        {
            Vector3 linear = new(DecodeControl(stored.r), DecodeControl(stored.g), DecodeControl(stored.b));
            return .5f * linear + (bright ? .5f * Vector3.one : Vector3.zero);
        }

        void BindBezier(Material material)
        {
            material.SetInt("_BezierSteps", useThicknessBands && useBezierLighting
                ? Mathf.Max(0, colorSteps) : -1);
            if (!useThicknessBands || !useBezierLighting) return;

            material.SetVector("_CloudSky", SkyFill());
            material.SetVector("_DarkControl", CurvePoint(darkControl, false));
            material.SetVector("_LightControl", CurvePoint(lightControl, true));
        }

        Material instance;
        Camera sceneCamera;
        CloudPass pass;

        void OnEnable()
        {
            // Previous scenes stored the same palette from thin/lit to thick/dark.
            if (previousThicknessColors is { Length: > 0 })
            {
                int count = Mathf.Min(previousThicknessColors.Length, MaxBands);
                brightnessBands = new LightBand[count];
                for (int i = 0; i < count; ++i)
                {
                    int old = count - 1 - i;
                    float start = i == 0 ? 0 : Mathf.Exp(-previousThicknessColors[old + 1].start);
                    brightnessBands[i] = new LightBand(start, previousThicknessColors[old].color);
                }
                previousThicknessColors = null;
            }
            sceneCamera = GetComponent<Camera>();
            pass = new CloudPass();
            RenderPipelineManager.beginCameraRendering += Enqueue;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= Enqueue;
            spacingBuffer?.Release();
            spacingBuffer = null;
            CoreUtils.Destroy(instance);
        }

        void Enqueue(ScriptableRenderContext context, Camera camera)
        {
            bool sceneView = camera.cameraType == CameraType.SceneView &&
                             gameObject.scene == SceneManager.GetActiveScene();
            if ((!sceneView && camera != sceneCamera) || !cloudTemplate) return;
            if (!instance) instance = new Material(cloudTemplate) { hideFlags = HideFlags.HideAndDontSave };
            BindSpacing(instance);
            BindBezier(instance);
            instance.SetVector("_Weather", new Vector4(cover, layered, storm, erosion));
            instance.SetVector("_Layer", new Vector4(cloudBase, thickness, cloudSize, shadowStrength));
            instance.SetVector("_Flow", new Vector4(mapOffset.x, mapOffset.y, windVelocity.x, windVelocity.y));
            instance.SetVector("_Clock", new Vector4(previewSeconds, animateWind ? 1 : 0, samples, 0));
            BindBands(instance);
            pass.material = instance;
            pass.height = pixelHeight;
            camera.GetUniversalAdditionalCameraData().scriptableRenderer.EnqueuePass(pass);
        }

        sealed class CloudPass : ScriptableRenderPass
        {
            internal Material material;
            internal int height;
            sealed class Data { internal TextureHandle source, depth; internal Material material; internal MaterialPropertyBlock props; }

            readonly MaterialPropertyBlock props = new();

            public CloudPass()
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
                requiresIntermediateTexture = true;
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
            {
                var resources = frame.Get<UniversalResourceData>();
                var cameraData = frame.Get<UniversalCameraData>();
                if (resources.isActiveTargetBackBuffer || !resources.cameraDepthTexture.IsValid()) return;
                var source = resources.activeColorTexture;
                var desc = graph.GetTextureDesc(source);
                var cameraDesc = cameraData.cameraTargetDescriptor;
                // Equal integer enlargement on both axes wherever the viewport divides evenly.
                int scale = Mathf.Max(1, Mathf.RoundToInt(cameraDesc.height / (float)height));
                desc.sizeMode = TextureSizeMode.Explicit;
                desc.width = Mathf.Max(1, Mathf.CeilToInt(cameraDesc.width / (float)scale));
                desc.height = Mathf.Max(1, Mathf.CeilToInt(cameraDesc.height / (float)scale));
                desc.name = "Pixel cloud landscape";
                desc.msaaSamples = MSAASamples.None;
                desc.filterMode = FilterMode.Point;
                desc.clearBuffer = false;
                var pixels = graph.CreateTexture(desc);
                material.SetFloat("_PixelAngle", 2f / (Mathf.Abs(cameraData.GetProjectionMatrix().m11) * desc.height));
                using (var builder = graph.AddRasterRenderPass<Data>("Clouds and terrain on pixel grid", out var data))
                {
                    data.source = source;
                    data.material = material;
                    data.depth = resources.cameraDepthTexture;
                    data.props = props;
                    builder.UseTexture(source);
                    builder.UseTexture(resources.cameraDepthTexture);
                    builder.SetRenderAttachment(pixels, 0, AccessFlags.Write);
                    builder.SetRenderFunc(static (Data d, RasterGraphContext ctx) =>
                    {
                        d.props.SetTexture("_BlitTexture", d.source);
                        d.props.SetTexture("_LandscapeDepth", d.depth);
                        d.props.SetVector("_BlitScaleBias", new Vector4(1,1,0,0));
                        ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, 0, MeshTopology.Triangles, 3, 1, d.props);
                    });
                }
                graph.AddBlitPass(pixels, source, Vector2.one, Vector2.zero,
                    filterMode: RenderGraphUtils.BlitFilterMode.ClampNearest, passName: "Enlarge landscape pixels");
            }
        }
    }
}


