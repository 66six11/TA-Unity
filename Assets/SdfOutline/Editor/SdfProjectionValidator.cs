using System;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace TA.SdfOutline.Editor
{
    [InitializeOnLoad]
    public static class SdfProjectionValidator
    {
        static SdfProjectionValidator(){EditorApplication.update+=Poll;}
        static void Poll()
        {
            if(EditorApplication.isCompiling||EditorApplication.isUpdating||!File.Exists("Library/SdfProjection.request"))return;
            File.Delete("Library/SdfProjection.request");
            try{Run();}catch(Exception e){File.WriteAllText("Library/SdfProjection.txt",e.ToString());Debug.LogException(e);}
        }
        static void Run()
        {
            var cs=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/SdfOutline/Shaders/SdfOutline.compute");
            var mask=new Texture2D(33,21,TextureFormat.RFloat,false,true);
            var first=new RenderTexture(5,3,0,RenderTextureFormat.RGFloat){enableRandomWrite=true};first.Create();
            var last=new RenderTexture(1,1,0,RenderTextureFormat.RGFloat){enableRandomWrite=true};last.Create();
            var read=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);var previous=RenderTexture.active;
            try
            {
                for(int test=0;test<3;test++)
                {
                    var pixels=new Color[33*21];
                    if(test<2)for(int y=5;y<15;y++)for(int x=3;x<13;x++)pixels[y*33+x]=Color.white;
                    mask.SetPixels(pixels);mask.Apply();
                    int k=cs.FindKernel("ProjectionBounds");cs.SetVector("_BoundsSize",new Vector4(33,21,0,0));
                    cs.SetVector("_ProjectionAxis",test==1?new Vector4(0,1,-1,0):new Vector4(1,0,0,1));
                    cs.SetTexture(k,"_Mask",mask);cs.SetTexture(k,"_BoundsOutput",first);cs.Dispatch(k,5,3,1);
                    k=cs.FindKernel("ReduceBounds");cs.SetVector("_BoundsSize",new Vector4(5,3,0,0));
                    cs.SetTexture(k,"_BoundsInput",first);cs.SetTexture(k,"_BoundsOutput",last);cs.Dispatch(k,1,1,1);
                    RenderTexture.active=last;read.ReadPixels(new Rect(0,0,1,1),0,0);read.Apply();var value=read.GetPixel(0,0);
                    if(test==0&&(Mathf.Abs(value.r-3)>0.001f||Mathf.Abs(value.g-12)>0.001f))throw new Exception("Horizontal extent mismatch: "+value);
                    if(test==1&&(Mathf.Abs(value.r-5)>0.001f||Mathf.Abs(value.g-14)>0.001f))throw new Exception("Vertical extent mismatch: "+value);
                    if(test==2&&value.g>=value.r)throw new Exception("Empty mask must have invalid bounds.");
                }
                File.WriteAllText("Library/SdfProjection.txt","PASS: 33x21 partial groups, horizontal extent [3,12], vertical extent [5,14], empty invalid extent.");
            }
            finally{RenderTexture.active=previous;first.Release();last.Release();UnityEngine.Object.DestroyImmediate(first);UnityEngine.Object.DestroyImmediate(last);UnityEngine.Object.DestroyImmediate(read);UnityEngine.Object.DestroyImmediate(mask);}
        }
    }
}
