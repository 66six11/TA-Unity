using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TA.SurfaceDustGpu;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace TA.ScreenSpaceFluid.Editor
{
    [InitializeOnLoad]
    public static class ScreenSpaceFluidDemoBuilder
    {
        const string Root = "Assets/ScreenSpaceFluid";
        const string ScenePath = Root + "/ScreenSpaceFluidDemo.unity";
        const string Request = "Library/ScreenSpaceFluid.request";
        const string Status = "Library/ScreenSpaceFluid-validation.txt";
        const int Layer = 28;

        static ScreenSpaceFluidDemoBuilder() => EditorApplication.update += Poll;
        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Request)) return;
            string command;
            try { command = File.ReadAllText(Request).Trim(); File.Delete(Request); }
            catch (IOException) { return; }
            try
            {
                if (command == "open")
                {
                    bool dirty = false;
                    for (int i = 0; i < SceneManager.sceneCount; i++) dirty |= SceneManager.GetSceneAt(i).isDirty;
                    Scene scene = SceneManager.GetSceneByPath(ScenePath);
                    if (!scene.IsValid() || !scene.isLoaded)
                        scene = EditorSceneManager.OpenScene(ScenePath, dirty ? OpenSceneMode.Additive : OpenSceneMode.Single);
                    SceneManager.SetActiveScene(scene);
                    Selection.activeObject = UnityEngine.Object.FindAnyObjectByType<ScreenSpaceFluidView>();
                    File.WriteAllText("Library/ScreenSpaceFluid-open.txt", "Opened " + scene.path);
                }
                else BuildAndVerify();
            }
            catch (Exception e) { File.WriteAllText(Status, e.ToString()); Debug.LogException(e); }
        }

        [MenuItem("Tools/TA/Screen Space Fluid/Open Demo")]
        public static void OpenDemo()
        {
            if (!File.Exists(ScenePath)) BuildAndVerify();
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Tools/TA/Screen Space Fluid/Build and Verify Demo")]
        public static void BuildAndVerify()
        {
            Directory.CreateDirectory(Root + "/Generated");
            Directory.CreateDirectory(Root + "/Preview");
            AssetDatabase.Refresh();
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Shaders/ScreenSpaceFluid.shader");
            if (!shader) throw new InvalidOperationException("Fluid shader is not imported.");
            var compileMaterial = new Material(shader);
            try { for (int i = 0; i < compileMaterial.passCount; i++) ShaderUtil.CompilePass(compileMaterial, i, true); }
            finally { UnityEngine.Object.DestroyImmediate(compileMaterial); }
            if (ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Screen-space fluid shader compilation failed.");
            int rendererIndex = InstallRenderer(shader);
            var report = new StringBuilder();
            report.AppendLine($"Unity {Application.unityVersion}; {SystemInfo.graphicsDeviceName}; {SystemInfo.graphicsDeviceType}");
            report.AppendLine("Dedicated URP renderer index: " + rendererIndex);
            Scene original = SceneManager.GetActiveScene();
            var suspended = new List<Light>();
            foreach (var light in UnityEngine.Object.FindObjectsByType<Light>())
                if (light.enabled) { suspended.Add(light); light.enabled = false; }
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                RenderSettings.skybox = null; RenderSettings.fog = false;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.2f, 0.23f, 0.28f);
                var lit = Shader.Find("Universal Render Pipeline/Lit");
                var gold = MaterialAsset("Source gold", lit, new Color(0.55f, 0.29f, 0.07f));
                gold.SetFloat("_Metallic", 0.65f); gold.SetFloat("_Smoothness", 0.5f);
                var source = new GameObject("Original source model - remains visible", typeof(MeshFilter), typeof(MeshRenderer));
                source.layer = Layer;
                source.transform.position = new Vector3(0, 0.35f, 0);
                source.transform.rotation = Quaternion.Euler(65, -18, 12);
                var sourceMesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/SurfaceDustGpu/Generated/SourceTorus.asset");
                if (!sourceMesh) throw new InvalidOperationException("Create the Surface Dust demo source mesh first.");
                source.GetComponent<MeshFilter>().sharedMesh = sourceMesh;
                source.GetComponent<MeshRenderer>().sharedMaterial = gold;

                var floorMaterial = MaterialAsset("Checker", lit, Color.white);
                floorMaterial.SetTexture("_BaseMap", CheckerTexture());
                floorMaterial.SetTextureScale("_BaseMap", new Vector2(10, 10));
                floorMaterial.SetFloat("_Smoothness", 0.12f);
                Primitive("Checker floor", PrimitiveType.Plane, new Vector3(0, -1.35f, 0), Vector3.one * 1.5f, floorMaterial);
                var dark = MaterialAsset("Backdrop", lit, new Color(0.06f, 0.09f, 0.13f));
                Primitive("Backdrop", PrimitiveType.Cube, new Vector3(0, 1.2f, 2.6f), new Vector3(12, 7, 0.1f), dark);
                var stripe = MaterialAsset("Backdrop stripes", lit, new Color(0.32f, 0.42f, 0.5f));
                for (int i = -5; i <= 5; i++)
                    Primitive("Refraction reference stripe", PrimitiveType.Cube, new Vector3(i * 0.55f, 1.2f, 2.5f), new Vector3(0.035f, 6, 0.03f), stripe);
                NewLight("Key", new Vector3(35, -35, 0), new Color(0.85f, 0.92f, 1), 1.5f);
                NewLight("Rim", new Vector3(30, 145, 0), new Color(1, 0.74f, 0.45f), 1);

                var go = new GameObject("GPU fluid particles", typeof(SurfaceDustEmitter));
                go.layer = Layer;
                var emitter = go.GetComponent<SurfaceDustEmitter>();
                emitter.source = source.GetComponent<MeshFilter>();
                emitter.simulation = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/SurfaceDustGpu/Shaders/SurfaceDust.compute");
                emitter.dustMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/SurfaceDustGpu/Generated/Dust.mat");
                emitter.drawBillboards = false;
                emitter.particleCount = 12288;
                emitter.particleSize = new Vector2(0.045f, 0.075f);
                emitter.lifetime = new Vector2(2.5f, 4.5f);
                emitter.outwardSpeed = 0.3f; emitter.surfaceOffset = 0.055f;
                emitter.turbulence = 0.22f; emitter.flowFrequency = 1.4f;
                emitter.wind = new Vector3(0.05f, 0.12f, 0);
                emitter.drag = 1.1f; emitter.maxTravelDistance = 4;

                var cameraGo = new GameObject("Screen Space Fluid Camera", typeof(Camera), typeof(ScreenSpaceFluidView));
                cameraGo.layer = Layer; cameraGo.tag = "MainCamera";
                var camera = cameraGo.GetComponent<Camera>();
                camera.transform.position = new Vector3(3.6f, 1.9f, -6.4f);
                camera.transform.LookAt(new Vector3(0, 0.45f, 0));
                camera.fieldOfView = 40; camera.nearClipPlane = 0.1f; camera.farClipPlane = 40;
                camera.cullingMask = 1 << Layer; camera.allowMSAA = false;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.025f, 0.035f, 0.055f);
                var cameraData = camera.GetUniversalAdditionalCameraData();
                cameraData.SetRenderer(rendererIndex); cameraData.requiresDepthTexture = true;
                var view = cameraGo.GetComponent<ScreenSpaceFluidView>();
                view.emitter = emitter; view.showControls = true;

                emitter.Initialize();
                view.enabled = false;
                // Warm the new renderer before measuring the background.
                UnityEngine.Object.DestroyImmediate(Capture(camera));
                Texture2D background = Capture(camera);
                view.enabled = true;
                Texture2D empty = Capture(camera);
                int emptyChanges = DifferentPixels(background, empty);
                UnityEngine.Object.DestroyImmediate(empty);
                Require(emptyChanges < 50, "Empty particle pool must preserve the scene background; changed: " + emptyChanges);
                report.AppendLine("PASS: empty fluid preserves opaque camera color.");
                for (int i = 0; i < 300; i++) emitter.StepSimulation(1f / 60f);
                Texture2D final = Capture(camera);
                int changed = DifferentPixels(background, final);
                Require(changed > 3000, "Expected visible fluid reconstruction, changed pixels: " + changed);
                Require(changed < background.width * background.height * 0.55f, "Fluid composite must preserve pixels outside its silhouette.");
                File.WriteAllBytes(Root + "/Preview/SourceOnly.png", background.EncodeToPNG());
                File.WriteAllBytes(Root + "/Preview/Liquid.png", final.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(background); UnityEngine.Object.DestroyImmediate(final);
                report.AppendLine($"PASS: screen-space liquid changes {changed} pixels; CPU billboard rendering disabled.");

                Texture2D raw = null, smooth = null;
                try
                {
                    foreach (ScreenSpaceFluidView.DisplayMode mode in Enum.GetValues(typeof(ScreenSpaceFluidView.DisplayMode)))
                    {
                        if (mode == ScreenSpaceFluidView.DisplayMode.Liquid) continue;
                        view.display = mode;
                        var image = Capture(camera);
                        File.WriteAllBytes(Root + "/Preview/" + mode + ".png", image.EncodeToPNG());
                        if (mode == ScreenSpaceFluidView.DisplayMode.ParticleDepth) raw = image;
                        else if (mode == ScreenSpaceFluidView.DisplayMode.SmoothDepth) smooth = image;
                        else UnityEngine.Object.DestroyImmediate(image);
                    }
                    int smoothed = DifferentPixels(raw, smooth, 0);
                    Require(smoothed > 500, "Depth smoothing must change raw sphere depths.");
                    report.AppendLine($"PASS: bilateral depth filtering changes {smoothed} pixels; raw depth, smooth depth, normal and thickness previews saved.");
                }
                finally { if (raw) UnityEngine.Object.DestroyImmediate(raw); if (smooth) UnityEngine.Object.DestroyImmediate(smooth); }
                view.display = ScreenSpaceFluidView.DisplayMode.Liquid;
                var resized = Capture(camera, 960, 540);
                File.WriteAllBytes(Root + "/Preview/Liquid-960x540.png", resized.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(resized);
                Require(source.GetComponent<Renderer>().enabled, "Source model must remain visible.");
                report.AppendLine("PASS: second resolution 960x540 rendered; original mesh remains enabled.");
                emitter.enabled = false; emitter.enabled = true;
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene, ScenePath);
                AssetDatabase.SaveAssets();
                report.AppendLine("Saved " + ScenePath);
                File.WriteAllText(Status, report.ToString());
                Debug.Log(report.ToString());
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
                foreach (var light in suspended) if (light) light.enabled = true;
            }
            AssetDatabase.Refresh();
        }

        static int InstallRenderer(Shader shader)
        {
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (!pipeline) throw new InvalidOperationException("This demo requires URP.");
            string path = Root + "/Generated/FluidRenderer.asset";
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (!data)
            {
                AssetDatabase.CopyAsset("Assets/Settings/PC_Renderer.asset", path);
                data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                data.rendererFeatures.Clear();
            }
            ScreenSpaceFluidFeature feature = null;
            foreach (var candidate in data.rendererFeatures) if (candidate is ScreenSpaceFluidFeature fluid) feature = fluid;
            if (!feature)
            {
                feature = ScriptableObject.CreateInstance<ScreenSpaceFluidFeature>();
                feature.name = "Screen Space Fluid";
                AssetDatabase.AddObjectToAsset(feature, data);
                data.rendererFeatures.Add(feature);
            }
            feature.fluidShader = shader; feature.Create(); feature.SetActive(true);
            var rendererSettings = new SerializedObject(data);
            rendererSettings.FindProperty("m_RenderingMode").intValue = 0; // Forward
            rendererSettings.FindProperty("m_IntermediateTextureMode").intValue = 1;
            rendererSettings.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(feature); EditorUtility.SetDirty(data); data.SetDirty();
            var settings = new SerializedObject(pipeline);
            var renderers = settings.FindProperty("m_RendererDataList");
            int index = -1;
            for (int i = 0; i < renderers.arraySize; i++)
                if (renderers.GetArrayElementAtIndex(i).objectReferenceValue == data) index = i;
            if (index < 0)
            {
                index = renderers.arraySize; renderers.arraySize++;
                renderers.GetArrayElementAtIndex(index).objectReferenceValue = data;
                settings.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(pipeline);
            }
            AssetDatabase.SaveAssets();
            return index;
        }

        static Material MaterialAsset(string name, Shader shader, Color color)
        {
            string path = Root + "/Generated/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            material.SetColor("_BaseColor", color); EditorUtility.SetDirty(material);
            return material;
        }
        static Texture2D CheckerTexture()
        {
            string path = Root + "/Generated/Checker.asset";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture) return texture;
            texture = new Texture2D(32, 32, TextureFormat.RGBA32, false) { name = "Checker", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
                texture.SetPixel(x, y, ((x / 16 + y / 16) & 1) == 0 ? new Color(0.12f, 0.16f, 0.2f) : new Color(0.45f, 0.5f, 0.55f));
            texture.Apply(); AssetDatabase.CreateAsset(texture, path); return texture;
        }
        static void Primitive(string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name; go.layer = Layer;
            go.transform.position = position; go.transform.localScale = scale; go.GetComponent<Renderer>().sharedMaterial = material;
        }
        static void NewLight(string name, Vector3 rotation, Color color, float intensity)
        {
            var go = new GameObject(name, typeof(Light)); go.layer = Layer;
            go.transform.rotation = Quaternion.Euler(rotation);
            var light = go.GetComponent<Light>(); light.type = LightType.Directional;
            light.color = color; light.intensity = intensity; light.cullingMask = 1 << Layer;
        }
        static Texture2D Capture(Camera camera, int width = 1280, int height = 800)
        {
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply(); return texture;
            }
            catch { UnityEngine.Object.DestroyImmediate(texture); throw; }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); }
        }
        static int DifferentPixels(Texture2D a, Texture2D b, int tolerance = 3)
        {
            var aa = a.GetPixels32(); var bb = b.GetPixels32(); int count = 0;
            for (int i = 0; i < aa.Length; i++)
                if (Mathf.Abs(aa[i].r - bb[i].r) > tolerance || Mathf.Abs(aa[i].g - bb[i].g) > tolerance || Mathf.Abs(aa[i].b - bb[i].b) > tolerance) count++;
            return count;
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
