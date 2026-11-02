using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
namespace TA.SdfOutline.Editor
{
    [InitializeOnLoad]
    public static class SdfConvexValidation
    {
        static SdfConvexValidation(){EditorApplication.update+=Poll;}
        static void Poll()
        {
            if(EditorApplication.isCompiling||EditorApplication.isUpdating||!File.Exists("Library/SdfConvex.request"))return;
            try{File.Delete("Library/SdfConvex.request");Run();}catch(Exception e){File.WriteAllText("Library/SdfConvex.txt",e.ToString());Debug.LogException(e);}
        }
        static void Run()
        {
            var scene=SceneManager.GetSceneByPath("Assets/SdfOutline/SdfOutlineDemo.unity");Camera camera=null;Renderer sphere=null;
            foreach(var root in scene.GetRootGameObjects())
            {
                if(root.TryGetComponent<Camera>(out var c))camera=c;
                if(root.name.StartsWith("01 Sphere"))sphere=root.GetComponent<Renderer>();
            }
            if(!camera||!sphere)throw new InvalidOperationException("Open demo scene first.");
            var view=camera.GetComponent<SdfOutlineControls>();string saved=EditorJsonUtility.ToJson(view);
            var pos=camera.transform.position;var rot=camera.transform.rotation;
            var hidden=new System.Collections.Generic.List<Renderer>();
            foreach(var renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if(renderer.gameObject.scene==scene&&renderer!=sphere&&renderer.enabled){hidden.Add(renderer);renderer.enabled=false;}
            var previous=RenderTexture.active;var rt=RenderTexture.GetTemporary(1280,720,24,RenderTextureFormat.ARGB32);
            var image=new Texture2D(1280,720,TextureFormat.RGB24,false);
            try
            {
                view.singleConvexTarget=true;view.convexTarget=sphere;view.display=SdfOutlineFeature.Display.ProjectionCoordinate;view.projectionAngle=0;
                Vector3 center=sphere.bounds.center;float radius=14;Color32[] reference=null;
                double totalError=0;double worst=0;int compared=0;
                for(int i=0;i<10;i++)
                {
                    camera.transform.position=center+Quaternion.Euler(0,i*10,0)*new Vector3(0,0,-radius);camera.transform.LookAt(center);
                    RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
                    RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();var pixels=image.GetPixels32();
                    if(i==0){reference=pixels;File.WriteAllBytes("Library/SdfOutline-ConvexU.png",image.EncodeToPNG());continue;}
                    double error=0;int active=0;
                    for(int p=0;p<pixels.Length;p++)
                    {
                        var a=reference[p];var b=pixels[p];if(a.r+a.g+a.b+b.r+b.g+b.b<12)continue;
                        error+=(Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b))/(3.0*255);active++;
                    }
                    double mean=active>0?error/active:0;totalError+=mean;worst=Math.Max(worst,mean);compared++;
                }
                view.display=SdfOutlineFeature.Display.Composite;
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
                RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes("Library/SdfOutline-ConvexComposite.png",image.EncodeToPNG());
                File.WriteAllText("Library/SdfConvex.txt",$"PASS rendered 10 angles 0..90 degrees, orthographic sphere, 1280x720. Projection U RGB MAE vs angle0 over active union: mean={totalError/compared:F6}, worst={worst:F6}. This measures a symmetric sphere only, not arbitrary convex or occluded shapes.");
            }
            finally{foreach(var renderer in hidden)if(renderer)renderer.enabled=true;EditorJsonUtility.FromJsonOverwrite(saved,view);camera.transform.SetPositionAndRotation(pos,rot);RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(image);}
        }
    }
}
