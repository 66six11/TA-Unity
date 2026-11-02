using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace TA.VolumeSmoke3D.Editor
{
    // Opt-in graphics verification. Creates a separate scene and restores existing lights.
    [InitializeOnLoad]
    public static class VolumeSmokeLightingValidator
    {
        const string Root="Assets/VolumeSmoke3D";
        const string Request="Library/VolumeSmokeLighting.request";
        static VolumeSmokeLightingValidator()=>EditorApplication.update+=Poll;
        static void Poll()
        {
            if(!File.Exists(Request)||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            if(EditorApplication.isPlayingOrWillChangePlaymode){if(EditorApplication.isPlaying)EditorApplication.ExitPlaymode();return;}
            File.Delete(Request);Validate();
        }
        [MenuItem("Tools/TA/Volume Smoke 3D/Verify Scene Lighting and Export Views")]
        public static void Validate()
        {
            var report=new StringBuilder($"{DateTime.Now:O}\n{SystemInfo.graphicsDeviceName} / {SystemInfo.graphicsDeviceType}\n");
            try
            {
                var shader=AssetDatabase.LoadAssetAtPath<Shader>(Root+"/Shaders/VolumeSmoke.shader");
                var material=new Material(shader);
                try{ShaderUtil.CompilePass(material,0,true);}finally{UnityEngine.Object.DestroyImmediate(material);}
                Check(!ShaderUtil.ShaderHasError(shader),"Raymarch shader compiles");
                VerifyLightingKernel(report);
                ExportViews(report);
                File.WriteAllText("Library/VolumeSmokeLighting-validation.txt",report.ToString());
                Debug.Log(report.ToString());
            }
            catch(Exception e){File.WriteAllText("Library/VolumeSmokeLighting-validation.txt",report+"\nFAIL: "+e);Debug.LogException(e);}
        }
        static void VerifyLightingKernel(StringBuilder report)
        {
            const int size=16;
            var cs=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(Root+"/Shaders/VolumeSmoke.compute"));
            var density=new Texture3D(size,size,size,TextureFormat.RFloat,false){filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            var values=new float[size*size*size];
            var output=new RenderTexture(size,size,0){dimension=TextureDimension.Tex3D,volumeDepth=size,graphicsFormat=GraphicsFormat.R32G32B32A32_SFloat,enableRandomWrite=true};output.Create();
            try
            {
                int k=cs.FindKernel("SceneLighting");
                cs.SetInt("_Size",size);cs.SetInt("_VolumeLightCount",1);cs.SetInt("_VolumeLightSteps",32);
                cs.SetVector("_LightingBoundsMin",new Vector4(-3,-3,-3,0));cs.SetVector("_LightingBoundsSize",new Vector4(6,6,6,0));
                cs.SetVector("_LightingCamera",new Vector4(0,0,-8,0));cs.SetFloat("_LightingExtinction",1.8f);cs.SetFloat("_LightingAnisotropy",0);
                cs.SetTexture(k,"_DensityRead",density);cs.SetTexture(k,"_SceneLightOut",output);
                var positions=new Vector4[8];var colors=new Vector4[8];var directions=new Vector4[8];var cones=new Vector4[8];
                colors[0]=new Vector4(1,0.5f,0.25f,1f/400);directions[0]=new Vector4(0,0,1,0);cones[0]=new Vector4(0.8f,10,0,0);
                float[] Sample()
                {
                    density.SetPixelData(values,0);density.Apply();
                    cs.SetVectorArray("_VolumeLightPosition",positions);cs.SetVectorArray("_VolumeLightColor",colors);
                    cs.SetVectorArray("_VolumeLightDirection",directions);cs.SetVectorArray("_VolumeLightCone",cones);
                    cs.Dispatch(k,4,4,4);
                    var request=AsyncGPUReadback.Request(output,0,TextureFormat.RGBAFloat);request.WaitForCompletion();Check(!request.hasError,"Lighting readback");
                    var result=new float[4];var layer=request.GetData<float>(8);int offset=4*(8+size*8);
                    for(int i=0;i<4;i++){result[i]=layer[offset+i];Check(float.IsFinite(result[i])&&result[i]>=0,"Finite nonnegative lighting");}return result;
                }
                positions[0]=new Vector4(0.1875f,0.1875f,-1.8125f,1);float near=Sample()[0];
                positions[0].z=-3.8125f;float far=Sample()[0];
                Check(near>far*3.8f,"Point light inverse-square attenuation");
                report.AppendLine($"PASS: point light distance 2 -> 4: {near:F6} -> {far:F6}.");
                positions[0].w=2;float inside=Sample()[0];directions[0].z=-1;float outside=Sample()[0];
                Check(inside>0&&outside<1e-6f,"Spot cone rejects outside directions");
                report.AppendLine($"PASS: spot cone facing {inside:F6}, turned away {outside:F6}.");
                positions[0].w=0;directions[0].z=1;float clear=Sample()[0];
                colors[0]=new Vector4(2,1,0.5f,1f/400);float doubled=Sample()[0];Check(Mathf.Abs(doubled/clear-2)<0.01f,"Light intensity linearity");
                for(int i=0;i<values.Length;i++)values[i]=0.2f;
                float[] smoky=Sample();float expected=Mathf.Exp(-0.2f*1.8f*3.1875f);
                Check(Mathf.Abs(smoky[3]-expected)<0.002f,"Beer-Lambert smoke attenuation");
                Check(smoky[0]<doubled,"Smoke absorbs incident light");
                cs.SetInt("_VolumeLightCount",0);float[] dark=Sample();Check(dark[0]==0&&dark[3]==1,"No lights clears radiance");
                report.AppendLine($"PASS: intensity doubles; uniform smoke light T {smoky[3]:F6}, analytic {expected:F6}; no-light output RGB=0 / T=1.");
            }
            finally{output.Release();UnityEngine.Object.DestroyImmediate(output);UnityEngine.Object.DestroyImmediate(density);UnityEngine.Object.DestroyImmediate(cs);}
        }
        static void ExportViews(StringBuilder report)
        {
            string folder=Root+"/Preview/Lighting";Directory.CreateDirectory(folder);
            Scene previous=SceneManager.GetActiveScene();var suspended=new List<Light>();var renderers=new List<Renderer>();
            foreach(var light in UnityEngine.Object.FindObjectsByType<Light>())if(light.enabled){light.enabled=false;suspended.Add(light);}
            foreach(var renderer in UnityEngine.Object.FindObjectsByType<Renderer>())if(renderer.enabled&&renderer.gameObject.layer==26){renderer.enabled=false;renderers.Add(renderer);}
            Scene scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);RenderSettings.skybox=null;RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(0.08f,0.08f,0.08f);
                var source=new GameObject("Lit torus",typeof(MeshFilter),typeof(MeshRenderer));source.layer=26;source.transform.rotation=Quaternion.Euler(70,10,8);
                source.GetComponent<MeshFilter>().sharedMesh=AssetDatabase.LoadAssetAtPath<Mesh>("Assets/SurfaceDustGpu/Generated/SourceTorus.asset");
                source.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Generated/Source.mat");
                var simGo=new GameObject("Smoke simulation",typeof(VolumeSmokeSimulation));simGo.layer=26;simGo.transform.position=new Vector3(0,1,0);
                var sim=simGo.GetComponent<VolumeSmokeSimulation>();sim.source=source.GetComponent<MeshFilter>();sim.solver=AssetDatabase.LoadAssetAtPath<ComputeShader>(Root+"/Shaders/VolumeSmoke.compute");sim.resolution=64;sim.Initialize();sim.paused=true;
                var cameraGo=new GameObject("Lighting demo camera",typeof(Camera),typeof(VolumeSmokeCamera),typeof(VolumeSmokeFreeCamera));cameraGo.layer=26;cameraGo.tag="MainCamera";
                var camera=cameraGo.GetComponent<Camera>();camera.transform.position=new Vector3(4,2.3f,-8);camera.transform.LookAt(new Vector3(0,0.8f,0));camera.fieldOfView=43;camera.farClipPlane=50;camera.cullingMask=1<<26;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(0.008f,0.012f,0.018f);camera.allowMSAA=false;
                var pipeline=new SerializedObject(GraphicsSettings.currentRenderPipeline);var list=pipeline.FindProperty("m_RendererDataList");int index=-1;
                for(int i=0;i<list.arraySize;i++)if(AssetDatabase.GetAssetPath(list.GetArrayElementAtIndex(i).objectReferenceValue)==Root+"/Generated/VolumeRenderer.asset")index=i;
                Check(index>=0,"Dedicated smoke renderer exists");camera.GetUniversalAdditionalCameraData().SetRenderer(index);camera.GetUniversalAdditionalCameraData().requiresDepthTexture=true;
                var view=cameraGo.GetComponent<VolumeSmokeCamera>();view.simulation=sim;view.showControls=false;view.ambientLight=new Color(0.03f,0.04f,0.06f);view.smokeColor=new Color(0.8f,0.85f,0.9f);view.anisotropy=0.2f;sim.sourceCamera=camera;view.validationCaptureDeltaTime=1f/60f;
                var lightGo=new GameObject("Volume spotlight",typeof(Light));var spot=lightGo.GetComponent<Light>();spot.type=LightType.Spot;spot.range=12;spot.spotAngle=48;spot.innerSpotAngle=28;spot.intensity=45;spot.color=new Color(1,0.6f,0.22f);spot.cullingMask=1<<26;
                lightGo.transform.position=new Vector3(-2,2,-2);lightGo.transform.LookAt(new Vector3(0,1,0));
                Capture(camera,folder+"/00-empty.png");for(int i=0;i<240;i++)sim.StepSimulation(1f/60f);
                view.volumeDisplay=VolumeSmokeCamera.VolumeDisplay.Scattering;var lit=Capture(camera,folder+"/01-scattering.png");
                report.AppendLine($"Captured volume light count: {view.activeVolumeLights}");
                Check(view.activeVolumeLights==1,"Scene light is extracted once per render; count="+view.activeVolumeLights);
                spot.enabled=false;var unlit=Capture(camera,folder+"/02-no-light.png");Check(view.activeVolumeLights==0,"Light list clears between camera renders");Check(Difference(lit,unlit)>100,"Scene spotlight changes scattering");spot.enabled=true;
                spot.transform.Rotate(0,180,0,Space.World);var away=Capture(camera,folder+"/03-turned-away.png");Check(Difference(lit,away)>100,"Turning spotlight changes scattering");spot.transform.LookAt(new Vector3(0,1,0));
                view.volumeDisplay=VolumeSmokeCamera.VolumeDisplay.Composite;Capture(camera,folder+"/04-composite.png");
                foreach(VolumeSmokeCamera.VolumeDisplay mode in Enum.GetValues(typeof(VolumeSmokeCamera.VolumeDisplay)))
                {
                    view.volumeDisplay=mode;view.slicePosition=mode==VolumeSmokeCamera.VolumeDisplay.ObstacleSlice?0.31f:0.5f;
                    Capture(camera,folder+"/View-"+mode+".png");
                }
                report.AppendLine("PASS: default-layer scene spotlight affects volume; disabling it and turning its cone changes integrated scattering; all 11 volume views exported.");
                view.volumeDisplay=VolumeSmokeCamera.VolumeDisplay.Composite;view.slicePosition=0.5f;view.validationCaptureDeltaTime=0;view.showControls=true;sim.paused=false;
                EditorSceneManager.SaveScene(scene,Root+"/VolumeSmokeLightingDemo.unity");
                report.AppendLine("Saved separate VolumeSmokeLightingDemo.unity; current scene and user lights preserved.");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene,true);if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);
                foreach(var light in suspended)if(light)light.enabled=true;
                foreach(var renderer in renderers)if(renderer)renderer.enabled=true;
            }
            AssetDatabase.Refresh();
        }
        static Color32[] Capture(Camera camera,string path)
        {
            var target=RenderTexture.GetTemporary(960,600,24,RenderTextureFormat.ARGB32);var previous=RenderTexture.active;var image=new Texture2D(960,600,TextureFormat.RGB24,false);
            try{RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});RenderTexture.active=target;image.ReadPixels(new Rect(0,0,960,600),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());return image.GetPixels32();}
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);UnityEngine.Object.DestroyImmediate(image);}
        }
        static int Difference(Color32[] a,Color32[] b){int count=0;for(int i=0;i<a.Length;i++)if(Mathf.Abs(a[i].r-b[i].r)>3||Mathf.Abs(a[i].g-b[i].g)>3||Mathf.Abs(a[i].b-b[i].b)>3)count++;return count;}
        static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    }
}
