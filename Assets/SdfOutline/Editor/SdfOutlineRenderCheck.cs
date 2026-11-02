using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace TA.SdfOutline.Editor
{
    [InitializeOnLoad]
    public static class SdfOutlineRenderCheck
    {
        static SdfOutlineRenderCheck() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists("Library/SdfOutline-render.request")) return;
            File.Delete("Library/SdfOutline-render.request");
            try { Run(); } catch (Exception e) { File.WriteAllText("Library/SdfOutline-render.txt", e.ToString()); Debug.LogException(e); }
        }
        [MenuItem("Tools/TA/SDF Outline/Validate Full Render")]
        public static void Run()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            var settings = new SerializedObject(pipeline);
            var list = settings.FindProperty("m_RendererDataList"); int count = list.arraySize;
            var original = list.GetArrayElementAtIndex(settings.FindProperty("m_DefaultRendererIndex").intValue).objectReferenceValue as UniversalRendererData;
            var renderer = UnityEngine.Object.Instantiate(original); renderer.rendererFeatures.Clear();
            var feature = ScriptableObject.CreateInstance<SdfOutlineFeature>();
            feature.distanceCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/SdfOutline/Shaders/SdfOutline.compute");
            feature.maskShader = Shader.Find("TA/SDF Outline Mask"); feature.compositeShader = Shader.Find("TA/SDF Outline Composite");
            feature.targetLayers = 1 << 31; feature.outlineColor = Color.red; feature.width = 5;
            renderer.rendererFeatures.Add(feature); feature.Create(); renderer.SetDirty();
            const string previewPath = "Assets/SdfOutline/ValidationRenderer.asset";
            if (AssetDatabase.LoadAssetAtPath<UniversalRendererData>(previewPath)) AssetDatabase.DeleteAsset(previewPath);
            AssetDatabase.CreateAsset(renderer, previewPath); AssetDatabase.AddObjectToAsset(feature, renderer); AssetDatabase.SaveAssets();
            var scene = EditorSceneManager.NewPreviewScene();
            var target = new RenderTexture(128, 128, 24, RenderTextureFormat.ARGB32);
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit")); material.SetColor("_BaseColor", Color.white);
            Texture2D image = null; var previous = RenderTexture.active;
            try
            {
                list.arraySize = count + 1; list.GetArrayElementAtIndex(count).objectReferenceValue = renderer; settings.ApplyModifiedPropertiesWithoutUndo();
                var go = new GameObject("SDF verification camera"); SceneManager.MoveGameObjectToScene(go, scene);
                var camera = go.AddComponent<Camera>(); camera.scene = scene; camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 2;
                camera.transform.position = new Vector3(0, 0, -5); camera.cullingMask = 1 << 31;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.allowMSAA = false;
                camera.GetUniversalAdditionalCameraData().SetRenderer(count);
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(cube, scene); cube.layer = 31;
                cube.GetComponent<Renderer>().sharedMaterial = material;
                int red = 0, white = 0;
                foreach (var mode in new[] { SdfOutlineFeature.Display.Composite, SdfOutlineFeature.Display.Mask, SdfOutlineFeature.Display.SignedDistance })
                {
                    feature.display = mode;
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                    RenderTexture.active = target;
                    image = new Texture2D(128, 128, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); image.Apply();
                    File.WriteAllBytes("Library/SdfOutline-" + mode + ".png", image.EncodeToPNG());
                    if (mode == SdfOutlineFeature.Display.Composite)
                        foreach (var c in image.GetPixels()) { if (c.r > 0.5f && c.g < 0.2f) red++; if (c.r > 0.8f && c.g > 0.8f) white++; }
                    UnityEngine.Object.DestroyImmediate(image); image = null;
                }
                if (red < 50 || white < 100) throw new InvalidOperationException($"Full render failed: red={red}, white={white}");
                File.WriteAllText("Library/SdfOutline-render.txt", $"PASS full RenderGraph capture / compute / composite: red outline pixels={red}, white object pixels={white}. Three preview PNGs saved in Library.");
            }
            finally
            {
                RenderTexture.active = previous;
                settings.Update(); list = settings.FindProperty("m_RendererDataList"); list.arraySize = count; settings.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.ClosePreviewScene(scene);
                target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(material);
                if (image) UnityEngine.Object.DestroyImmediate(image);
                // Keep the validation renderer asset available for manual inspection.
            }
        }
    }
}
