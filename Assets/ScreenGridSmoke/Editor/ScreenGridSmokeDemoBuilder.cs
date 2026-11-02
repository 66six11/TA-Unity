using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace TA.ScreenGridSmoke.Editor
{
    [InitializeOnLoad]
    public static class ScreenGridSmokeDemoBuilder
    {
        const string Root = "Assets/ScreenGridSmoke";
        const string ScenePath = Root + "/ScreenGridSmokeDemo.unity";
        const string Request = "Library/ScreenGridSmoke.request";
        const string Status = "Library/ScreenGridSmoke-validation.txt";
        const int Layer = 27;
        static ScreenGridSmokeDemoBuilder() => EditorApplication.update += Poll;
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
                    Selection.activeObject = UnityEngine.Object.FindAnyObjectByType<ScreenGridSmokeView>();
                    File.WriteAllText("Library/ScreenGridSmoke-open.txt", "Opened " + scene.path);
                }
                else BuildAndVerify();
            }
            catch (Exception e) { File.WriteAllText(Status, e.ToString()); Debug.LogException(e); }
        }
        [MenuItem("Tools/TA/Screen Grid Smoke/Open Demo")]
        public static void OpenDemo()
        {
            if (!File.Exists(ScenePath)) BuildAndVerify();
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }
        [MenuItem("Tools/TA/Screen Grid Smoke/Build and Verify Demo")]
        public static void BuildAndVerify()
        {
            Directory.CreateDirectory(Root + "/Generated"); Directory.CreateDirectory(Root + "/Preview");
            AssetDatabase.Refresh();
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Shaders/ScreenGridSmoke.shader");
            var solver = AssetDatabase.LoadAssetAtPath<ComputeShader>(Root + "/Shaders/ScreenGridSmoke.compute");
            if (!shader || !solver) throw new InvalidOperationException("Smoke shader assets are not imported.");
            var compileMaterial = new Material(shader);
            try { for (int i = 0; i < compileMaterial.passCount; i++) ShaderUtil.CompilePass(compileMaterial, i, true); }
            finally { UnityEngine.Object.DestroyImmediate(compileMaterial); }
            if (ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Smoke shader compilation failed.");
            int rendererIndex = InstallRenderer(shader, solver);
            var report = new StringBuilder();
            report.AppendLine($"Unity {Application.unityVersion}; {SystemInfo.graphicsDeviceName}; {SystemInfo.graphicsDeviceType}");
            var suspended = new List<Light>();
            foreach (var light in UnityEngine.Object.FindObjectsByType<Light>()) if (light.enabled) { suspended.Add(light); light.enabled = false; }
            Scene original = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                RenderSettings.skybox = null; RenderSettings.fog = false;
                RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.16f, 0.2f, 0.25f);
                var lit = Shader.Find("Universal Render Pipeline/Lit");
                var source = new GameObject("Original model - screen-space smoke source", typeof(MeshFilter), typeof(MeshRenderer));
                source.layer = Layer;
                source.transform.position = new Vector3(0, -0.25f, 0);
                source.transform.rotation = Quaternion.Euler(75, 0, 8);
                source.GetComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/SurfaceDustGpu/Generated/SourceTorus.asset");
                var sourceMaterial = MaterialAsset("Source", lit, new Color(0.58f, 0.36f, 0.14f));
                sourceMaterial.SetFloat("_Metallic", 0.7f); sourceMaterial.SetFloat("_Smoothness", 0.48f);
                source.GetComponent<Renderer>().sharedMaterial = sourceMaterial;
                var pedestal = GameObject.CreatePrimitive(PrimitiveType.Cylinder); pedestal.name = "Pedestal"; pedestal.layer = Layer;
                pedestal.transform.position = new Vector3(0, -1.9f, 0); pedestal.transform.localScale = new Vector3(2, 0.15f, 2);
                pedestal.GetComponent<Renderer>().sharedMaterial = MaterialAsset("Pedestal", lit, new Color(0.08f, 0.1f, 0.14f));
                NewLight("Cool key", new Vector3(30, -35, 0), new Color(0.76f, 0.88f, 1), 1.7f);
                NewLight("Warm rim", new Vector3(30, 150, 0), new Color(1, 0.64f, 0.28f), 1.4f);
                var cameraGo = new GameObject("Screen Grid Smoke Camera", typeof(Camera), typeof(ScreenGridSmokeView));
                cameraGo.layer = Layer; cameraGo.tag = "MainCamera";
                Camera camera = cameraGo.GetComponent<Camera>();
                camera.transform.position = new Vector3(0.15f, 1.3f, -8);
                camera.transform.LookAt(new Vector3(0, 0.65f, 0));
                camera.fieldOfView = 43; camera.nearClipPlane = 0.1f; camera.farClipPlane = 40;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.017f, 0.024f, 0.037f);
                camera.cullingMask = 1 << Layer; camera.allowMSAA = false;
                var data = camera.GetUniversalAdditionalCameraData(); data.SetRenderer(rendererIndex); data.requiresDepthTexture = true;
                var view = cameraGo.GetComponent<ScreenGridSmokeView>();
                view.source = source.GetComponent<MeshFilter>(); view.validationDeltaTime = 1f / 30f;
                view.paused = true;
                view.enabled = false;
                UnityEngine.Object.DestroyImmediate(Capture(camera));
                var background = Capture(camera);
                view.enabled = true;
                var empty = Capture(camera);
                Require(DifferentPixels(background, empty) < 50, "Empty smoke must preserve scene color.");
                UnityEngine.Object.DestroyImmediate(empty);
                report.AppendLine("PASS: empty grid preserves source model and scene color.");
                view.paused = false;
                for (int i = 0; i < 180; i++)
                {
                    var image = Capture(camera);
                    if (i == 29 || i == 89 || i == 179) File.WriteAllBytes(Root + "/Preview/Smoke-" + (i + 1) + ".png", image.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(image);
                    if (i % 30 == 29) File.WriteAllText("Library/ScreenGridSmoke-progress.txt", $"Simulated {i+1}/180 frames");
                }
                view.paused = true;
                var final = Capture(camera);
                int changed = DifferentPixels(background, final);
                Require(changed > 3000, "Expected visible advected smoke. Changed pixels: " + changed);
                File.WriteAllBytes(Root + "/Preview/Smoke.png", final.EncodeToPNG());
                File.WriteAllBytes(Root + "/Preview/SourceOnly.png", background.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(final); UnityEngine.Object.DestroyImmediate(background);
                float mass = DyeMass(view);
                Require(mass > 100, "Dye injection and advection must produce non-zero density.");
                double before = Rms(view.Divergence.rt), after = Rms(view.ProjectedDivergence.rt);
                Require(after < before * 0.5, $"Pressure projection must substantially reduce divergence: {before} -> {after}");
                report.AppendLine($"PASS: grid {view.Width}x{view.Height}, dye mass {mass:F1}, smoke changes {changed} pixels.");
                report.AppendLine($"PASS: divergence RMS {before:F6} -> {after:F6} after {view.pressureIterations} Jacobi iterations.");
                var pausedImage = Capture(camera); UnityEngine.Object.DestroyImmediate(pausedImage);
                Require(Mathf.Abs(DyeMass(view) - mass) < 0.01f, "Pause must preserve dye history.");
                foreach (ScreenGridSmokeView.DisplayMode mode in Enum.GetValues(typeof(ScreenGridSmokeView.DisplayMode)))
                {
                    if (mode == ScreenGridSmokeView.DisplayMode.Smoke) continue;
                    view.display = mode;
                    var image = Capture(camera); File.WriteAllBytes(Root + "/Preview/" + mode + ".png", image.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(image);
                }
                view.display = ScreenGridSmokeView.DisplayMode.Smoke;
                Vector3 cameraPosition = camera.transform.position; Quaternion cameraRotation = camera.transform.rotation;
                int resetCount = view.ResetCount;
                camera.transform.RotateAround(source.transform.position, Vector3.up, 15);
                var rotated = Capture(camera); UnityEngine.Object.DestroyImmediate(rotated);
                Require(view.ResetCount > resetCount && DyeMass(view) < 0.001f, "Camera rotation must reset screen-space history.");
                report.AppendLine("PASS: pause preserves dye; rotating camera resets persistent velocity/pressure/dye histories.");
                resetCount = view.ResetCount;
                var resized = Capture(camera, 960, 540); UnityEngine.Object.DestroyImmediate(resized);
                Require(view.ResetCount > resetCount && DyeMass(view) < 0.001f, "Aspect ratio changes must reallocate and clear history.");
                camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                Require(source.GetComponent<Renderer>().enabled, "The original model must remain enabled.");
                view.enabled = false;
                Require(view.Velocity == null && view.Dye == null, "Disable must release persistent textures.");
                view.enabled = true; view.paused = false; view.validationDeltaTime = 0;
                report.AppendLine("PASS: resize resets history; disable releases persistent render textures; original model remains enabled.");
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene, ScenePath); AssetDatabase.SaveAssets();
                report.AppendLine("Saved " + ScenePath);
                File.WriteAllText(Status, report.ToString()); Debug.Log(report.ToString());
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
                foreach (var light in suspended) if (light) light.enabled = true;
            }
            AssetDatabase.Refresh();
        }

        static int InstallRenderer(Shader shader, ComputeShader solver)
        {
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (!pipeline) throw new InvalidOperationException("URP is required.");
            string path = Root + "/Generated/SmokeRenderer.asset";
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (!data)
            {
                AssetDatabase.CopyAsset("Assets/Settings/PC_Renderer.asset", path);
                data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path); data.rendererFeatures.Clear();
            }
            ScreenGridSmokeFeature feature = null;
            foreach (var item in data.rendererFeatures) if (item is ScreenGridSmokeFeature smoke) feature = smoke;
            if (!feature)
            {
                feature = ScriptableObject.CreateInstance<ScreenGridSmokeFeature>(); feature.name = "Screen Grid Smoke";
                AssetDatabase.AddObjectToAsset(feature, data); data.rendererFeatures.Add(feature);
            }
            feature.solver = solver; feature.smokeShader = shader; feature.Create(); feature.SetActive(true);
            var rendererSettings = new SerializedObject(data);
            rendererSettings.FindProperty("m_RenderingMode").intValue = 0;
            rendererSettings.FindProperty("m_IntermediateTextureMode").intValue = 1;
            rendererSettings.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(feature); EditorUtility.SetDirty(data); data.SetDirty();
            var pipelineSettings = new SerializedObject(pipeline);
            var list = pipelineSettings.FindProperty("m_RendererDataList");
            int index = -1;
            for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).objectReferenceValue == data) index = i;
            if (index < 0)
            {
                index = list.arraySize; list.arraySize++; list.GetArrayElementAtIndex(index).objectReferenceValue = data;
                pipelineSettings.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(pipeline);
            }
            AssetDatabase.SaveAssets(); return index;
        }
        static Material MaterialAsset(string name, Shader shader, Color color)
        {
            string path = Root + "/Generated/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            material.SetColor("_BaseColor", color); EditorUtility.SetDirty(material); return material;
        }
        static void NewLight(string name, Vector3 rotation, Color color, float intensity)
        {
            var go = new GameObject(name, typeof(Light)); go.layer = Layer; go.transform.rotation = Quaternion.Euler(rotation);
            var light = go.GetComponent<Light>(); light.type = LightType.Directional; light.color = color; light.intensity = intensity; light.cullingMask = 1 << Layer;
        }
        static float DyeMass(ScreenGridSmokeView view)
        {
            var request = AsyncGPUReadback.Request(view.Dye[view.CurrentDye].rt, 0, TextureFormat.RGBAFloat); request.WaitForCompletion();
            if (request.hasError) throw new InvalidOperationException("Dye diagnostic readback failed.");
            var values = request.GetData<float>(); double mass = 0;
            for (int i = 3; i < values.Length; i += 4)
            {
                Require(float.IsFinite(values[i]) && values[i] >= 0, "Density must be finite and non-negative."); mass += values[i];
            }
            return (float)mass;
        }
        static double Rms(RenderTexture texture)
        {
            var request = AsyncGPUReadback.Request(texture, 0, TextureFormat.RFloat); request.WaitForCompletion();
            if (request.hasError) throw new InvalidOperationException("Divergence diagnostic readback failed.");
            var values = request.GetData<float>(); double sum = 0;
            foreach (float value in values) { Require(float.IsFinite(value), "Divergence must be finite."); sum += value * value; }
            return Math.Sqrt(sum / values.Length);
        }
        static Texture2D Capture(Camera camera, int width = 1280, int height = 800)
        {
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active; var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply(); return image;
            }
            catch { UnityEngine.Object.DestroyImmediate(image); throw; }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); }
        }
        static int DifferentPixels(Texture2D a, Texture2D b)
        {
            var aa = a.GetPixels32(); var bb = b.GetPixels32(); int count = 0;
            for (int i = 0; i < aa.Length; i++)
                if (Mathf.Abs(aa[i].r - bb[i].r) > 3 || Mathf.Abs(aa[i].g - bb[i].g) > 3 || Mathf.Abs(aa[i].b - bb[i].b) > 3) count++;
            return count;
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
