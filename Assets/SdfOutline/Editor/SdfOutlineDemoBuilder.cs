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
    public static class SdfOutlineDemoBuilder
    {
        const string Root = "Assets/SdfOutline";
        const string ScenePath = Root + "/SdfOutlineDemo.unity";
        const int TargetLayer = 31, EnvironmentLayer = 30;
        static SdfOutlineDemoBuilder() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists("Library/SdfOutline-demo.request")) return;
            string command = File.ReadAllText("Library/SdfOutline-demo.request").Trim();
            if (EditorApplication.isPlayingOrWillChangePlaymode && command != "verify") return;
            File.Delete("Library/SdfOutline-demo.request");
            try { if (EditorApplication.isPlaying) VerifyExisting(); else Build(); } catch (Exception e) { File.WriteAllText("Library/SdfOutline-demo.txt", e.ToString()); Debug.LogException(e); }
        }
        [MenuItem("Tools/TA/SDF Outline/Create Demo Scene")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before creating the demo.");
            if (File.Exists(ScenePath)) { Open(); VerifyExisting(); return; }
            Directory.CreateDirectory(Root + "/Demo"); AssetDatabase.Refresh();
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (!pipeline) throw new InvalidOperationException("URP pipeline required.");
            var settings = new SerializedObject(pipeline);
            var list = settings.FindProperty("m_RendererDataList");
            var original = list.GetArrayElementAtIndex(settings.FindProperty("m_DefaultRendererIndex").intValue).objectReferenceValue as UniversalRendererData;
            string rendererPath = Root + "/Demo/SdfOutlineRenderer.asset";
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (!renderer)
            {
                AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(original), rendererPath);
                renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath); renderer.rendererFeatures.Clear();
            }
            SdfOutlineFeature feature = null;
            foreach (var item in renderer.rendererFeatures) if (item is SdfOutlineFeature sdf) feature = sdf;
            if (!feature)
            {
                feature = ScriptableObject.CreateInstance<SdfOutlineFeature>(); feature.name = "SDF Outline Demo";
                AssetDatabase.AddObjectToAsset(feature, renderer); renderer.rendererFeatures.Add(feature);
            }
            feature.distanceCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(Root + "/Shaders/SdfOutline.compute");
            feature.maskShader = Shader.Find("TA/SDF Outline Mask"); feature.compositeShader = Shader.Find("TA/SDF Outline Composite");
            feature.targetLayers = 1 << TargetLayer; feature.width = 5; feature.outlineColor = new Color(1, 0.57f, 0.08f, 1);
            feature.Create(); feature.SetActive(true);
            renderer.SetDirty(); EditorUtility.SetDirty(renderer); EditorUtility.SetDirty(feature);
            int index = -1;
            for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).objectReferenceValue == renderer) index = i;
            if (index < 0) { index = list.arraySize; list.arraySize++; list.GetArrayElementAtIndex(index).objectReferenceValue = renderer; settings.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(pipeline); }
            AssetDatabase.SaveAssets();
            bool dirty = false;
            for (int i = 0; i < SceneManager.sceneCount; i++) dirty |= SceneManager.GetSceneAt(i).isDirty;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            Material Mat(string name, Color color)
            {
                string path = Root + "/Demo/" + name + ".mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(m, path); }
                m.SetColor("_BaseColor", color); EditorUtility.SetDirty(m); return m;
            }
            var blue = Mat("Target Blue", new Color(0.13f, 0.36f, 0.53f));
            var teal = Mat("Target Teal", new Color(0.12f, 0.48f, 0.43f));
            var wall = Mat("Occluder", new Color(0.24f, 0.29f, 0.37f));
            var platform = Mat("Platform", new Color(0.055f, 0.085f, 0.13f));
            GameObject Shape(string name, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat, bool target = true)
            {
                var go = GameObject.CreatePrimitive(type); go.name = name; go.layer = target ? TargetLayer : EnvironmentLayer;
                SceneManager.MoveGameObjectToScene(go, scene); go.transform.position = pos; go.transform.localScale = scale;
                go.GetComponent<Renderer>().sharedMaterial = mat; return go;
            }
            void Label(string text, Vector3 pos, float size, Color color)
            {
                var go = new GameObject(text); SceneManager.MoveGameObjectToScene(go, scene); go.layer = EnvironmentLayer; go.transform.position = pos;
                var label = go.AddComponent<TextMesh>(); label.text = text; label.anchor = TextAnchor.MiddleCenter; label.alignment = TextAlignment.Center;
                label.fontSize = 64; label.characterSize = size; label.color = color;
            }
            Shape("01 Sphere - curved silhouette", PrimitiveType.Sphere, new Vector3(-3.6f, 0.25f, 0), Vector3.one * 1.65f, blue);
            Shape("02 Capsule - partially occluded", PrimitiveType.Capsule, new Vector3(0, 0.25f, 0), new Vector3(1.15f, 1.1f, 1.15f), teal);
            Shape("Occluding bar - excluded from outline", PrimitiveType.Cube, new Vector3(0.5f, -0.15f, -1), new Vector3(1.25f, 0.6f, 0.35f), wall, false);
            var cube = Shape("03 Rotated cube", PrimitiveType.Cube, new Vector3(3.15f, 0.3f, 0), Vector3.one * 1.4f, blue);
            cube.transform.rotation = Quaternion.Euler(15, 25, 20);
            Shape("04 Overlapping sphere - union contour", PrimitiveType.Sphere, new Vector3(4.05f, -0.05f, -0.45f), Vector3.one * 1.1f, teal);
            foreach (float x in new[] { -3.6f, 0, 3.6f }) Shape("Display plinth", PrimitiveType.Cube, new Vector3(x, -1.25f, 0.15f), new Vector3(2.8f, 0.18f, 2), platform, false);
            Label("GPU / SCREEN SPACE SDF", new Vector3(0, 2.55f, -1.5f), 0.095f, Color.white);
            Label("5 PX OUTLINE   /   VISIBLE SILHOUETTE   /   JUMP FLOOD", new Vector3(0, 2.05f, -1.5f), 0.04f, new Color(0.55f, 0.68f, 0.8f));
            Label("01  CURVES", new Vector3(-3.6f, -1.8f, -1.5f), 0.045f, Color.white);
            Label("02  OCCLUSION", new Vector3(0, -1.8f, -1.5f), 0.045f, Color.white);
            Label("03  MERGED CONTOUR", new Vector3(3.6f, -1.8f, -1.5f), 0.045f, Color.white);
            var cameraGo = new GameObject("SDF Demo Camera"); SceneManager.MoveGameObjectToScene(cameraGo, scene); cameraGo.tag = "MainCamera";
            var camera = cameraGo.AddComponent<Camera>(); camera.transform.position = new Vector3(0, 0.3f, -14);
            camera.orthographic = true; camera.orthographicSize = 3.6f; camera.nearClipPlane = 0.1f; camera.farClipPlane = 40;
            camera.backgroundColor = new Color(0.018f, 0.028f, 0.048f); camera.clearFlags = CameraClearFlags.SolidColor;
            camera.cullingMask = (1 << TargetLayer) | (1 << EnvironmentLayer); camera.allowMSAA = false; camera.depth = 100;
            camera.GetUniversalAdditionalCameraData().SetRenderer(index);
            camera.GetUniversalAdditionalCameraData().requiresDepthTexture = true;
            cameraGo.AddComponent<SdfOutlineControls>();
            EnsureControls(scene);
            EditorSceneManager.SaveScene(scene, ScenePath); AssetDatabase.SaveAssets();
            var previous = RenderTexture.active; var rt = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                RenderTexture.active = rt; image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                File.WriteAllBytes(Root + "/Demo/Preview.png", image.EncodeToPNG());
                int outlined = 0;
                foreach (var c in image.GetPixels()) if (c.r > 0.7f && c.g > 0.25f && c.g < 0.85f && c.b < 0.4f) outlined++;
                if (outlined < 100) throw new InvalidOperationException("Demo capture contains too few outline pixels: " + outlined);
                File.WriteAllText("Library/SdfOutline-demo.txt", "PASS: " + ScenePath + "; outline pixels=" + outlined + "; renderer index=" + index);
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); UnityEngine.Object.DestroyImmediate(image); }
            AssetDatabase.Refresh();
            Selection.activeObject = renderer;
            if (SceneView.lastActiveSceneView) SceneView.lastActiveSceneView.AlignViewToObject(camera.transform);
            if (!dirty) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }
        static void EnsureControls(Scene scene)
        {
            Renderer sphere=null;
            foreach(var root in scene.GetRootGameObjects())if(root.name.StartsWith("01 Sphere"))sphere=root.GetComponent<Renderer>();
            bool changed=false;
            foreach(var root in scene.GetRootGameObjects())
                if(root.TryGetComponent<Camera>(out var camera))
                {
                    if(!camera.TryGetComponent<SdfOutlineControls>(out var view)){view=root.AddComponent<SdfOutlineControls>();changed=true;}
                    if(!view.convexTarget&&sphere){view.convexTarget=sphere;changed=true;}
                }
            if(changed&&!EditorApplication.isPlaying){EditorSceneManager.MarkSceneDirty(scene);if(!string.IsNullOrEmpty(scene.path))EditorSceneManager.SaveScene(scene);}
        }
        static void VerifyExisting()
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            Camera camera = null;
            foreach (var root in scene.GetRootGameObjects())
                if (root.TryGetComponent<Camera>(out var candidate)) camera = candidate;
            if (!camera) throw new InvalidOperationException("Demo camera missing.");
            EnsureControls(scene);
            var controls = camera.GetComponent<SdfOutlineControls>();
            var savedView = controls.display;
            controls.display = SdfOutlineFeature.Display.Composite;
            var previous = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                RenderTexture.active = rt; image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                int count = 0;
                foreach (var c in image.GetPixels()) if (Mathf.Max(c.r, c.g, c.b) > 0.25f) count++;
                if (count < 100) throw new InvalidOperationException("Missing demo outline.");
                File.WriteAllBytes(Root + "/Demo/Preview.png", image.EncodeToPNG());

                var signatures = new System.Collections.Generic.HashSet<int>();
                try
                {
                    foreach (SdfOutlineFeature.Display view in Enum.GetValues(typeof(SdfOutlineFeature.Display)))
                    {
                        controls.display = view;
                        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                        RenderTexture.active = rt; image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                        int hash = 17;
                        var pixels = image.GetPixels32();
                        unchecked { for (int i = 0; i < pixels.Length; i += 31) hash = hash * 31 + pixels[i].GetHashCode(); }
                        if (!signatures.Add(hash)) throw new InvalidOperationException("Duplicate debug output: " + view);
                        File.WriteAllBytes("Library/SdfOutline-Demo-" + view + ".png", image.EncodeToPNG());
                    }
                    controls.display = SdfOutlineFeature.Display.Composite;
                    bool savedEnabled = controls.paperEnabled;
                    int[] noiseHashes = new int[2];
                    try
                    {
                        for (int enabled = 0; enabled < 2; enabled++)
                        {
                            controls.paperEnabled = enabled == 1;
                            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                            RenderTexture.active = rt; image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                            int hash = 17; var pixels = image.GetPixels32();
                            unchecked { for (int i = 0; i < pixels.Length; i += 31) hash = hash * 31 + pixels[i].GetHashCode(); }
                            noiseHashes[enabled] = hash;
                            File.WriteAllBytes("Library/SdfOutline-Paper-" + enabled + ".png", image.EncodeToPNG());
                        }
                        if (noiseHashes[0] == noiseHashes[1]) throw new InvalidOperationException("Paper toggle did not change rendering (check texture import).");
                    }
                    finally { controls.paperEnabled = savedEnabled; }
                    bool savedLines = controls.linesEnabled;
                    float savedSpacing = controls.lineSpacing;
                    var lineHashes = new System.Collections.Generic.HashSet<int>();
                    try
                    {
                        for (int variant = 0; variant < 3; variant++)
                        {
                            controls.linesEnabled = variant != 0;
                            controls.lineSpacing = variant == 2 ? 16 : 8;
                            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                            RenderTexture.active = rt; image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                            int hash = 17; var pixels = image.GetPixels32();
                            unchecked { for (int i = 0; i < pixels.Length; i += 31) hash = hash * 31 + pixels[i].GetHashCode(); }
                            if (!lineHashes.Add(hash)) throw new InvalidOperationException("Contour line toggle or spacing did not affect output.");
                            File.WriteAllBytes("Library/SdfOutline-Lines-" + variant + ".png", image.EncodeToPNG());
                        }
                    }
                    finally { controls.linesEnabled = savedLines; controls.lineSpacing = savedSpacing; }

                }
                finally { controls.display = savedView; }
                bool dashState=controls.dashesEnabled;
                float dashDuty=controls.dashDuty,dashPhase=controls.dashPhase,axis=controls.projectionAngle;
                try
                {
                    controls.display=SdfOutlineFeature.Display.Composite;controls.dashesEnabled=true;
                    var hashes=new System.Collections.Generic.HashSet<int>();
                    for(int variant=0;variant<4;variant++)
                    {
                        controls.dashDuty=variant==0?1:0.5f;controls.dashPhase=variant==2?0.025f:0;controls.projectionAngle=variant==3?75:0;
                        RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
                        RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();
                        int hash=17;var pixels=image.GetPixels32();
                        unchecked{for(int i=0;i<pixels.Length;i+=31)hash=hash*31+pixels[i].GetHashCode();}
                        if(!hashes.Add(hash))throw new InvalidOperationException("Projection duty/phase/axis did not change rendering.");
                        File.WriteAllBytes("Library/SdfOutline-Projection-"+variant+".png",image.EncodeToPNG());
                    }
                }
                finally{controls.display=savedView;controls.dashesEnabled=dashState;controls.dashDuty=dashDuty;controls.dashPhase=dashPhase;controls.projectionAngle=axis;}
                bool savedSpike=controls.spikeDashesEnabled,savedDash=controls.dashesEnabled;
                float savedHeight=controls.spikeHeight,savedDuty=controls.dashDuty;
                int savedSeed=controls.spikeSeed;
                try
                {
                    controls.display=SdfOutlineFeature.Display.Composite;controls.dashesEnabled=true;
                    var hashes=new System.Collections.Generic.HashSet<int>();
                    for(int variant=0;variant<4;variant++)
                    {
                        controls.spikeDashesEnabled=variant!=0;controls.spikeHeight=24;
                        controls.spikeSeed=variant==2?83:17;controls.dashDuty=variant==3?0:0.65f;
                        RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
                        RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();
                        int hash=17;var pixels=image.GetPixels32();
                        unchecked{for(int j=0;j<pixels.Length;j+=31)hash=hash*31+pixels[j].GetHashCode();}
                        if(!hashes.Add(hash))throw new InvalidOperationException("Spike shape/seed/dash mask did not change rendering.");
                        File.WriteAllBytes("Library/SdfOutline-SpikeDash-"+variant+".png",image.EncodeToPNG());
                    }
                    controls.display=SdfOutlineFeature.Display.SpikeDashes;
                    RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
                    RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();
                    foreach(var pixel in image.GetPixels32())if(pixel.r!=0||pixel.g!=0||pixel.b!=0)throw new InvalidOperationException("Zero dash duty must suppress all spike coverage.");
                }
                finally{controls.display=savedView;controls.spikeDashesEnabled=savedSpike;controls.dashesEnabled=savedDash;controls.spikeHeight=savedHeight;controls.spikeSeed=savedSeed;controls.dashDuty=savedDuty;}
                int savedDashCount=controls.dashCount,savedSpikeCount=controls.spikeCount;
                float savedDashDuty=controls.dashDuty,savedDashPhase=controls.dashPhase;
                try
                {
                    controls.display=SdfOutlineFeature.Display.SpikeShape;
                    int originalHash=0;
                    for(int variant=0;variant<3;variant++)
                    {
                        controls.dashCount=variant==0?8:40;controls.dashDuty=variant==0?0.2f:0.8f;controls.dashPhase=variant==0?0:0.3f;
                        controls.spikeCount=variant==2?60:24;
                        RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
                        RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();
                        int hash=17;var pixels=image.GetPixels32();
                        unchecked{for(int j=0;j<pixels.Length;j+=31)hash=hash*31+pixels[j].GetHashCode();}
                        if(variant==0)originalHash=hash;
                        if(variant==1&&hash!=originalHash)throw new InvalidOperationException("Dash parameters changed the independent spike shape.");
                        if(variant==2&&hash==originalHash)throw new InvalidOperationException("Spike count did not change spike geometry.");
                        File.WriteAllBytes("Library/SdfOutline-IndependentSpikes-"+variant+".png",image.EncodeToPNG());
                    }
                }
                finally{controls.display=savedView;controls.dashCount=savedDashCount;controls.spikeCount=savedSpikeCount;controls.dashDuty=savedDashDuty;controls.dashPhase=savedDashPhase;}
                File.WriteAllText("Library/SdfOutline-demo.txt", "PASS saved demo scene rendered; camera controls attached; eleven debug views; spike shape independent of dash count/duty/phase; spike count and zero duty verified; outline pixels=" + count);
            }
            finally { controls.display = savedView; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); UnityEngine.Object.DestroyImmediate(image); }
        }
        [MenuItem("Tools/TA/SDF Outline/Open Demo Scene")]
        public static void Open()
        {
            if (!File.Exists(ScenePath)) { Build(); return; }
            bool dirty = false;
            for (int i = 0; i < SceneManager.sceneCount; i++) dirty |= SceneManager.GetSceneAt(i).isDirty;
            var existing = SceneManager.GetSceneByPath(ScenePath);
            if (existing.IsValid() && existing.isLoaded) { SceneManager.SetActiveScene(existing); EnsureControls(existing); return; }
            var scene = EditorSceneManager.OpenScene(ScenePath, dirty ? OpenSceneMode.Additive : OpenSceneMode.Single);
            SceneManager.SetActiveScene(scene);
            EnsureControls(scene);
        }
    }
}
