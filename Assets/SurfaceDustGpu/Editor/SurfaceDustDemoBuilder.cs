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

namespace TA.SurfaceDustGpu.Editor
{
    [InitializeOnLoad]
    public static class SurfaceDustDemoBuilder
    {
        const string Root = "Assets/SurfaceDustGpu";
        const string ScenePath = Root + "/SurfaceDustDemo.unity";
        const string Request = "Library/SurfaceDustGpu.request";
        const string Status = "Library/SurfaceDustGpu-validation.txt";
        const int Layer = 29;

        static SurfaceDustDemoBuilder() => EditorApplication.update += Poll;

        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Request)) return;
            string command;
            try { command = File.ReadAllText(Request).Trim(); File.Delete(Request); }
            catch (IOException) { return; }
            try
            {
                if (command == "open")
                {
                    // Preserve unsaved work when opening from an external request.
                    bool dirty = false;
                    for (int i = 0; i < SceneManager.sceneCount; i++)
                        dirty |= SceneManager.GetSceneAt(i).isDirty;
                    Scene scene = SceneManager.GetSceneByPath(ScenePath);
                    if (!scene.IsValid() || !scene.isLoaded)
                        scene = EditorSceneManager.OpenScene(ScenePath, dirty ? OpenSceneMode.Additive : OpenSceneMode.Single);
                    SceneManager.SetActiveScene(scene);
                    Selection.activeObject = UnityEngine.Object.FindAnyObjectByType<SurfaceDustEmitter>();
                    File.WriteAllText("Library/SurfaceDustGpu-open.txt", "Opened " + scene.path + (dirty ? " (additive; unsaved work preserved)" : ""));
                }
                else BuildAndVerify();
            }
            catch (Exception e) { File.WriteAllText(Status, e.ToString()); Debug.LogException(e); }
        }

        [MenuItem("Tools/TA/Surface Dust GPU/Open Demo")]
        public static void OpenDemo()
        {
            if (!File.Exists(ScenePath)) BuildAndVerify();
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Tools/TA/Surface Dust GPU/Build and Verify Demo")]
        public static void BuildAndVerify()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Exit Play mode before rebuilding.");
            Directory.CreateDirectory(Root + "/Generated");
            Directory.CreateDirectory(Root + "/Preview");
            AssetDatabase.Refresh();
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Shaders/SurfaceDust.shader");
            var compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(Root + "/Shaders/SurfaceDust.compute");
            if (!shader || !compute) throw new InvalidOperationException("Shader assets have not imported.");
            var material = MaterialAsset("Dust", shader, Color.white);
            ShaderUtil.CompilePass(material, 0, true);
            if (ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Dust shader compilation failed.");
            var report = new StringBuilder();
            report.AppendLine($"Unity {Application.unityVersion}; GPU: {SystemInfo.graphicsDeviceName}; API: {SystemInfo.graphicsDeviceType}");

            Scene original = SceneManager.GetActiveScene();
            var suspendedLights = new List<Light>();
            foreach (var existing in UnityEngine.Object.FindObjectsByType<Light>())
                if (existing.enabled) { suspendedLights.Add(existing); existing.enabled = false; }
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                RenderSettings.skybox = null;
                RenderSettings.fog = false;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.16f, 0.21f, 0.28f);

                var mesh = BuildTorus();
                var sourceObject = new GameObject("Source - original mesh remains visible", typeof(MeshFilter), typeof(MeshRenderer));
                sourceObject.layer = Layer;
                sourceObject.transform.position = new Vector3(0, 0.3f, 0);
                sourceObject.transform.rotation = Quaternion.Euler(65, -18, 12);
                var filter = sourceObject.GetComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                var litShader = Shader.Find("Universal Render Pipeline/Lit");
                var sourceMaterial = MaterialAsset("Source", litShader, new Color(0.11f, 0.35f, 0.38f));
                sourceMaterial.SetFloat("_Metallic", 0.65f);
                sourceMaterial.SetFloat("_Smoothness", 0.48f);
                sourceObject.GetComponent<MeshRenderer>().sharedMaterial = sourceMaterial;

                var pedestal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pedestal.name = "Display pedestal";
                pedestal.layer = Layer;
                pedestal.transform.position = new Vector3(0, -1.35f, 0);
                pedestal.transform.localScale = new Vector3(2, 0.12f, 2);
                pedestal.GetComponent<Renderer>().sharedMaterial = MaterialAsset("Pedestal", litShader, new Color(0.055f, 0.075f, 0.105f));
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = "Ground";
                ground.layer = Layer;
                ground.transform.position = new Vector3(0, -1.49f, 0);
                ground.GetComponent<Renderer>().sharedMaterial = MaterialAsset("Ground", litShader, new Color(0.035f, 0.045f, 0.065f));
                Light sun = NewLight("Cool key", new Vector3(35, -35, 0), new Color(0.72f, 0.87f, 1), 2);
                sun.shadows = LightShadows.Soft;
                RenderSettings.sun = sun;
                NewLight("Warm rim", new Vector3(30, 150, 0), new Color(1, 0.65f, 0.28f), 1.2f);

                var cameraObject = new GameObject("Demo Camera", typeof(Camera));
                cameraObject.layer = Layer;
                cameraObject.tag = "MainCamera";
                Camera camera = cameraObject.GetComponent<Camera>();
                camera.transform.position = new Vector3(4.3f, 2.35f, -7.2f);
                camera.transform.LookAt(new Vector3(0.1f, 0.7f, 0));
                camera.fieldOfView = 45;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 60;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.016f, 0.023f, 0.039f);
                camera.cullingMask = 1 << Layer;
                camera.allowHDR = true;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;

                var emitterObject = new GameObject("GPU surface dust", typeof(SurfaceDustEmitter));
                emitterObject.layer = Layer;
                var emitter = emitterObject.GetComponent<SurfaceDustEmitter>();
                emitter.source = filter;
                emitter.simulation = compute;
                emitter.dustMaterial = material;
                emitter.particleCount = 32768;
                cameraObject.AddComponent<SurfaceDustDemoControls>().emitter = emitter;

                VerifyAreaSampling(emitter, report);
                emitter.source = filter;
                emitter.particleCount = 32768;
                emitter.Initialize();
                var initial = emitter.ReadbackForValidation();
                float maxSurfaceError = 0;
                foreach (var p in initial)
                {
                    Vector3 local = filter.transform.InverseTransformPoint(p.position);
                    float radial = new Vector2(local.x, local.z).magnitude;
                    float tube = new Vector2(radial - 1.05f, local.y).magnitude;
                    maxSurfaceError = Mathf.Max(maxSurfaceError, Mathf.Abs(tube - (0.34f + emitter.surfaceOffset)));
                }
                Require(maxSurfaceError < 0.012f, "Initial particles must lie on the offset model surface.");
                for (int i = 0; i < 420; i++) emitter.StepSimulation(1f / 60f);
                var first = emitter.ReadbackForValidation();
                emitter.StepSimulation(0.1f);
                var second = emitter.ReadbackForValidation();
                int active = 0, moving = 0, reborn = 0;
                for (int i = 0; i < second.Length; i++)
                {
                    var p = second[i];
                    Require(float.IsFinite(p.position.x) && float.IsFinite(p.position.y) && float.IsFinite(p.position.z), "GPU positions must be finite.");
                    Require(p.age >= 0 && p.age <= p.lifetime + 0.001f, "Particle lifetime invariant.");
                    Require(p.travel <= emitter.maxTravelDistance + 0.001f, "Travel bound invariant.");
                    if (p.age >= 0) active++;
                    if ((p.position - first[i].position).sqrMagnitude > 1e-8f) moving++;
                    if (p.seed != first[i].seed) reborn++;
                }
                Require(active == emitter.AllocatedCount && moving > active * 0.95f && reborn > 0, "GPU motion and recycling must run.");
                report.AppendLine($"PASS: surface sampling error <= {maxSurfaceError:F5}; {active} live particles, {moving} moved, {reborn} recycled over 0.1s.");

                Texture2D before = Capture(camera);
                emitter.SubmitDraw(camera);
                Texture2D after = Capture(camera);
                try
                {
                    int changed = DifferentPixels(before, after);
                    Require(changed > 1000, $"Expected visible GPU dust; changed pixels={changed}.");
                    File.WriteAllBytes(Root + "/Preview/SurfaceDust.png", after.EncodeToPNG());
                    File.WriteAllBytes(Root + "/Preview/SourceOnly.png", before.EncodeToPNG());
                    report.AppendLine($"PASS: custom GPU rendering changes {changed} pixels; original model renderer remains enabled.");
                }
                finally { UnityEngine.Object.DestroyImmediate(before); UnityEngine.Object.DestroyImmediate(after); }
                Require(sourceObject.GetComponent<Renderer>().enabled, "The original mesh must remain enabled.");
                emitter.enabled = false;
                Require(!emitter.IsInitialized, "Disable must release GPU buffers.");
                emitter.enabled = true;
                emitter.Initialize();
                report.AppendLine("PASS: disable releases buffers; reinitialize succeeds. No per-frame CPU particle readback in runtime Update.");
                emitter.enabled = false;
                emitter.enabled = true;
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
                foreach (var light in suspendedLights) if (light) light.enabled = true;
            }
            AssetDatabase.Refresh();
        }

        static void VerifyAreaSampling(SurfaceDustEmitter emitter, StringBuilder report)
        {
            var testObject = new GameObject("Area sampling validation", typeof(MeshFilter));
            var mesh = new Mesh { name = "Area ratio 1 to 4" };
            // Triangle areas 0.5 and 2; disjoint positions identify the selected triangle.
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up,
                new Vector3(10, 0, 0), new Vector3(12, 0, 0), new Vector3(10, 2, 0) };
            mesh.triangles = new[] { 0, 1, 2, 3, 4, 5 };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            try
            {
                testObject.GetComponent<MeshFilter>().sharedMesh = mesh;
                emitter.source = testObject.GetComponent<MeshFilter>();
                emitter.particleCount = 8193; // Also validates final partial compute thread group.
                emitter.Initialize();
                var particles = emitter.ReadbackForValidation();
                int larger = 0;
                foreach (var p in particles) if (p.position.x > 5) larger++;
                float ratio = larger / (float)particles.Length;
                Require(Mathf.Abs(ratio - 0.8f) < 0.025f, "Area-weighted sampling must match triangle areas.");
                Require(particles[particles.Length - 1].lifetime > 0, "Last compute thread must initialize.");
                report.AppendLine($"PASS: 1:4 triangle areas -> larger triangle fraction {ratio:F4}; non-multiple-of-128 count 8193 initialized.");
            }
            finally { UnityEngine.Object.DestroyImmediate(testObject); UnityEngine.Object.DestroyImmediate(mesh); }
        }

        static Light NewLight(string name, Vector3 rotation, Color color, float intensity)
        {
            var go = new GameObject(name, typeof(Light));
            go.layer = Layer;
            go.transform.rotation = Quaternion.Euler(rotation);
            Light light = go.GetComponent<Light>();
            light.type = LightType.Directional; light.color = color; light.intensity = intensity;
            light.cullingMask = 1 << Layer;
            return light;
        }

        static Mesh BuildTorus()
        {
            const int rings = 96, sides = 32;
            var vertices = new Vector3[(rings + 1) * (sides + 1)];
            var normals = new Vector3[vertices.Length];
            var indices = new int[rings * sides * 6];
            int cursor = 0;
            for (int r = 0; r <= rings; r++)
                for (int s = 0; s <= sides; s++)
                {
                    float a = r * Mathf.PI * 2 / rings, b = s * Mathf.PI * 2 / sides;
                    var radial = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    Vector3 normal = radial * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                    int v = r * (sides + 1) + s;
                    vertices[v] = radial * 1.05f + normal * 0.34f;
                    normals[v] = normal;
                    if (r == rings || s == sides) continue;
                    int next = v + sides + 1;
                    indices[cursor++] = v; indices[cursor++] = v + 1; indices[cursor++] = next;
                    indices[cursor++] = v + 1; indices[cursor++] = next + 1; indices[cursor++] = next;
                }
            var mesh = new Mesh { name = "Dust source torus" };
            mesh.vertices = vertices; mesh.normals = normals; mesh.triangles = indices; mesh.RecalculateBounds();
            string path = Root + "/Generated/SourceTorus.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing) { EditorUtility.CopySerialized(mesh, existing); UnityEngine.Object.DestroyImmediate(mesh); mesh = existing; }
            else AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static Material MaterialAsset(string name, Shader shader, Color color)
        {
            if (!shader) throw new InvalidOperationException("Required shader missing.");
            string path = Root + "/Generated/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            material.shader = shader;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Texture2D Capture(Camera camera)
        {
            var target = RenderTexture.GetTemporary(1280, 800, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            var texture = new Texture2D(1280, 800, TextureFormat.RGB24, false);
            try
            {
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0); texture.Apply();
                return texture;
            }
            catch { UnityEngine.Object.DestroyImmediate(texture); throw; }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); }
        }

        static int DifferentPixels(Texture2D a, Texture2D b)
        {
            var aa = a.GetPixels32(); var bb = b.GetPixels32(); int count = 0;
            for (int i = 0; i < aa.Length; i++)
                if (Mathf.Abs(aa[i].r - bb[i].r) > 3 || Mathf.Abs(aa[i].g - bb[i].g) > 3 || Mathf.Abs(aa[i].b - bb[i].b) > 3) count++;
            return count;
        }
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
