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
    public static class MixedShadowReceiverTest
    {
        const string Root="Assets/UnityShadowBakeTest/Comparison";
        const string ScenePath="Assets/UnityShadowBakeTest/UnityShadowBakeTest.unity";
        const string Request="Library/MixedShadowReceiver.request";
        const string Status="Library/MixedShadowReceiver-validation.txt";

        static MixedShadowReceiverTest() => EditorApplication.update+=Poll;

        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || Lightmapping.isRunning ||
                EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Request)) return;
            string command;
            try { command=File.ReadAllText(Request).Trim(); File.Delete(Request); }
            catch (IOException) { return; }
            try
            {
                if (command=="verify") Verify();
                else InstallAndVerify();
            }
            catch (Exception e)
            {
                File.WriteAllText(Status,e.ToString());
                Debug.LogException(e);
            }
        }

        [MenuItem("Tools/TA/Mixed Shadow Receiver/Install and Verify")]
        public static void InstallAndVerify()
        {
            if (SceneManager.GetActiveScene().path!=ScenePath)
            {
                for (int i=0;i<SceneManager.sceneCount;i++)
                    if (SceneManager.GetSceneAt(i).isDirty)
                        throw new InvalidOperationException("Save the current scene before opening the baked test scene.");
                EditorSceneManager.OpenScene(ScenePath);
            }
            var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/ShadowChromaticDemo/Shaders/UnityMixedShadowReceiver.shader");
            if (!shader) throw new Exception("Receiver shader not imported.");
            if (LightmapSettings.lightmaps.Length==0) throw new Exception("The native bake must exist before installing receivers.");
            Directory.CreateDirectory(Root);
            AssetDatabase.Refresh();
            var camera=Camera.main;
            if (!camera) throw new Exception("No test camera found.");
            var controls=camera.GetComponent<UnityMixedShadowComparison>();
            if (!controls) controls=camera.gameObject.AddComponent<UnityMixedShadowComparison>();

            // A non-static object exercises the SH/probe path alongside lightmapped surfaces.
            var fallback=GameObject.Find("SH fallback - non-lightmapped receiver");
            if (!fallback)
            {
                fallback=GameObject.CreatePrimitive(PrimitiveType.Sphere);
                fallback.name="SH fallback - non-lightmapped receiver";
                fallback.transform.position=new Vector3(4.25f,0.65f,1.5f);
                fallback.transform.localScale=Vector3.one*1.3f;
                fallback.GetComponent<Renderer>().sharedMaterial=
                    AssetDatabase.LoadAssetAtPath<Material>("Assets/UnityShadowBakeTest/Neutral receiver.mat");
            }
            var receivers=UnityEngine.Object.FindObjectsByType<MeshRenderer>();
            var native=new Material[receivers.Length];
            var custom=new Material[receivers.Length];
            var cache=new Dictionary<Material,Material>();
            for (int i=0;i<receivers.Length;i++)
            {
                var source=receivers[i].sharedMaterial;
                if (controls.receivers!=null)
                    for (int j=0;j<controls.receivers.Length;j++)
                        if (controls.receivers[j]==receivers[i] && j<controls.nativeMaterials.Length)
                            source=controls.nativeMaterials[j];
                if (!source || source.shader.name!="Universal Render Pipeline/Lit")
                    throw new Exception("Expected a native Lit source for " + receivers[i].name);
                native[i]=source;
                if (!cache.TryGetValue(source,out var receiverMaterial))
                {
                    string path=Root + "/" + source.name + " Receiver.mat";
                    receiverMaterial=AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (!receiverMaterial)
                    {
                        receiverMaterial=new Material(source) { shader=shader };
                        AssetDatabase.CreateAsset(receiverMaterial,path);
                    }
                    receiverMaterial.shader=shader;
                    receiverMaterial.CopyPropertiesFromMaterial(source);
                    receiverMaterial.shaderKeywords=source.shaderKeywords;
                    EditorUtility.SetDirty(receiverMaterial);
                    cache.Add(source,receiverMaterial);
                }
                custom[i]=receiverMaterial;
            }
            controls.receivers=receivers;
            controls.nativeMaterials=native;
            controls.customMaterials=custom;
            controls.mixedSun=RenderSettings.sun;
            controls.useNativeLit=false;
            controls.viewMode=0;
            controls.Apply();
            EditorUtility.SetDirty(controls);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Verify();
        }

        [MenuItem("Tools/TA/Mixed Shadow Receiver/Verify Current Scene")]
        public static void Verify()
        {
            var camera=Camera.main;
            var controls=camera ? camera.GetComponent<UnityMixedShadowComparison>() : null;
            if (!controls) throw new Exception("Install the mixed-shadow receiver first.");
            var report=new List<string> {"Unity " + Application.unityVersion,
                "URP native PBR inputs: lightmap/SH + Shadowmask + realtime shadows."};
            Directory.CreateDirectory(Root);
            var maps=LightmapSettings.lightmaps;
            if (maps.Length==0) throw new Exception("No baked lightmap data loaded.");
            var quality=QualitySettings.shadowmaskMode;
            bool savedArtEnabled=controls.artEnabled;
            float savedArtStrength=controls.artStrength;
            float savedTerminatorWidth=controls.artTerminatorWidth;
            Color savedShadowColor=controls.shadowEdgeColor;
            Color savedLightColor=controls.lightEdgeColor;
            Color savedSunColor=controls.mixedSun.color;
            // Match the current receiver inputs, including edits made since install.
            // Keep the saved native materials intact; compare temporary Lit copies.
            var savedNativeMaterials=controls.nativeMaterials;
            var comparisonMaterials=new Material[controls.customMaterials.Length];
            var nativeShader=Shader.Find("Universal Render Pipeline/Lit");
            for (int i=0;i<comparisonMaterials.Length;i++)
                comparisonMaterials[i]=new Material(controls.customMaterials[i]) {shader=nativeShader};
            controls.nativeMaterials=comparisonMaterials;
            var modes=new ShadowCastingMode[controls.receivers.Length];
            var textures=new List<Texture2D>();
            for (int i=0;i<modes.Length;i++) modes[i]=controls.receivers[i].shadowCastingMode;
            int bakedCount=0,shCount=0;
            foreach (var receiver in controls.receivers)
                if (receiver.lightmapIndex>=0 && receiver.lightmapIndex<maps.Length) bakedCount++;
                else shCount++;
            if (bakedCount==0 || shCount==0) throw new Exception("Both lightmapped and SH fallback receivers are required.");
            report.Add("Lightmapped receivers: " + bakedCount + "; SH/probe fallback receivers: " + shCount);
            try
            {
                controls.artEnabled=false;
                controls.viewMode=0;
                QualitySettings.shadowmaskMode=ShadowmaskMode.DistanceShadowmask;
                controls.useNativeLit=true;
                controls.Apply();
                var native=CaptureStable(camera,textures);
                Write("NativeLit.png",native);
                controls.useNativeLit=false;
                controls.Apply();
                var custom=CaptureStable(camera,textures);
                Write("MixedReceiver.png",custom);
                CompareEquivalent(report,"Distance Shadowmask: native vs custom",native,custom);

                QualitySettings.shadowmaskMode=ShadowmaskMode.Shadowmask;
                foreach (var receiver in controls.receivers) receiver.shadowCastingMode=ShadowCastingMode.Off;
                controls.useNativeLit=true;
                controls.Apply();
                var nativeBaked=CaptureStable(camera,textures);
                Write("NativeBakedOnly.png",nativeBaked);
                controls.useNativeLit=false;
                controls.Apply();
                var customBaked=CaptureStable(camera,textures);
                Write("CustomBakedOnly.png",customBaked);
                CompareEquivalent(report,"Realtime casting OFF: native vs custom",nativeBaked,customBaked);

                var noMask=new LightmapData[maps.Length];
                var noGI=new LightmapData[maps.Length];
                for (int i=0;i<maps.Length;i++)
                {
                    noMask[i]=new LightmapData {lightmapColor=maps[i].lightmapColor,
                        lightmapDir=maps[i].lightmapDir,shadowMask=Texture2D.whiteTexture};
                    noGI[i]=new LightmapData {lightmapColor=Texture2D.blackTexture,
                        lightmapDir=maps[i].lightmapDir,shadowMask=maps[i].shadowMask};
                }
                LightmapSettings.lightmaps=noMask;
                var whiteMask=CaptureStable(camera,textures);
                Write("CustomWhiteMask.png",whiteMask);
                int maskChanged=DifferentPixels(customBaked,whiteMask,3);
                report.Add("Custom baked mask vs white mask changed pixels: " + maskChanged);
                if (maskChanged<300) throw new Exception("Custom receiver did not consume Shadowmask.");
                LightmapSettings.lightmaps=noGI;
                var blackGI=CaptureStable(camera,textures);
                Write("CustomWithoutBakedGI.png",blackGI);
                int giChanged=DifferentPixels(customBaked,blackGI,3);
                report.Add("Custom lightmap vs black GI changed pixels: " + giChanged);
                if (giChanged<300) throw new Exception("Custom receiver did not consume the baked lightmap.");
                LightmapSettings.lightmaps=maps;
                controls.viewMode=1;
                controls.Apply();
                Write("BakedGIAndSH.png",CaptureStable(camera,textures));
                controls.viewMode=2;
                controls.Apply();
                Write("MixedVisibility.png",CaptureStable(camera,textures));
                controls.viewMode=3;
                controls.Apply();
                Write("BakedShadowmask.png",CaptureStable(camera,textures));

                // Artistic treatment must work on baked shadows as well as realtime PCF.
                controls.artEnabled=true;
                controls.artStrength=0.8f;
                controls.artTerminatorWidth=0.6f;
                controls.shadowEdgeColor=new Color(0.55f,0.2f,0.78f);
                controls.lightEdgeColor=new Color(1,0.48f,0.12f);
                controls.viewMode=4;
                controls.Apply();
                var artMask=CaptureStable(camera,textures);
                Write("ArtTransitionMask.png",artMask);
                controls.viewMode=0;
                controls.Apply();
                var artBaked=CaptureStable(camera,textures);
                Write("ArtisticBakedShadow.png",artBaked);
                int artBakedChanges=DifferentPixels(customBaked,artBaked,3);
                report.Add("Art on baked-only shadows changed pixels: " + artBakedChanges);
                if (artBakedChanges<200) throw new Exception("Artistic colors did not affect baked penumbrae.");
                int outsideChanges=OutsideMaskDifferences(customBaked,artBaked,artMask);
                report.Add("Art changed pixels outside transition mask (>3/255): " + outsideChanges);
                if (outsideChanges>25) throw new Exception("Art leaked outside the transition mask.");

                // Isolate the object's own shading: no realtime casting and a
                // completely white baked mask leave only the diffuse terminator.
                LightmapSettings.lightmaps=noMask;
                controls.Apply();
                var artSelf=CaptureStable(camera,textures);
                Write("ArtisticSelfShadow.png",artSelf);
                int selfChanges=DifferentPixels(whiteMask,artSelf,3);
                report.Add("Art on self-shadow terminators (cast masks white) changed pixels: " + selfChanges);
                if (selfChanges<200) throw new Exception("Art did not affect the surface's own light/shadow transition.");
                controls.viewMode=4;
                controls.Apply();
                Write("SelfShadowTransitionMask.png",CaptureStable(camera,textures));
                LightmapSettings.lightmaps=maps;
                controls.viewMode=0;

                QualitySettings.shadowmaskMode=ShadowmaskMode.DistanceShadowmask;
                for (int i=0;i<modes.Length;i++) controls.receivers[i].shadowCastingMode=modes[i];
                controls.Apply();
                var artRealtime=CaptureStable(camera,textures);
                Write("ArtisticMixedShadow.png",artRealtime);
                int artRealtimeChanges=DifferentPixels(custom,artRealtime,3);
                report.Add("Art on realtime/mixed shadows changed pixels: " + artRealtimeChanges);
                if (artRealtimeChanges<50) throw new Exception("Artistic colors did not affect realtime penumbrae.");
                // Removing direct radiance must remove the artistic contribution,
                // without tinting the already baked indirect lighting.
                controls.mixedSun.color=Color.black;
                var indirectArt=CaptureStable(camera,textures);
                controls.artEnabled=false;
                controls.Apply();
                var indirectNative=CaptureStable(camera,textures);
                CompareEquivalent(report,"No direct radiance: art vs unchanged GI",indirectNative,indirectArt);
                controls.mixedSun.color=savedSunColor;
                controls.artEnabled=true;
                controls.artStrength=0;
                controls.Apply();
                var zeroArt=CaptureStable(camera,textures);
                CompareEquivalent(report,"Art strength zero vs native",native,zeroArt);
                var shader=controls.customMaterials[0].shader;
                if (ShaderUtil.ShaderHasError(shader)) throw new Exception("Custom shader reported a compilation error.");
                report.Add("PASS: native baseline, GI/SH and masks, artistic realtime/baked penumbrae, zero-strength restoration.");
            }
            finally
            {
                LightmapSettings.lightmaps=maps;
                controls.mixedSun.color=savedSunColor;
                QualitySettings.shadowmaskMode=quality;
                controls.nativeMaterials=savedNativeMaterials;
                for (int i=0;i<modes.Length;i++) if (controls.receivers[i]) controls.receivers[i].shadowCastingMode=modes[i];
                controls.useNativeLit=false;
                controls.viewMode=0;
                controls.artEnabled=savedArtEnabled;
                controls.artStrength=savedArtStrength;
                controls.artTerminatorWidth=savedTerminatorWidth;
                controls.shadowEdgeColor=savedShadowColor;
                controls.lightEdgeColor=savedLightColor;
                controls.Apply();
                foreach (var texture in textures) if (texture) UnityEngine.Object.DestroyImmediate(texture);
                foreach (var material in comparisonMaterials) UnityEngine.Object.DestroyImmediate(material);
            }
            EditorUtility.SetDirty(controls);
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            File.WriteAllLines(Status,report);
            File.WriteAllLines(Root + "/Validation.txt",report);
            AssetDatabase.Refresh();
            Debug.Log(string.Join("\n",report));
        }

        static void CompareEquivalent(List<string> report,string label,Texture2D native,Texture2D custom)
        {
            int changed=DifferentPixels(native,custom,2);
            report.Add(label + " changed pixels (>2/255): " + changed);
            if (changed>100) throw new Exception(label + " differs by " + changed + " pixels.");
        }

        static int DifferentPixels(Texture2D a,Texture2D b,int tolerance)
        {
            var aa=a.GetPixels32();
            var bb=b.GetPixels32();
            int changed=0;
            for (int i=0;i<aa.Length;i++)
                if (Mathf.Abs(aa[i].r-bb[i].r)>tolerance || Mathf.Abs(aa[i].g-bb[i].g)>tolerance ||
                    Mathf.Abs(aa[i].b-bb[i].b)>tolerance) changed++;
            return changed;
        }

        static int OutsideMaskDifferences(Texture2D baseline,Texture2D artistic,Texture2D mask)
        {
            var aa=baseline.GetPixels32();
            var bb=artistic.GetPixels32();
            var mm=mask.GetPixels32();
            int changed=0;
            for (int i=0;i<aa.Length;i++)
                if (mm[i].r==0 && mm[i].g==0 && mm[i].b==0 &&
                    (Mathf.Abs(aa[i].r-bb[i].r)>3 || Mathf.Abs(aa[i].g-bb[i].g)>3 || Mathf.Abs(aa[i].b-bb[i].b)>3)) changed++;
            return changed;
        }

        static Texture2D CaptureStable(Camera camera,List<Texture2D> textures)
        {
            var warmup=Capture(camera);
            UnityEngine.Object.DestroyImmediate(warmup);
            var texture=Capture(camera);
            var pixels=texture.GetPixels32();
            int visible=0;
            foreach (var pixel in pixels)
                if (Mathf.Abs(pixel.r-pixels[0].r)>5 || Mathf.Abs(pixel.g-pixels[0].g)>5 || Mathf.Abs(pixel.b-pixels[0].b)>5) visible++;
            if (visible<10000)
            {
                UnityEngine.Object.DestroyImmediate(texture);
                throw new Exception("Offscreen capture did not draw test geometry.");
            }
            textures.Add(texture);
            return texture;
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

        static void Write(string filename,Texture2D texture)
        {
            string path=Root+"/"+filename;
            File.WriteAllBytes(path+".tmp",texture.EncodeToPNG());
            if (File.Exists(path)) File.Replace(path+".tmp",path,null);
            else File.Move(path+".tmp",path);
        }
    }
}
