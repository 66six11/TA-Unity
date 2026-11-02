using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TA.SdfOutline.Editor
{
    [InitializeOnLoad]
    public static class SdfOutlineSetup
    {
        const string Root = "Assets/SdfOutline";
        static SdfOutlineSetup() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists("Library/SdfOutline.request")) return;
            File.Delete("Library/SdfOutline.request");
            try { Validate(); } catch (Exception e) { File.WriteAllText("Library/SdfOutline-validation.txt", e.ToString()); Debug.LogException(e); }
        }
        [MenuItem("Tools/TA/SDF Outline/Install on Default Renderer")]
        public static void Install()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (!pipeline) throw new InvalidOperationException("An active URP pipeline is required.");
            var serialized = new SerializedObject(pipeline);
            int index = serialized.FindProperty("m_DefaultRendererIndex").intValue;
            var renderer = serialized.FindProperty("m_RendererDataList").GetArrayElementAtIndex(index).objectReferenceValue as UniversalRendererData;
            if (!renderer) throw new InvalidOperationException("Universal Renderer required.");
            SdfOutlineFeature feature = null;
            foreach (var item in renderer.rendererFeatures) if (item is SdfOutlineFeature sdf) feature = sdf;
            if (!feature)
            {
                feature = ScriptableObject.CreateInstance<SdfOutlineFeature>(); feature.name = "Screen Space SDF Outline";
                AssetDatabase.AddObjectToAsset(feature, renderer); renderer.rendererFeatures.Add(feature);
            }
            feature.distanceCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(Root + "/Shaders/SdfOutline.compute");
            feature.maskShader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Shaders/SdfOutlineMask.shader");
            feature.compositeShader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Shaders/SdfOutlineComposite.shader");
            feature.Create(); feature.SetActive(true);
            EditorUtility.SetDirty(feature); EditorUtility.SetDirty(renderer); renderer.SetDirty(); AssetDatabase.SaveAssets();
            Selection.activeObject = renderer;
            Debug.Log("SDF Outline installed. Set Target Layers on its Renderer Feature to choose opaque targets.");
        }
        [MenuItem("Tools/TA/SDF Outline/Validate GPU Distance Field")]
        public static void Validate()
        {
            foreach (string file in new[] { "SdfOutlineMask", "SdfOutlineComposite" })
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Shaders/" + file + ".shader");
                if (!shader) throw new InvalidOperationException("Missing shader: " + file);
                var material = new Material(shader);
                try { ShaderUtil.CompilePass(material, 0, true); }
                finally { UnityEngine.Object.DestroyImmediate(material); }
                if (ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Shader error: " + file);
            }
            var cs = AssetDatabase.LoadAssetAtPath<ComputeShader>(Root + "/Shaders/SdfOutline.compute");
            const int size = 32;
            var mask = new Texture2D(size, size, TextureFormat.RFloat, false, true);
            var a = Target(RenderTextureFormat.RGFloat); var b = Target(RenderTextureFormat.RGFloat); var distance = Target(RenderTextureFormat.RFloat);
            RenderTexture Target(RenderTextureFormat format)
            {
                var rt = new RenderTexture(size, size, 0, format) { enableRandomWrite = true }; rt.Create(); return rt;
            }
            var previous = RenderTexture.active;
            Texture2D read = null;
            try
            {
                var pixels = new Color[size * size];
                for (int y = 8; y < 24; y++) for (int x = 8; x < 24; x++) pixels[y * size + x] = Color.white;
                mask.SetPixels(pixels); mask.Apply();
                cs.SetVector("_Size", new Vector4(size, size, 0, 0));
                int seed = cs.FindKernel("Seed"), jump = cs.FindKernel("Jump"), finish = cs.FindKernel("Distance");
                cs.SetTexture(seed, "_Mask", mask); cs.SetTexture(seed, "_SeedsOut", a); cs.Dispatch(seed, 4, 4, 1);
                for (int step = 16; step >= 1; step /= 2)
                {
                    cs.SetInt("_Step", step); cs.SetTexture(jump, "_Seeds", a); cs.SetTexture(jump, "_SeedsOut", b); cs.Dispatch(jump, 4, 4, 1);
                    (a, b) = (b, a);
                }
                cs.SetTexture(finish, "_Mask", mask); cs.SetTexture(finish, "_Seeds", a); cs.SetTexture(finish, "_DistanceOut", distance); cs.Dispatch(finish, 4, 4, 1);
                RenderTexture.active = distance;
                read = new Texture2D(size, size, TextureFormat.RFloat, false, true); read.ReadPixels(new Rect(0, 0, size, size), 0, 0); read.Apply();
                float inner = read.GetPixel(8, 16).r, outer = read.GetPixel(7, 16).r, center = read.GetPixel(16, 16).r;
                if (Mathf.Abs(inner + 0.5f) > 0.01f || Mathf.Abs(outer - 0.5f) > 0.01f || Mathf.Abs(center + 7.5f) > 0.01f)
                    throw new InvalidOperationException($"Unexpected SDF: inner={inner}, outer={outer}, center={center}");
                // Empty input must produce a finite positive sentinel, with no stale outline.
                Array.Clear(pixels, 0, pixels.Length); mask.SetPixels(pixels); mask.Apply();
                cs.SetTexture(seed, "_SeedsOut", a); cs.Dispatch(seed, 4, 4, 1);
                cs.SetTexture(finish, "_Seeds", a); cs.Dispatch(finish, 4, 4, 1);
                read.ReadPixels(new Rect(0, 0, size, size), 0, 0); read.Apply();
                if (read.GetPixel(16, 16).r < 10000) throw new InvalidOperationException("Empty mask retained seeds.");
                File.WriteAllText("Library/SdfOutline-validation.txt", $"PASS: C# reload; both raster shaders compiled; GPU Seed/Jump/Distance: inner={inner}, outer={outer}, center={center}; empty mask passed.");
                Debug.Log("SDF Outline GPU validation passed.");
            }
            finally
            {
                RenderTexture.active = previous;
                foreach (var rt in new[] { a, b, distance }) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
                UnityEngine.Object.DestroyImmediate(mask); if (read) UnityEngine.Object.DestroyImmediate(read);
            }
        }
    }
}
