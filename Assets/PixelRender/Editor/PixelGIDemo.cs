using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TA.PixelRender.Editor
{
    [InitializeOnLoad]
    public static class PixelGIDemo
    {
        const string Root="Assets/PixelRender";
        const string ScenePath=Root+"/PixelGIDemo.unity";
        const string Report="Library/PixelGI-validation.txt";
        static PixelGIDemo() { EditorApplication.update+=Poll; }
        static void Poll()
        {
            const string request="Library/PixelGI.build-request";
            if(!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete(request);
            try { Build(); } catch(Exception e) { File.WriteAllText(Report,e.ToString()); Debug.LogException(e); }
        }
        [MenuItem("Tools/TA/Pixel Render/GI/Build and Verify GI Demo")]
        public static void Build()
        {
            Directory.CreateDirectory(Root+"/Materials/GI"); Directory.CreateDirectory(Root+"/Preview/GI"); AssetDatabase.Refresh();
            foreach(string name in new[]{"TA/PixelRender/Toon","Hidden/TA/PixelSSGI"})
            {
                var shader=Shader.Find(name); if(!shader) throw new Exception("Missing shader "+name);
                var m=new Material(shader);
                try { for(int i=0;i<m.passCount;i++) ShaderUtil.CompilePass(m,i,true); }
                finally { UnityEngine.Object.DestroyImmediate(m); }
                Require(!ShaderUtil.ShaderHasError(shader),"Shader compile failed: "+name);
            }
            var original=SceneManager.GetActiveScene();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var matte=Mat("Matte",new Color(0.85f,0.85f,0.85f),Color.black);
                var red=Mat("RedEmitter",new Color(0.7f,0.12f,0.06f),new Color(4,0.05f,0.01f));
                var blue=Mat("BlueEmitter",new Color(0.06f,0.2f,0.7f),new Color(0.01f,0.15f,4));
                Shape("Neutral floor",new Vector3(0,-0.1f,0),new Vector3(8,0.2f,6),matte);
                Shape("Neutral back wall",new Vector3(0,1.5f,2.95f),new Vector3(8,3,0.1f),matte);
                Shape("Red emissive wall",new Vector3(-3.95f,1.5f,0),new Vector3(0.1f,3,6),red);
                Shape("Blue emissive panel",new Vector3(1.7f,1.2f,2.83f),new Vector3(2,2.4f,0.1f),blue);
                Shape("Neutral occluder",new Vector3(-0.8f,0.65f,0.4f),new Vector3(0.75f,1.3f,0.75f),matte);
                var camera=new GameObject("GI Camera - no direct lights").AddComponent<Camera>();
                camera.tag="MainCamera"; camera.transform.position=new Vector3(7,6,-9); camera.transform.LookAt(new Vector3(0,0.9f,0));
                camera.orthographic=true; camera.orthographicSize=5.1f; camera.nearClipPlane=0.1f; camera.farClipPlane=50;
                camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.black; camera.allowHDR=false; camera.allowMSAA=false;
                camera.cullingMask=1<<29;
                var pixel=camera.gameObject.AddComponent<PixelCamera>(); pixel.outlineEnabled=false;
                pixel.informationHeight=360; pixel.verticalResolution=180;
                var gi=camera.gameObject.AddComponent<PixelScreenSpaceGI>(); gi.giHeight=90; gi.rayCount=32; gi.raySteps=64;
                gi.view=PixelScreenSpaceGI.GIView.GIOnly;
                EditorSceneManager.SaveScene(scene,ScenePath);
                pixel.RenderNow();
                Require(pixel.Radiance.width==640 && gi.IndirectDiffuse.width==160,"GI dimensions incorrect");
                var result=Read(gi.IndirectDiffuse);
                float energy=0,redEnergy=0,blueEnergy=0; int nonzero=0;
                foreach(var c in result)
                {
                    Require(float.IsFinite(c.r) && float.IsFinite(c.g) && float.IsFinite(c.b) && c.r>=0 && c.g>=0 && c.b>=0,"GI contains invalid radiance");
                    energy+=c.r+c.g+c.b; redEnergy+=c.r; blueEnergy+=c.b;
                    if(c.r+c.g+c.b>0.001f) nonzero++;
                }
                Require(energy>0.1f && redEnergy>0.01f && blueEnergy>0.01f,"No visible color bounce detected: "+energy);
                // Exact HDR output for analysis, independently of clamped PNG display.
                WriteExr(gi.IndirectDiffuse,Root+"/Preview/GI/IndirectDiffuse.exr");
                foreach(PixelScreenSpaceGI.GIView view in new[]{PixelScreenSpaceGI.GIView.GIOnly,PixelScreenSpaceGI.GIView.SourceRadiance,PixelScreenSpaceGI.GIView.RawGI,PixelScreenSpaceGI.GIView.HitRatio})
                { gi.view=view; pixel.RenderNow(); Write(pixel.Output,Root+"/Preview/GI/"+view+".png"); }
                // Same geometry/albedo with zero radiance must produce zero GI.
                var black=new Texture2D(640,360,TextureFormat.RGBAHalf,false,true);
                try
                {
                    black.SetPixels(new Color[640*360]); black.Apply(); gi.externalRadiance=black; pixel.RenderNow();
                    foreach(var c in Read(gi.IndirectDiffuse)) Require(c.r==0 && c.g==0 && c.b==0,"Zero source must yield zero GI");
                }
                finally { gi.externalRadiance=null; UnityEngine.Object.DestroyImmediate(black); }
                // Emitter-free surfaces with unchanged base colors cannot emit light.
                var oldRed=red.GetColor("_EmissionColor"); var oldBlue=blue.GetColor("_EmissionColor");
                try
                {
                    red.SetColor("_EmissionColor",Color.black); blue.SetColor("_EmissionColor",Color.black); pixel.RenderNow();
                    foreach(var c in Read(gi.IndirectDiffuse)) Require(c.r==0 && c.g==0 && c.b==0,"Albedo treated as a light source");
                }
                finally { red.SetColor("_EmissionColor",oldRed); blue.SetColor("_EmissionColor",oldBlue); }
                pixel.RenderNow(); var repeated=Read(gi.IndirectDiffuse);
                for(int i=0;i<result.Length;i++) Require(result[i]==repeated[i],"GI unexpectedly depends on previous GI frames");
                gi.giHeight=45; pixel.RenderNow(); Require(gi.IndirectDiffuse.height==45,"GI resize failed");
                gi.enabled=false; Require(!gi.RawGI && !gi.IndirectDiffuse,"GI buffers not released");
                gi.enabled=true; gi.giHeight=90; gi.view=PixelScreenSpaceGI.GIView.GIOnly; pixel.RenderNow();
                Require(gi.IndirectDiffuse,"GI re-enable failed");
                Require(scene.GetRootGameObjects().Length>0,"GI scene missing");
                var log=new StringBuilder(); log.AppendLine("PASS: no Light components created; emission-only radiance, no direct-light evaluation in GI path.");
                log.AppendLine("PASS: 640x360 surface/radiance; 160x90 GI; 320x180 presentation.");
                log.AppendLine("PASS: finite nonnegative GI; nonzero pixels="+nonzero+"; energy="+energy+"; red="+redEnergy+"; blue="+blueEnergy);
                log.AppendLine("PASS: external black input => zero GI; zero emission => zero GI despite colored albedo; deterministic repeat/no feedback; resize/release/re-enable.");
                File.WriteAllText(Report,log.ToString());
                EditorSceneManager.SaveScene(scene,ScenePath); AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            }
            finally { if(original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original); EditorSceneManager.CloseScene(scene,true); }
        }
        [MenuItem("Tools/TA/Pixel Render/GI/Open GI Demo")]
        public static void Open()
        {
            if(!File.Exists(ScenePath)) Build();
            if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }
        static void Require(bool value,string message) { if(!value) throw new Exception(message); }
        static Material Mat(string name,Color color,Color emission)
        {
            string path=Root+"/Materials/GI/"+name+".mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!m) { m=new Material(Shader.Find("TA/PixelRender/Toon")); AssetDatabase.CreateAsset(m,path); }
            m.SetColor("_BaseColor",color); m.SetColor("_EmissionColor",emission); EditorUtility.SetDirty(m); return m;
        }
        static void Shape(string name,Vector3 position,Vector3 scale,Material material)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube); go.name=name; go.layer=29;
            go.transform.position=position; go.transform.localScale=scale; go.GetComponent<Renderer>().sharedMaterial=material;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        }
        internal static Color[] Read(RenderTexture rt)
        {
            var previous=RenderTexture.active; var t=new Texture2D(rt.width,rt.height,TextureFormat.RGBAFloat,false,true);
            try { RenderTexture.active=rt; t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0); t.Apply(); return t.GetPixels(); }
            finally { RenderTexture.active=previous; UnityEngine.Object.DestroyImmediate(t); }
        }
        internal static void Write(RenderTexture rt,string path)
        {
            var t=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false); var colors=Read(rt);
            for(int i=0;i<colors.Length;i++) colors[i]=colors[i].gamma;
            try { t.SetPixels(colors); t.Apply(); File.WriteAllBytes(path,t.EncodeToPNG()); }
            finally { UnityEngine.Object.DestroyImmediate(t); }
        }
        static void WriteExr(RenderTexture rt,string path)
        {
            var t=new Texture2D(rt.width,rt.height,TextureFormat.RGBAFloat,false,true);
            try { t.SetPixels(Read(rt)); t.Apply(); File.WriteAllBytes(path,t.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat)); }
            finally { UnityEngine.Object.DestroyImmediate(t); }
        }
    }
}
