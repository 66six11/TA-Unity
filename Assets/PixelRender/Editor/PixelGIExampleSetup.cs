using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TA.PixelRender.Editor
{
    [InitializeOnLoad]
    public static class PixelGIExampleSetup
    {
        const string Root="Assets/PixelRender";
        const string ScenePath=Root+"/PixelRenderDemo.unity";
        const string Report="Library/PixelGI-cottage-validation.txt";
        static PixelGIExampleSetup() { EditorApplication.update+=Poll; }
        static void Poll()
        {
            const string request="Library/PixelGI.integrate-request";
            if(!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete(request);
            try { Integrate(); } catch(Exception e) { File.WriteAllText(Report,e.ToString()); Debug.LogException(e); }
        }

        [MenuItem("Tools/TA/Pixel Render/GI/Integrate Into Cottage Demo")]
        public static void Integrate()
        {
            var scene=SceneManager.GetSceneByPath(ScenePath);
            if(!scene.IsValid() || !scene.isLoaded) scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
            PixelCamera pixel=null; GameObject setup=null;
            foreach(var root in scene.GetRootGameObjects())
            {
                if(!pixel) pixel=root.GetComponentInChildren<PixelCamera>();
                if(root.name=="GI Example Setup") setup=root;
            }
            if(!pixel) throw new Exception("Cottage PixelCamera missing");
            var lighting=pixel.GetComponent<PixelToonLighting>();
            if(lighting) { Undo.RecordObject(lighting,"Switch to GI-only experiment"); lighting.enabled=false; }
            // Keep a disk backup and preserve the currently loaded scene, including camera edits.
            if(!File.Exists("Library/PixelRenderDemo-before-GI.unity")) File.Copy(ScenePath,"Library/PixelRenderDemo-before-GI.unity");
            Directory.CreateDirectory(Root+"/Materials/GI"); Directory.CreateDirectory(Root+"/Preview/GI"); AssetDatabase.Refresh();
            var frame=Mat("CottageLampFrame",new Color(.18f,.15f,.12f),Color.black);
            var warm=Mat("CottageWarmEmitter",new Color(1,.6f,.15f),new Color(10,3,.3f));
            var cool=Mat("CottageCoolEmitter",new Color(.15f,.5f,1),new Color(.3f,3,10));
            if(!setup)
            {
                setup=new GameObject("GI Example Setup"); SceneManager.MoveGameObjectToScene(setup,scene); Undo.RegisterCreatedObjectUndo(setup,"Add cottage GI lamps");
                Lamp(setup.transform,"Warm courtyard lantern",new Vector3(-2.6f,0,-1.6f),warm,frame,new Color(1,.48f,.12f));
                Lamp(setup.transform,"Cool courtyard lantern",new Vector3(1.5f,0,-1.25f),cool,frame,new Color(.12f,.45f,1));
            }
            var gi=pixel.GetComponent<PixelScreenSpaceGI>();
            if(!gi) gi=Undo.AddComponent<PixelScreenSpaceGI>(pixel.gameObject);
            Undo.RecordObject(gi,"Configure cottage GI");
            gi.enabled=true; gi.view=PixelScreenSpaceGI.GIView.GIOnly; gi.giHeight=90; gi.rayCount=32; gi.raySteps=64;
            gi.externalRadiance=null;
            pixel.RenderNow(); var baseline=PixelGIDemo.Read(gi.IndirectDiffuse);
            float energy=0; int nonzero=0;
            foreach(var c in baseline)
            {
                if(!float.IsFinite(c.r+c.g+c.b) || c.r<0 || c.g<0 || c.b<0) throw new Exception("Invalid cottage GI");
                energy+=c.r+c.g+c.b; if(c.r+c.g+c.b>.001f) nonzero++;
            }
            if(energy<.1f) throw new Exception("No cottage bounce detected");
            var lights=new List<Light>(); var enabledStates=new List<bool>();
            foreach(var root in scene.GetRootGameObjects()) foreach(var light in root.GetComponentsInChildren<Light>(true))
            { lights.Add(light); enabledStates.Add(light.enabled); }
            try
            {
                foreach(var light in lights) light.enabled=false;
                pixel.RenderNow(); var disabled=PixelGIDemo.Read(gi.IndirectDiffuse);
                for(int i=0;i<baseline.Length;i++) if(baseline[i]!=disabled[i]) throw new Exception("GI depends on Unity Light components");
            }
            finally { for(int i=0;i<lights.Count;i++) lights[i].enabled=enabledStates[i]; }
            try
            {
                gi.view=PixelScreenSpaceGI.GIView.SourceRadiance; pixel.RenderNow(); PixelGIDemo.Write(pixel.Output,Root+"/Preview/GI/CottageSourceRadiance.png");
                gi.view=PixelScreenSpaceGI.GIView.Off;
                var view=pixel.view;
                try { pixel.view=PixelCamera.BufferView.Albedo; pixel.RenderNow(); PixelGIDemo.Write(pixel.Output,Root+"/Preview/GI/CottageContext.png"); }
                finally { pixel.view=view; }
            }
            finally { gi.view=PixelScreenSpaceGI.GIView.GIOnly; pixel.RenderNow(); }
            PixelGIDemo.Write(pixel.Output,Root+"/Preview/GI/CottageGI.png");
            EditorUtility.SetDirty(gi); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            File.WriteAllText(Report,"PASS: cottage scene integrated; two emissive lanterns with enabled Point Light components.\n"+
                "PASS: all "+lights.Count+" scene Lights disabled => identical GI pixels; no direct lighting.\n"+
                "PASS: finite nonnegative GI; energy="+energy+"; nonzero pixels="+nonzero+".\n"+
                "Information="+pixel.Albedo.width+"x"+pixel.Albedo.height+"; GI="+gi.IndirectDiffuse.width+"x"+gi.IndirectDiffuse.height+"; output="+pixel.Output.width+"x"+pixel.Output.height+".\n");
        }
        static Material Mat(string name,Color color,Color emission)
        {
            string path=Root+"/Materials/GI/"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material) { material=new Material(Shader.Find("TA/PixelRender/Toon")); AssetDatabase.CreateAsset(material,path); }
            material.SetColor("_BaseColor",color); material.SetColor("_EmissionColor",emission); EditorUtility.SetDirty(material); return material;
        }
        static void Lamp(Transform parent,string name,Vector3 position,Material emitter,Material frame,Color color)
        {
            var root=new GameObject(name); root.transform.SetParent(parent,false); root.transform.localPosition=position;
            Cube(root.transform,"Post",new Vector3(0,.42f,0),new Vector3(.12f,.84f,.12f),frame);
            Cube(root.transform,"Emissive glass",new Vector3(0,1,0),new Vector3(.6f,.6f,.6f),emitter);
            Cube(root.transform,"Cap",new Vector3(0,1.34f,0),new Vector3(.72f,.08f,.72f),frame);
            var lightObject=new GameObject("Point Light (reserved for future direct lighting)"); lightObject.transform.SetParent(root.transform,false); lightObject.transform.localPosition=new Vector3(0,1,0);
            var light=lightObject.AddComponent<Light>(); light.type=LightType.Point; light.color=color; light.intensity=3; light.range=5; light.shadows=LightShadows.None; light.cullingMask=1<<29;
        }
        static void Cube(Transform parent,string name,Vector3 position,Vector3 scale,Material material)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube); go.name=name; go.layer=29; go.transform.SetParent(parent,false);
            go.transform.localPosition=position; go.transform.localScale=scale; go.GetComponent<Renderer>().sharedMaterial=material;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        }
    }
}
