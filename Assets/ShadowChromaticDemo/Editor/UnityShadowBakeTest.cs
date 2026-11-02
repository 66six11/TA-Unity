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
    public static class UnityShadowBakeTest
    {
        const string Root = "Assets/UnityShadowBakeTest";
        const string ScenePath = Root + "/UnityShadowBakeTest.unity";
        const string Request = "Library/UnityShadowBake.request";
        const string Status = "Library/UnityShadowBake-validation.txt";
        const string ActiveKey = "TA.UnityShadowBake.Active";

        static UnityShadowBakeTest()
        {
            EditorApplication.update += CheckRequest;
            Lightmapping.bakeCompleted += OnBakeCompleted;
        }

        static void CheckRequest()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || Lightmapping.isRunning ||
                EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Request)) return;
            string command;
            try { command=File.ReadAllText(Request).Trim(); File.Delete(Request); }
            catch (IOException) { return; }
            try { if (command == "verify") Verify(); else BuildAndBake(); }
            catch (Exception e) { Fail(e); }
        }

        [MenuItem("Tools/TA/Unity Shadow Bake/Build Scene and Bake")]
        public static void BuildAndBake()
        {
            if (Lightmapping.isRunning) throw new InvalidOperationException("A light bake is already running.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save the currently modified scene before running this scene-building tool.");

            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Root + "/Preview");
            AssetDatabase.Refresh();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = null;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.24f, 0.36f, 0.65f);
            RenderSettings.ambientEquatorColor = new Color(0.12f, 0.17f, 0.28f);
            RenderSettings.ambientGroundColor = new Color(0.07f, 0.06f, 0.05f);
            var ground = Material("Neutral receiver", new Color(0.7f,0.7f,0.7f));
            var caster = Material("Warm neutral objects", new Color(0.76f,0.7f,0.6f));
            var wall = Material("Wall", new Color(0.75f,0.76f,0.78f));
            Shape("Ground - baked shadow receiver", PrimitiveType.Cube,
                new Vector3(0,-0.15f,0),new Vector3(12,0.3f,11),ground);
            Shape("Wall - baked shadow receiver", PrimitiveType.Cube,
                new Vector3(0,1.8f,-4),new Vector3(12,3.6f,0.25f),wall);
            Shape("Cube - static caster",PrimitiveType.Cube,
                new Vector3(-2,1,0),new Vector3(1.6f,2,1.6f),caster);
            Shape("Sphere - static caster",PrimitiveType.Sphere,
                new Vector3(0,0.95f,0),Vector3.one*1.9f,caster);
            Shape("Pillar - static caster",PrimitiveType.Cylinder,
                new Vector3(2.4f,1.5f,0.3f),new Vector3(0.8f,1.5f,0.8f),caster);
            Shape("Raised beam - wall shadow test",PrimitiveType.Cube,
                new Vector3(-1,2.6f,-1.6f),new Vector3(3,0.3f,0.4f),caster);

            var sun = new GameObject("Mixed Sun - Unity Shadowmask").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.lightmapBakeType = LightmapBakeType.Mixed;
            sun.color = new Color(1,0.85f,0.68f);
            sun.intensity = 2;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 1;
            sun.shadowNormalBias = 0.2f;
            sun.shadowBias = 0.04f;
            sun.shadowAngle = 3;
            sun.transform.rotation = Quaternion.Euler(48,145,0);
            RenderSettings.sun = sun;

            var camera = new GameObject("Main Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(11,9,13);
            camera.transform.LookAt(new Vector3(0,1,-0.8f));
            camera.fieldOfView = 45;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 60;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.055f,0.065f,0.09f);
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;

            var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(Root + "/ShadowmaskLighting.lighting");
            if (!settings)
            {
                settings = new LightingSettings();
                AssetDatabase.CreateAsset(settings, Root + "/ShadowmaskLighting.lighting");
            }
            settings.bakedGI = true;
            settings.realtimeGI = false;
            settings.mixedBakeMode = MixedLightingMode.Shadowmask;
            settings.lightmapper = LightingSettings.Lightmapper.ProgressiveCPU;
            settings.lightmapResolution = 12;
            settings.lightmapMaxSize = 1024;
            settings.directionalityMode = LightmapsMode.NonDirectional;
            settings.directSampleCount = 64;
            settings.indirectSampleCount = 64;
            settings.environmentSampleCount = 64;
            settings.maxBounces = 2;
            settings.filteringMode = LightingSettings.FilterMode.None;
            Lightmapping.lightingSettings = settings;
            EditorUtility.SetDirty(settings);
            EditorSceneManager.SaveScene(scene,ScenePath);
            AssetDatabase.SaveAssets();
            SessionState.SetBool(ActiveKey,true);
            File.WriteAllText(Status,"BAKING: Unity Progressive Lightmapper, Mixed / Shadowmask.\nScene: " + ScenePath);
            if (!Lightmapping.BakeAsync()) throw new InvalidOperationException("Unity refused to start the bake.");
        }

        static void OnBakeCompleted()
        {
            if (!SessionState.GetBool(ActiveKey,false)) return;
            SessionState.SetBool(ActiveKey,false);
            EditorApplication.delayCall += () =>
            {
                try { Verify(); }
                catch (Exception e) { Fail(e); }
            };
        }

        [MenuItem("Tools/TA/Unity Shadow Bake/Verify Baked Shadowmask")]
        public static void Verify()
        {
            if (SceneManager.GetActiveScene().path != ScenePath)
                throw new InvalidOperationException("Open UnityShadowBakeTest.unity first.");
            var report = new List<string> { "Unity " + Application.unityVersion, "Native URP Lit + Unity Mixed Shadowmask" };
            var maps = LightmapSettings.lightmaps;
            if (maps.Length == 0) throw new Exception("No lightmaps were produced.");
            int maskCount = 0;
            foreach (var map in maps) if (map.shadowMask) maskCount++;
            if (maskCount == 0) throw new Exception("No Shadowmask textures were produced.");
            var sun = RenderSettings.sun;
            int channel = sun.bakingOutput.occlusionMaskChannel;
            if (channel < 0 || channel > 3) throw new Exception("Mixed light has no baked shadowmask channel.");
            report.Add("Lightmaps: " + maps.Length + "; Shadowmask textures: " + maskCount + "; Sun mask channel: " + channel);
            report.Add("Lighting data: " + AssetDatabase.GetAssetPath(Lightmapping.lightingDataAsset));
            foreach (var map in maps)
                if (map.shadowMask) report.Add("Shadowmask: " + AssetDatabase.GetAssetPath(map.shadowMask));

            var renderers = UnityEngine.Object.FindObjectsByType<MeshRenderer>();
            var modes = new ShadowCastingMode[renderers.Length];
            var originalQuality = QualitySettings.shadowmaskMode;
            Texture2D baked = null, white = null;
            try
            {
                QualitySettings.shadowmaskMode = ShadowmaskMode.Shadowmask;
                // Realtime shadow maps are empty: no scene mesh is allowed to cast.
                for (int i=0;i<renderers.Length;i++)
                {
                    modes[i]=renderers[i].shadowCastingMode;
                    renderers[i].shadowCastingMode=ShadowCastingMode.Off;
                }
                var camera = Camera.main;
                baked = CaptureStable(camera);
                WritePreview("UnityBakedShadowmask.png",baked);
                var unmasked = new LightmapData[maps.Length];
                for (int i=0;i<maps.Length;i++)
                    unmasked[i] = new LightmapData { lightmapColor=maps[i].lightmapColor,
                        lightmapDir=maps[i].lightmapDir, shadowMask=Texture2D.whiteTexture };
                LightmapSettings.lightmaps=unmasked;
                white=CaptureStable(camera);
                WritePreview("WhiteShadowmaskControl.png",white);
                int differences=0;
                var aa=baked.GetPixels32();
                var bb=white.GetPixels32();
                int visiblePixels=0;
                for (int i=0;i<aa.Length;i++)
                {
                    if (Mathf.Abs(aa[i].r-aa[0].r)>5 || Mathf.Abs(aa[i].g-aa[0].g)>5 || Mathf.Abs(aa[i].b-aa[0].b)>5) visiblePixels++;
                    if (Mathf.Abs(aa[i].r-bb[i].r)>3 || Mathf.Abs(aa[i].g-bb[i].g)>3 || Mathf.Abs(aa[i].b-bb[i].b)>3) differences++;
                }
                if (visiblePixels<10000) throw new Exception("Baked comparison did not render the test geometry.");
                report.Add("Visible test geometry pixels: " + visiblePixels);
                report.Add("Realtime mesh shadow casting: OFF during comparison.");
                report.Add("Baked mask versus white mask changed pixels: " + differences);
                if (differences<300) throw new Exception("Baked Shadowmask did not visibly affect the image: " + differences);
                report.Add("PASS: native Unity bake produced masks and URP Lit uses them without realtime mesh shadows.");
            }
            finally
            {
                LightmapSettings.lightmaps=maps;
                QualitySettings.shadowmaskMode=originalQuality;
                for (int i=0;i<renderers.Length;i++) if (renderers[i]) renderers[i].shadowCastingMode=modes[i];
                if (baked) UnityEngine.Object.DestroyImmediate(baked);
                if (white) UnityEngine.Object.DestroyImmediate(white);
            }
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            File.WriteAllLines(Status,report);
            File.WriteAllLines(Root + "/Validation.txt",report);
            AssetDatabase.Refresh();
            Debug.Log(string.Join("\n",report));
        }

        static Material Material(string name, Color color)
        {
            string path=Root + "/" + name + ".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                material=new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material,path);
            }
            material.SetColor("_BaseColor",color);
            material.SetFloat("_Smoothness",0.15f);
            EditorUtility.SetDirty(material);
            return material;
        }

        static void Shape(string name,PrimitiveType type,Vector3 position,Vector3 scale,Material material)
        {
            var obj=GameObject.CreatePrimitive(type);
            obj.name=name;
            obj.transform.position=position;
            obj.transform.localScale=scale;
            GameObjectUtility.SetStaticEditorFlags(obj,StaticEditorFlags.ContributeGI);
            var renderer=obj.GetComponent<MeshRenderer>();
            renderer.sharedMaterial=material;
            renderer.receiveGI=ReceiveGI.Lightmaps;
        }

        static Texture2D Capture(Camera camera)
        {
            var rt=RenderTexture.GetTemporary(1280,800,24,RenderTextureFormat.ARGB32);
            var previous=RenderTexture.active;
            var texture=new Texture2D(1280,800,TextureFormat.RGB24,false);
            try
            {
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest {destination=rt});
                RenderTexture.active=rt;
                texture.ReadPixels(new Rect(0,0,1280,800),0,0);
                texture.Apply();
                return texture;
            }
            catch { UnityEngine.Object.DestroyImmediate(texture); throw; }
            finally { RenderTexture.active=previous; RenderTexture.ReleaseTemporary(rt); }
        }

        static Texture2D CaptureStable(Camera camera)
        {
            // The first render immediately after a bake can initialize GPU resident
            // renderer/lightmap state without drawing. Never use that frame as evidence.
            var warmup=Capture(camera);
            UnityEngine.Object.DestroyImmediate(warmup);
            return Capture(camera);
        }

        static void WritePreview(string filename,Texture2D texture)
        {
            string path=Root + "/Preview/" + filename;
            File.WriteAllBytes(path+".tmp",texture.EncodeToPNG());
            if (File.Exists(path)) File.Replace(path+".tmp",path,null);
            else File.Move(path+".tmp",path);
        }

        static void Fail(Exception exception)
        {
            SessionState.SetBool(ActiveKey,false);
            File.WriteAllText(Status,exception.ToString());
            Debug.LogException(exception);
        }
    }
}
