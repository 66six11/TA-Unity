using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TA.PixelRender.Editor
{
    // Read-only capture of the saved demo, without rebuilding or saving the user's scene.
    [InitializeOnLoad]
    public static class PixelAbstractionCapture
    {
        const string Request = "Library/PixelAbstraction.capture-request";
        static PixelAbstractionCapture() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete(Request);
            try { Capture(); }
            catch (Exception e) { File.WriteAllText("Library/PixelAbstraction-capture.txt", e.ToString()); Debug.LogException(e); }
        }
        [MenuItem("Tools/TA/Pixel Render/Export Abstraction Source")]
        public static void Capture()
        {
            // Preview scenes keep external cameras, lighting and objects out of this capture.
            var scene = EditorSceneManager.OpenPreviewScene("Assets/PixelRender/PixelRenderDemo.unity");
            RenderTexture rt = null;
            Texture2D image = null;
            var previous = RenderTexture.active;
            try
            {
                Camera camera = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    var pixel = root.GetComponentInChildren<PixelCamera>();
                    if (!pixel) continue;
                    camera = pixel.GetComponent<Camera>();
                    pixel.enabled = false;
                    break;
                }
                if (!camera) throw new InvalidOperationException("Saved pixel demo has no PixelCamera.");
                camera.aspect = 16f / 9f;
                camera.targetTexture = null;
                rt = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32);
                image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                RenderTexture.active = rt;
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                Directory.CreateDirectory("Assets/PixelRender/Experiments/Output");
                File.WriteAllBytes("Assets/PixelRender/Experiments/Output/Source.png", image.EncodeToPNG());
                File.WriteAllText("Library/PixelAbstraction-capture.txt", "PASS: isolated 1280x720 saved-scene capture.");
                AssetDatabase.Refresh();
            }
            finally
            {
                RenderTexture.active = previous;
                if (rt) RenderTexture.ReleaseTemporary(rt);
                if (image) UnityEngine.Object.DestroyImmediate(image);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
