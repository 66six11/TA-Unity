using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TA.PixelRender.Editor
{
    [InitializeOnLoad]
    public static class PixelAASetup
    {
        const string ScenePath="Assets/PixelRender/PixelRenderDemo.unity";
        const string Report="Library/PixelAA-validation.txt";
        static PixelAASetup() { EditorApplication.update+=Poll; }
        static void Poll()
        {
            const string request="Library/PixelAA.integrate-request";
            if(!File.Exists(request)||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
            File.Delete(request);
            try { Integrate(); } catch(Exception e) { File.WriteAllText(Report,e.ToString());Debug.LogException(e); }
        }
        static PixelCamera CameraIn(Scene scene)
        {
            foreach(var root in scene.GetRootGameObjects()) { var c=root.GetComponentInChildren<PixelCamera>();if(c)return c; }return null;
        }
        [MenuItem("Tools/TA/Pixel Render/Lighting/Enable and Verify Connectivity AA")]
        public static void Integrate()
        {
            var shader=Shader.Find("Hidden/TA/PixelToonLighting");var material=new Material(shader);
            try { for(int i=0;i<material.passCount;i++)ShaderUtil.CompilePass(material,i,true); }
            finally { UnityEngine.Object.DestroyImmediate(material); }
            Require(!ShaderUtil.ShaderHasError(shader),"AA shader compilation failed");
            int changes=0,edges=0;
            var preview=EditorSceneManager.OpenPreviewScene(ScenePath);
            try
            {
                var pixel=CameraIn(preview);var lighting=pixel.GetComponent<PixelToonLighting>();
                Require(lighting,"Toon lighting missing");lighting.enabled=true;lighting.aaStrength=1;lighting.aaThreshold=.875f;
                lighting.connectivityAA=false;pixel.RenderNow();var before=PixelGIDemo.Read(lighting.DirectRadiance);
                lighting.connectivityAA=true;pixel.RenderNow();var after=PixelGIDemo.Read(lighting.DirectRadiance);
                foreach(var c in PixelGIDemo.Read(lighting.AAConnectivity))
                { Require(float.IsFinite(c.r+c.g)&&c.r>=0&&c.r<=1&&c.g>=c.r&&c.g<=1,"Invalid connectivity ratio");if(c.a>.5f&&c.g<.875f)edges++; }
                for(int i=0;i<before.Length;i++)
                { for(int k=0;k<3;k++) Require(after[i][k]<=before[i][k]+.004f,"AA increased radiance"); if((before[i]-after[i]).maxColorComponent>.005f)changes++; }
                Require(edges>0&&changes>0,"AA did not affect edge lighting");
                lighting.aaStrength=0;pixel.RenderNow();var bypass=PixelGIDemo.Read(lighting.DirectRadiance);
                for(int i=0;i<before.Length;i++)Require(before[i]==bypass[i],"Zero strength differs from disabled AA");
                foreach(var root in preview.GetRootGameObjects())foreach(var light in root.GetComponentsInChildren<Light>())light.enabled=false;
                lighting.aaStrength=1;pixel.RenderNow();var emission=PixelGIDemo.Read(pixel.Radiance);var dark=PixelGIDemo.Read(lighting.DirectRadiance);
                for(int i=0;i<dark.Length;i++)Require(emission[i]==dark[i],"AA modified emission");
                pixel.verticalResolution=90;pixel.RenderNow();Require(lighting.AAConnectivity.height==90,"AA resize failed");
                lighting.enabled=false;Require(!lighting.AAConnectivity&&!lighting.DirectRadiance,"AA resources leaked");
                lighting.enabled=true;pixel.RenderNow();Require(lighting.AAConnectivity,"AA re-enable failed");
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
            var scene=SceneManager.GetSceneByPath(ScenePath);
            if(!scene.IsValid()||!scene.isLoaded)scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
            var camera=CameraIn(scene);var toon=camera.GetComponent<PixelToonLighting>();
            Undo.RecordObject(toon,"Enable connectivity AA");toon.connectivityAA=true;
            Directory.CreateDirectory("Assets/PixelRender/Preview/AA");
            var oldView=camera.view;
            try
            {
                camera.view=PixelCamera.BufferView.Outlined;
                toon.connectivityAA=false;camera.RenderNow();PixelGIDemo.Write(camera.Output,"Assets/PixelRender/Preview/AA/Off.png");
                toon.connectivityAA=true;camera.RenderNow();PixelGIDemo.Write(camera.Output,"Assets/PixelRender/Preview/AA/On.png");
                PixelGIDemo.Write(toon.AAConnectivity,"Assets/PixelRender/Preview/AA/Connectivity.png");
            }
            finally { toon.connectivityAA=true;camera.view=oldView;camera.RenderNow(); }
            EditorUtility.SetDirty(toon);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.Refresh();
            File.WriteAllText(Report,"PASS: three lighting passes compile; bounded connectivity ratios; edge pixels="+edges+"; changed HDR samples="+changes+".\nPASS: AA never brightens; strength zero matches disabled; emission unchanged; resize/release/re-enable.\nPASS: AA enabled in loaded cottage; camera and light settings preserved.\n");
        }
        static void Require(bool value,string message) { if(!value)throw new Exception(message); }
    }
}
