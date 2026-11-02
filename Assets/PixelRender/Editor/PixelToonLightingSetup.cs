using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TA.PixelRender.Editor
{
    [InitializeOnLoad]
    public static class PixelToonLightingSetup
    {
        const string ScenePath="Assets/PixelRender/PixelRenderDemo.unity";
        const string Preview="Assets/PixelRender/Preview/Lighting";
        const string Report="Library/PixelToonLighting-validation.txt";
        static PixelToonLightingSetup() { EditorApplication.update+=Poll; }
        static void Poll()
        {
            const string request="Library/PixelToon.integrate-request";
            if(!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete(request);
            try { Integrate(); } catch(Exception e) { File.WriteAllText(Report,e.ToString()); Debug.LogException(e); }
        }
        [MenuItem("Tools/TA/Pixel Render/Lighting/Integrate and Verify Toon Lighting")]
        public static void Integrate()
        {
            var shader=Shader.Find("Hidden/TA/PixelToonLighting"); Require(shader,"Missing toon lighting shader");
            var material=new Material(shader);
            try { for(int i=0;i<material.passCount;i++) ShaderUtil.CompilePass(material,i,true); }
            finally { UnityEngine.Object.DestroyImmediate(material); }
            Require(!ShaderUtil.ShaderHasError(shader),"Toon shader compile failed");
            var log=new StringBuilder();
            // Isolate destructive validation changes from the user's loaded camera and scene edits.
            var preview=EditorSceneManager.OpenPreviewScene(ScenePath);
            try { Validate(FindPixel(preview),log); }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
            var scene=SceneManager.GetSceneByPath(ScenePath);
            if(!scene.IsValid() || !scene.isLoaded) scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
            var pixel=FindPixel(scene); Require(pixel,"Cottage PixelCamera missing");
            var lighting=pixel.GetComponent<PixelToonLighting>();
            if(!lighting) lighting=Undo.AddComponent<PixelToonLighting>(pixel.gameObject);
            Undo.RecordObject(lighting,"Enable toon lighting"); lighting.enabled=true; lighting.view=PixelToonLighting.LightingView.Composite;
            var gi=pixel.GetComponent<PixelScreenSpaceGI>();
            if(gi) { Undo.RecordObject(gi,"Enable lit GI"); gi.enabled=true; gi.view=PixelScreenSpaceGI.GIView.GIOnly; }
            pixel.RenderNow();
            Directory.CreateDirectory(Preview);
            try
            {
                foreach(var view in new[]{PixelToonLighting.LightingView.Composite,PixelToonLighting.LightingView.DirectOnly,PixelToonLighting.LightingView.GIOnly,PixelToonLighting.LightingView.SourceRadiance})
                { lighting.view=view; pixel.RenderNow(); PixelGIDemo.Write(pixel.Output,Preview+"/"+view+".png"); }
            }
            finally { lighting.view=PixelToonLighting.LightingView.Composite; pixel.RenderNow(); }
            EditorUtility.SetDirty(lighting); if(gi) EditorUtility.SetDirty(gi);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            log.AppendLine("PASS: loaded cottage integrated without changing camera settings; captured lights="+lighting.CapturedLightCount+"; omitted="+lighting.OmittedLightCount);
            File.WriteAllText(Report,log.ToString());
        }
        static PixelCamera FindPixel(Scene scene)
        {
            foreach(var root in scene.GetRootGameObjects()) { var pixel=root.GetComponentInChildren<PixelCamera>(); if(pixel) return pixel; }
            return null;
        }
        static void Validate(PixelCamera pixel,StringBuilder log)
        {
            Require(pixel,"Validation camera missing");
            var lighting=pixel.GetComponent<PixelToonLighting>(); if(!lighting) lighting=pixel.gameObject.AddComponent<PixelToonLighting>();
            lighting.enabled=true;
            var gi=pixel.GetComponent<PixelScreenSpaceGI>(); Require(gi,"Cottage GI missing");
            gi.enabled=true; gi.view=PixelScreenSpaceGI.GIView.GIOnly; gi.externalRadiance=null;
            var lights=new List<Light>();
            foreach(var root in pixel.gameObject.scene.GetRootGameObjects()) foreach(var light in root.GetComponentsInChildren<Light>()) lights.Add(light);
            foreach(var light in lights) light.enabled=false;
            pixel.RenderNow(); var emission=PixelGIDemo.Read(pixel.Radiance); var unlit=PixelGIDemo.Read(lighting.DirectRadiance);
            Require(Difference(emission,unlit)==0,"No lights should produce emission-only source radiance");
            var emissionGI=PixelGIDemo.Read(gi.IndirectDiffuse);
            float directionalChange=0,pointChange=0;
            foreach(var light in lights)
            {
                light.enabled=true; pixel.RenderNow();
                float difference=Difference(unlit,PixelGIDemo.Read(lighting.DirectRadiance));
                if(light.type==LightType.Directional) directionalChange+=difference;
                if(light.type==LightType.Point) pointChange+=difference;
                light.enabled=false;
            }
            Require(directionalChange>.1f && pointChange>.1f,"Directional/point light contributions missing");
            foreach(var light in lights) light.enabled=true;
            pixel.RenderNow(); var direct=PixelGIDemo.Read(lighting.DirectRadiance); var indirect=PixelGIDemo.Read(gi.IndirectDiffuse);
            Require(Difference(emissionGI,indirect)>.01f,"Direct radiance did not feed GI");
            pixel.RenderNow(); Require(Difference(indirect,PixelGIDemo.Read(gi.IndirectDiffuse))==0,"Unexpected GI feedback");
            foreach(var c in direct) Require(float.IsFinite(c.r+c.g+c.b) && c.r>=0 && c.g>=0 && c.b>=0,"Invalid direct radiance");
            foreach(var c in indirect) Require(float.IsFinite(c.r+c.g+c.b) && c.r>=0 && c.g>=0 && c.b>=0,"Invalid indirect radiance");
            foreach(var light in lights) light.cullingMask=0;
            pixel.RenderNow(); Require(Difference(unlit,PixelGIDemo.Read(lighting.DirectRadiance))==0,"Light culling masks ignored");
            // Sole spot light: reversing its cone must remove visible contribution.
            foreach(var light in lights) light.enabled=false;
            var spotObject=new GameObject("Spot fixture"); SceneManager.MoveGameObjectToScene(spotObject,pixel.gameObject.scene);
            var spot=spotObject.AddComponent<Light>(); spot.type=LightType.Spot; spot.range=10; spot.intensity=10; spot.spotAngle=80; spot.innerSpotAngle=60;
            spot.transform.position=new Vector3(0,4,-2); spot.transform.LookAt(new Vector3(0,0,0));
            pixel.RenderNow(); float toward=Difference(unlit,PixelGIDemo.Read(lighting.DirectRadiance));
            spot.transform.rotation=Quaternion.LookRotation(Vector3.up); pixel.RenderNow();
            Require(toward>.1f && Difference(unlit,PixelGIDemo.Read(lighting.DirectRadiance))<.001f,"Spot cone test failed");
            UnityEngine.Object.DestroyImmediate(spotObject);
            pixel.informationHeight=180; pixel.RenderNow(); Require(lighting.DirectRadiance.height==180,"Lighting resize failed");
            lighting.enabled=false; Require(!lighting.DirectRadiance,"Lighting target leaked on disable");
            lighting.enabled=true; pixel.RenderNow(); Require(lighting.DirectRadiance,"Lighting re-enable failed");
            log.AppendLine("PASS: toon shader compiled; zero lights = emission; directional and point lights affect direct radiance; spot cone reverses correctly; culling mask respected.");
            log.AppendLine("PASS: direct + emission feeds GI; deterministic repeat/no feedback; finite nonnegative HDR; resize/disable/re-enable.");
        }
        static float Difference(Color[] a,Color[] b)
        {
            Require(a.Length==b.Length,"Readback dimensions differ"); float result=0;
            for(int i=0;i<a.Length;i++) result+=Mathf.Abs(a[i].r-b[i].r)+Mathf.Abs(a[i].g-b[i].g)+Mathf.Abs(a[i].b-b[i].b);
            return result;
        }
        static void Require(bool value,string message) { if(!value) throw new Exception(message); }
    }
}
