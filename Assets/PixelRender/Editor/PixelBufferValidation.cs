using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TA.PixelRender.Editor
{
    [InitializeOnLoad]
    public static class PixelBufferValidation
    {
        const string Report="Library/PixelBuffers-validation.txt";
        const string Preview="Assets/PixelRender/Preview/Buffers";
        static PixelBufferValidation() { EditorApplication.update+=Poll; }
        static void Poll()
        {
            const string request="Library/PixelBuffers.validate-request";
            if(!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete(request);
            try { Validate(); } catch(Exception e) { File.WriteAllText(Report,e.ToString()); Debug.LogException(e); }
        }
        [MenuItem("Tools/TA/Pixel Render/Validate Surface Buffers")]
        public static void Validate()
        {
            var scene=EditorSceneManager.OpenPreviewScene("Assets/PixelRender/PixelRenderDemo.unity");
            try
            {
                PixelCamera pixel=null;
                foreach(var root in scene.GetRootGameObjects()) { pixel=root.GetComponentInChildren<PixelCamera>(); if(pixel) break; }
                if(!pixel) throw new Exception("PixelCamera missing");
                var gi=pixel.GetComponent<PixelScreenSpaceGI>();
                if(gi) gi.enabled=false; // Validate surface/outline output independently of GI presentation.
                var lighting=pixel.GetComponent<PixelToonLighting>();
                if(lighting) lighting.enabled=false;
                var camera=pixel.GetComponent<Camera>();
                foreach(string name in new[]{"TA/PixelRender/Toon","Hidden/TA/PixelBufferPreview","Hidden/TA/PixelConnectivityOutline"})
                {
                    var shader=Shader.Find(name);
                    if(!shader) throw new Exception("Missing shader "+name);
                    var material=new Material(shader);
                    try { for(int p=0;p<material.passCount;p++) ShaderUtil.CompilePass(material,p,true); }
                    finally { UnityEngine.Object.DestroyImmediate(material); }
                    if(ShaderUtil.ShaderHasError(shader)) throw new Exception("Shader compilation failed: "+name);
                }
                pixel.informationHeight=360; pixel.verticalResolution=180;
                pixel.RenderNow();
                var albedo=Read(pixel.Albedo); var normals=Read(pixel.WorldNormal); var depths=Read(pixel.LinearDepth);
                int covered=0; float min=float.MaxValue,max=0;
                for(int i=0;i<albedo.Length;i++)
                {
                    if(albedo[i].a<0.5f)
                    {
                        Require(normals[i].a==0 && depths[i].a==0,"Background coverage must be zero"); continue;
                    }
                    covered++;
                    var n=new Vector3(normals[i].r,normals[i].g,normals[i].b);
                    Require(Mathf.Abs(n.magnitude-1)<0.003f,"Normal must have unit length");
                    float d=depths[i].r;
                    Require(!float.IsNaN(d) && d>=camera.nearClipPlane && d<=camera.farClipPlane,"Linear depth outside camera range");
                    Require(normals[i].a>0.99f && depths[i].a>0.99f,"MRT coverage disagreement");
                    min=Mathf.Min(min,d); max=Mathf.Max(max,d);
                }
                Require(covered>1000 && covered<albedo.Length,"Geometry/background coverage missing: "+covered+"; unsupported="+pixel.UnsupportedMaterials);
                Require(pixel.UnsupportedMaterials==0,"Demo contains unsupported materials");
                // No lighting variables participate in this pass: rotate/disable the sun
                // and verify every raw albedo sample remains unchanged.
                foreach(var root in scene.GetRootGameObjects()) foreach(var light in root.GetComponentsInChildren<Light>())
                { light.enabled=false; light.transform.rotation=Quaternion.Euler(0,180,0); }
                pixel.RenderNow(); var unlit=Read(pixel.Albedo);
                for(int i=0;i<albedo.Length;i++) Require(albedo[i]==unlit[i],"Albedo depends on lighting");
                Directory.CreateDirectory(Preview);
                foreach(PixelCamera.BufferView view in Enum.GetValues(typeof(PixelCamera.BufferView)))
                { pixel.view=view; pixel.RenderNow(); Write(pixel.Output,Preview+"/"+view+".png"); }
                string connectivityReport=ValidateConnectivity(pixel);
                // Raw data remains linear / signed; EXR is intended for downstream processing.
                WriteExr(pixel.Albedo,"Albedo"); WriteExr(pixel.WorldNormal,"WorldNormal"); WriteExr(pixel.LinearDepth,"LinearDepth");
                var old=pixel.Albedo;
                pixel.informationHeight=180; pixel.verticalResolution=90; pixel.RenderNow();
                Require(pixel.Albedo.width==320 && pixel.Output.width==160 && pixel.Connectivity.width==160,"Resize dimensions incorrect");
                Require(!old || !old.IsCreated(),"Old render target leaked");
                pixel.enabled=false;
                Require(!pixel.Albedo && !pixel.Output && !pixel.Connectivity && !pixel.Radiance,"Disable did not release buffers");
                pixel.enabled=true; pixel.RenderNow();
                Require(pixel.Albedo && pixel.Output,"Re-enable failed");
                var log=new StringBuilder();
                log.AppendLine("PASS: three shaders compile; 640x360 MRT; 320x180 presentation.");
                log.AppendLine("Covered pixels: "+covered+"; eye depth range: "+min+" .. "+max);
                log.AppendLine("PASS: unit normals, matching coverage, finite depth, sun-independent albedo.");
                log.AppendLine("PASS: resize, release, re-enable. Raw EXR + debug PNGs exported.");
                log.AppendLine(connectivityReport);
                File.WriteAllText(Report,log.ToString()); AssetDatabase.Refresh();
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
        static string ValidateConnectivity(PixelCamera pixel)
        {
            pixel.RenderNow();
            foreach(var c in Read(pixel.Connectivity))
                for(int k=0;k<4;k++) Require(c[k]>=0 && c[k]<=3 && c[k]==Mathf.Round(c[k]),"Invalid connectivity flags");
            pixel.view=PixelCamera.BufferView.OutlineMask; pixel.RenderNow(); int lines=0;
            foreach(var c in Read(pixel.Output)) { Require(c.r==0 || c.r==1,"Outline mask is not binary"); if(c.r>0.5f) lines++; }
            Require(lines>10,"No outline generated");
            pixel.view=PixelCamera.BufferView.Outlined; pixel.outlineEnabled=false; pixel.RenderNow(); var bypass=Read(pixel.Output);
            pixel.view=PixelCamera.BufferView.Albedo; pixel.RenderNow(); var raw=Read(pixel.Output);
            for(int i=0;i<raw.Length;i++) Require(Mathf.Abs(raw[i].r-bypass[i].r)<0.005f && Mathf.Abs(raw[i].g-bypass[i].g)<0.005f && Mathf.Abs(raw[i].b-bypass[i].b)<0.005f,"Bypass differs from albedo");
            pixel.outlineEnabled=true; var layers=pixel.captureLayers; pixel.captureLayers=0;
            pixel.view=PixelCamera.BufferView.OutlineMask; pixel.RenderNow();
            foreach(var c in Read(pixel.Output)) Require(c.r==0,"Empty scene outlined");
            pixel.captureLayers=layers;
            ValidateConnectivityFixtures();
            return "PASS: integer four-way flags; binary outline; bypass; empty scene; planar connectivity; far-side outline fixture. Outline pixels="+lines;
        }
        static void ValidateConnectivityFixtures()
        {
            const int size=32;
            var depth=new Texture2D(size,size,TextureFormat.RGBAFloat,false,true) { filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp };
            var normal=new Texture2D(size,size,TextureFormat.RGBAFloat,false,true) { filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp };
            var albedo=new Texture2D(size,size,TextureFormat.RGBAFloat,false,true) { filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp };
            var rt=RenderTexture.GetTemporary(size,size,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);
            rt.filterMode=FilterMode.Point;
            var output=RenderTexture.GetTemporary(size,size,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            var material=new Material(Shader.Find("Hidden/TA/PixelConnectivityOutline"));
            try
            {
                var ds=new Color[size*size]; var ns=new Color[size*size]; var colors=new Color[size*size];
                var n=new Vector3(0.8f,0,1).normalized;
                for(int y=0;y<size;y++) for(int x=0;x<size;x++)
                {
                    int i=y*size+x; ds[i]=new Color(10+0.8f*((x+0.5f)*8f/size-4),0,0,1);
                    ns[i]=new Color(n.x,n.y,n.z,1); colors[i]=Color.white;
                }
                depth.SetPixels(ds); depth.Apply(); normal.SetPixels(ns); normal.Apply(); albedo.SetPixels(colors); albedo.Apply();
                material.SetTexture("_DepthBuffer",depth); material.SetTexture("_NormalBuffer",normal); material.SetTexture("_AlbedoBuffer",albedo); material.SetTexture("_Connectivity",rt);
                material.SetVector("_InformationSize",new Vector4(size,size,1f/size,1f/size));
                material.SetVector("_PixelStep",new Vector4(1f/size,1f/size,0,0)); material.SetInt("_ConnectivitySteps",1);
                material.SetMatrix("_CaptureInverseProjection",GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(-4,4,-4,4,0.1f,30),true).inverse);
                material.SetMatrix("_CaptureWorldToView",Matrix4x4.identity); material.SetFloat("_Threshold",0.05f);
                Graphics.Blit(depth,rt,material,0);
                foreach(var c in Read(rt)) for(int k=0;k<4;k++) Require(((int)c[k]&1)==1,"Tilted plane disconnected");
                for(int y=0;y<size;y++) for(int x=size/2;x<size;x++) { int i=y*size+x; ds[i]=ns[i]=colors[i]=Color.clear; }
                depth.SetPixels(ds); depth.Apply(); normal.SetPixels(ns); normal.Apply(); albedo.SetPixels(colors); albedo.Apply();
                Graphics.Blit(depth,rt,material,0); var flags=Read(rt);
                material.SetFloat("_View",5); Graphics.Blit(albedo,output,material,1); var mask=Read(output);
                for(int y=1;y<size-1;y++)
                {
                    Require(flags[y*size+size/2-1].r==2,"Near-side flags wrong");
                    Require(flags[y*size+size/2].g==0,"Far-side flags wrong");
                    Require(mask[y*size+size/2-1].r==0 && mask[y*size+size/2].r==1,"Outline must be on far side only");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(depth); UnityEngine.Object.DestroyImmediate(normal); UnityEngine.Object.DestroyImmediate(albedo); UnityEngine.Object.DestroyImmediate(material);
                RenderTexture.ReleaseTemporary(rt); RenderTexture.ReleaseTemporary(output);
            }
        }
        static void Require(bool condition,string message) { if(!condition) throw new Exception(message); }
        static Color[] Read(RenderTexture rt)
        {
            var previous=RenderTexture.active; var image=new Texture2D(rt.width,rt.height,TextureFormat.RGBAFloat,false,true);
            try { RenderTexture.active=rt; image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0); image.Apply(); return image.GetPixels(); }
            finally { RenderTexture.active=previous; UnityEngine.Object.DestroyImmediate(image); }
        }
        static void Write(RenderTexture rt,string path)
        {
            var colors=Read(rt);
            for(int i=0;i<colors.Length;i++) colors[i]=colors[i].gamma;
            var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
            try { image.SetPixels(colors); image.Apply(); File.WriteAllBytes(path,image.EncodeToPNG()); }
            finally { UnityEngine.Object.DestroyImmediate(image); }
        }
        static void WriteExr(RenderTexture rt,string name)
        {
            var image=new Texture2D(rt.width,rt.height,TextureFormat.RGBAFloat,false,true);
            try { image.SetPixels(Read(rt)); image.Apply(); File.WriteAllBytes(Preview+"/"+name+".exr",image.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat)); }
            finally { UnityEngine.Object.DestroyImmediate(image); }
        }
    }
}
