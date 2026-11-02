using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace TA.ShadowChromaticDemo.Editor
{
    [InitializeOnLoad]
    public static class ChromaticShadowDemoBuilder
    {
        const string Root = "Assets/ShadowChromaticDemo";
        const string ScenePath = Root + "/ChromaticShadowDemo.unity";
        const string RequestPath = "Library/ChromaticShadowDemo.build-request";
        const int DemoLayer = 30;

        static ChromaticShadowDemoBuilder() => EditorApplication.update += CheckRequest;

        static void CheckRequest()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(RequestPath)) return;
            try { File.Delete(RequestPath); }
            catch (IOException) { return; } // The request writer may still hold the file.
            try { BuildAndVerify(); }
            catch (Exception exception)
            {
                File.WriteAllText("Library/ChromaticShadowDemo-validation.txt", exception.ToString());
                Debug.LogException(exception);
            }
        }

        [MenuItem("Tools/TA/Chromatic Shadows/Open Demo")]
        public static void OpenDemo()
        {
            if (!File.Exists(ScenePath)) BuildAndVerify();
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Tools/TA/Chromatic Shadows/Build and Verify Demo")]
        public static void BuildAndVerify()
        {
            Directory.CreateDirectory(Root + "/Materials");
            Directory.CreateDirectory(Root + "/Preview");
            AssetDatabase.Refresh();
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Shaders/ChromaticShadow.shader");
            if (!shader) throw new InvalidOperationException("Chromatic shadow shader was not imported.");
            var compileMaterial = new Material(shader);
            try { ShaderUtil.CompilePass(compileMaterial, 0, true); }
            finally { UnityEngine.Object.DestroyImmediate(compileMaterial); }
            if (ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("Shader has compilation errors; inspect Unity Console.");

            Scene original = SceneManager.GetActiveScene();
            // Keep lights from other loaded scenes out of off-screen validation,
            // especially when the demo sun is turned off for decomposition checks.
            var suspendedLights = new List<Light>();
            foreach (var existingLight in UnityEngine.Object.FindObjectsByType<Light>())
                if (existingLight.enabled)
                {
                    suspendedLights.Add(existingLight);
                    existingLight.enabled = false;
                }
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                RenderSettings.fog = false;
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(0.48f, 0.65f, 1) * 0.4f;
                RenderSettings.ambientEquatorColor = new Color(0.24f, 0.32f, 0.5f) * 0.4f;
                RenderSettings.ambientGroundColor = new Color(0.2f, 0.17f, 0.14f) * 0.4f;
                RenderSettings.skybox = null;

                Material ground = MaterialAsset(shader, "Ground", new Color(0.72f, 0.72f, 0.72f));
                Material objects = MaterialAsset(Shader.Find("Universal Render Pipeline/Lit"), "Casters", new Color(0.86f, 0.83f, 0.77f));
                var receivers = new List<Renderer>();
                receivers.Add(Shape("Ground - warm sun and cool sky receiver", PrimitiveType.Cube,
                    new Vector3(0, -0.16f, 0), new Vector3(15, 0.3f, 13), ground));
                receivers[0].shadowCastingMode = ShadowCastingMode.Off;
                receivers.Add(Shape("Cube", PrimitiveType.Cube,
                    new Vector3(-2.1f, 1.1f, 0), new Vector3(1.5f, 2.2f, 1.5f), objects));
                receivers.Add(Shape("Sphere", PrimitiveType.Sphere,
                    new Vector3(0.3f, 1.05f, 0.2f), Vector3.one * 2.1f, objects));
                receivers.Add(Shape("Pillar", PrimitiveType.Cylinder,
                    new Vector3(2.8f, 1.65f, 0), new Vector3(0.75f, 1.65f, 0.75f), objects));
                for (int i = 0; i < 4; i++)
                    receivers.Add(Shape("Thin post " + i, PrimitiveType.Cube,
                        new Vector3(-2.7f + i * 0.65f, 0.8f, 3.0f), new Vector3(0.16f, 1.6f, 0.16f), objects));

                var lightObject = new GameObject("Main directional light");
                lightObject.layer = DemoLayer;
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.15f;
                light.color = Color.white;
                light.shadows = LightShadows.Soft;
                light.shadowStrength = 1;
                light.shadowBias = 0.03f;
                light.shadowNormalBias = 0.15f;
                light.cullingMask = 1 << DemoLayer;
                light.transform.rotation = Quaternion.Euler(48, -32, 0);
                RenderSettings.sun = light;

                var cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                var camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.045f, 0.055f, 0.075f);
                camera.cullingMask = 1 << DemoLayer;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 50;
                camera.fieldOfView = 43;
                camera.transform.position = new Vector3(10, 11, 13);
                camera.transform.LookAt(new Vector3(0, 0.4f, 1.1f));
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
                var controls = cameraObject.AddComponent<ChromaticShadowControls>();
                controls.receivers = new[] { receivers[0] };
                controls.sun = light;
                controls.Apply();

                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                    throw new IOException("Could not save demo scene.");
                var report = new List<string> { "Unity " + Application.unityVersion, "Shader: " + shader.name };
                Texture2D rgb = null, neutral = null, zero = null;
                try
                {
                    rgb = Capture(camera);
                    WritePreview("WarmSunCoolSky.png", rgb);
                    controls.neutralLighting = true;
                    controls.Apply();
                    neutral = Capture(camera);
                    WritePreview("NeutralLights.png", neutral);
                    controls.neutralLighting = false;
                    controls.sunIntensity = 0;
                    controls.Apply();
                    zero = Capture(camera);
                    int changed = DifferentPixels(rgb, neutral, 3);
                    controls.viewMode = 2;
                    controls.Apply();
                    var skyOnly = Capture(camera);
                    int zeroDifference;
                    try
                    {
                        WritePreview("ZeroSun.png", zero);
                        WritePreview("SkyOnly.png", skyOnly);
                        zeroDifference = DifferentPixels(zero, skyOnly, 1);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(skyOnly); }
                    report.Add("Warm sun / cool sky versus neutral lights changed pixels: " + changed);
                    report.Add("Zero sun versus sky-only changed pixels: " + zeroDifference);
                    if (ShaderUtil.ShaderHasError(shader)) throw new Exception("Shader failed after rendering.");
                    if (changed < 100) throw new Exception("Colored illumination did not affect enough pixels.");
                    if (zeroDifference > 0) throw new Exception("Zero sun should match sky-only lighting.");
                    UnityEngine.Object.DestroyImmediate(zero);
                    zero = null;
                    controls.sunIntensity = 4;
                    controls.skyIntensity = 0;
                    controls.viewMode = 0;
                    controls.Apply();
                    zero = Capture(camera);
                    controls.viewMode = 1;
                    controls.Apply();
                    var sunOnly = Capture(camera);
                    try { zeroDifference = DifferentPixels(zero, sunOnly, 1); }
                    finally { UnityEngine.Object.DestroyImmediate(sunOnly); }
                    report.Add("Zero sky versus sun-only changed pixels: " + zeroDifference);
                    if (zeroDifference > 0) throw new Exception("Zero sky should match sun-only lighting.");
                    controls.skyIntensity = 0.4f;
                    controls.viewMode = 3;
                    controls.Apply();
                    var mask = Capture(camera);
                    try { WritePreview("ScalarPCF.png", mask); }
                    finally { UnityEngine.Object.DestroyImmediate(mask); }
                    report.Add("PASS: scalar PCF, colored illumination, zero-sun and zero-sky decomposition.");
                }
                finally
                {
                    controls.sunIntensity = 4;
                    controls.skyIntensity = 0.4f;
                    controls.neutralLighting = false;
                    controls.viewMode = 0;
                    controls.Apply();
                    if (rgb) UnityEngine.Object.DestroyImmediate(rgb);
                    if (neutral) UnityEngine.Object.DestroyImmediate(neutral);
                    if (zero) UnityEngine.Object.DestroyImmediate(zero);
                }
                File.WriteAllLines("Library/ChromaticShadowDemo-validation.txt", report);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("Chromatic Shadow Demo: " + string.Join("\n", report));
            }
            finally
            {
                foreach (var suspendedLight in suspendedLights)
                    if (suspendedLight) suspendedLight.enabled = true;
                if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        static Material MaterialAsset(Shader shader, string name, Color color)
        {
            string path = Root + "/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(material);
            return material;
        }

        static void WritePreview(string filename, Texture2D texture)
        {
            string path = Root + "/Preview/" + filename;
            string temporary = path + ".tmp";
            File.WriteAllBytes(temporary, texture.EncodeToPNG());
            // Replace rather than truncate a PNG that the editor may have memory-mapped.
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        static Renderer Shape(string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
        {
            var obj = GameObject.CreatePrimitive(type);
            obj.name = name;
            obj.layer = DemoLayer;
            obj.transform.position = position;
            obj.transform.localScale = scale;
            var renderer = obj.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }

        static Texture2D Capture(Camera camera)
        {
            var target = RenderTexture.GetTemporary(1280, 800, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            var texture = new Texture2D(1280, 800, TextureFormat.RGB24, false);
            try
            {
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0);
                texture.Apply();
                return texture;
            }
            catch { UnityEngine.Object.DestroyImmediate(texture); throw; }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); }
        }

        static int DifferentPixels(Texture2D a, Texture2D b, int tolerance)
        {
            var aa = a.GetPixels32();
            var bb = b.GetPixels32();
            int count = 0;
            for (int i = 0; i < aa.Length; i++)
                if (Mathf.Abs(aa[i].r - bb[i].r) > tolerance || Mathf.Abs(aa[i].g - bb[i].g) > tolerance ||
                    Mathf.Abs(aa[i].b - bb[i].b) > tolerance) count++;
            return count;
        }
    }
}


