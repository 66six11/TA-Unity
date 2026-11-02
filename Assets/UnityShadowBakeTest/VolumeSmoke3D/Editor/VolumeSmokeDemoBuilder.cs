using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace TA.VolumeSmoke3D.Editor
{
    [InitializeOnLoad]
    public static class VolumeSmokeDemoBuilder
    {
        const string Root="Assets/VolumeSmoke3D";
        const string ScenePath=Root+"/VolumeSmoke3DDemo.unity";
        const string Request="Library/VolumeSmoke3D.request";
        const string Status="Library/VolumeSmoke3D-validation.txt";
        const int Layer=26;
        static VolumeSmokeDemoBuilder()=>EditorApplication.update+=Poll;
        static void Poll()
        {
            if(EditorApplication.isCompiling||EditorApplication.isUpdating||!File.Exists(Request))return;
            if(EditorApplication.isPlayingOrWillChangePlaymode)
            {
                // Explicit local test request: finish the active preview before rebuilding assets.
                if(File.ReadAllText(Request).Trim()=="rebuild"&&EditorApplication.isPlaying)EditorApplication.ExitPlaymode();
                return;
            }
            string command;
            try{command=File.ReadAllText(Request).Trim();File.Delete(Request);}catch(IOException){return;}
            try
            {
                if(command=="open")
                {
                    bool dirty=false;for(int i=0;i<SceneManager.sceneCount;i++)dirty|=SceneManager.GetSceneAt(i).isDirty;
                    Scene scene=SceneManager.GetSceneByPath(ScenePath);
                    if(!dirty)scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
                    else if(!scene.IsValid()||!scene.isLoaded)scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
                    SceneManager.SetActiveScene(scene);Selection.activeObject=UnityEngine.Object.FindAnyObjectByType<VolumeSmokeSimulation>();
                    File.WriteAllText("Library/VolumeSmoke3D-open.txt","Opened "+scene.path);
                }
                else BuildAndVerify();
            }
            catch(Exception e){File.WriteAllText(Status,e.ToString());Debug.LogException(e);}
        }
        [MenuItem("Tools/TA/Volume Smoke 3D/Open Demo")]
        public static void OpenDemo()
        {
            if(!File.Exists(ScenePath))BuildAndVerify();
            if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())EditorSceneManager.OpenScene(ScenePath);
        }
        [MenuItem("Tools/TA/Volume Smoke 3D/Build and Verify Demo")]
        public static void BuildAndVerify()
        {
            Directory.CreateDirectory(Root+"/Generated");Directory.CreateDirectory(Root+"/Preview");AssetDatabase.Refresh();
            var shader=AssetDatabase.LoadAssetAtPath<Shader>(Root+"/Shaders/VolumeSmoke.shader");
            var solver=AssetDatabase.LoadAssetAtPath<ComputeShader>(Root+"/Shaders/VolumeSmoke.compute");
            var captureShader=AssetDatabase.LoadAssetAtPath<Shader>(Root+"/Shaders/VolumeSourceCapture.shader");
            if(!shader||!solver||!captureShader)throw new InvalidOperationException("Volume smoke shader assets are not imported.");
            var compileMaterial=new Material(shader);
            try{ShaderUtil.CompilePass(compileMaterial,0,true);}finally{UnityEngine.Object.DestroyImmediate(compileMaterial);}
            if(ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException("Volume raymarch shader compilation failed.");
            var captureCompile=new Material(captureShader);
            try{for(int i=0;i<captureCompile.passCount;i++)ShaderUtil.CompilePass(captureCompile,i,true);}finally{UnityEngine.Object.DestroyImmediate(captureCompile);}
            if(ShaderUtil.ShaderHasError(captureShader))throw new InvalidOperationException("Source capture shader compilation failed.");
            int rendererIndex=InstallRenderer(shader,captureShader);
            var report=new StringBuilder();report.AppendLine($"Unity {Application.unityVersion}; {SystemInfo.graphicsDeviceName}; {SystemInfo.graphicsDeviceType}");
            var suspended=new List<Light>();foreach(var light in UnityEngine.Object.FindObjectsByType<Light>())if(light.enabled){suspended.Add(light);light.enabled=false;}
            var suspendedRenderers=new List<Renderer>();
            foreach(var renderer in UnityEngine.Object.FindObjectsByType<Renderer>())
                if(renderer.enabled&&renderer.gameObject.layer==Layer){suspendedRenderers.Add(renderer);renderer.enabled=false;}
            Scene original=SceneManager.GetActiveScene();
            Scene scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);RenderSettings.skybox=null;RenderSettings.fog=false;
                RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(0.18f,0.23f,0.29f);
                var lit=Shader.Find("Universal Render Pipeline/Lit");
                var source=new GameObject("Original model - dynamic screen-captured source",typeof(MeshFilter),typeof(MeshRenderer),typeof(VolumeSmokeSourceMotion));source.layer=Layer;
                source.transform.rotation=Quaternion.Euler(70,10,8);
                source.GetComponent<MeshFilter>().sharedMesh=AssetDatabase.LoadAssetAtPath<Mesh>("Assets/SurfaceDustGpu/Generated/SourceTorus.asset");
                var gold=MaterialAsset("Source",lit,new Color(0.58f,0.36f,0.14f));gold.SetFloat("_Metallic",0.7f);gold.SetFloat("_Smoothness",0.48f);
                source.GetComponent<Renderer>().sharedMaterial=gold;
                var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);floor.name="Ground";floor.layer=Layer;
                floor.transform.position=new Vector3(0,-1.8f,0);floor.transform.localScale=Vector3.one*2;
                floor.GetComponent<Renderer>().sharedMaterial=MaterialAsset("Ground",lit,new Color(0.055f,0.075f,0.1f));
                NewLight("Cool key",new Vector3(35,-35,0),new Color(0.78f,0.9f,1),1.7f);
                NewLight("Warm rim",new Vector3(30,150,0),new Color(1,0.68f,0.35f),1.3f);
                var simulationGo=new GameObject("3D fluid volume - camera independent",typeof(VolumeSmokeSimulation));simulationGo.layer=Layer;simulationGo.transform.position=new Vector3(0,1,0);
                var simulation=simulationGo.GetComponent<VolumeSmokeSimulation>();simulation.source=source.GetComponent<MeshFilter>();simulation.solver=solver;simulation.resolution=64;
                var cameraGo=new GameObject("Volume Smoke Camera",typeof(Camera),typeof(VolumeSmokeCamera),typeof(VolumeSmokeFreeCamera));cameraGo.layer=Layer;cameraGo.tag="MainCamera";
                Camera camera=cameraGo.GetComponent<Camera>();camera.transform.position=new Vector3(4,2.3f,-8);camera.transform.LookAt(new Vector3(0,0.8f,0));
                camera.fieldOfView=43;camera.nearClipPlane=0.1f;camera.farClipPlane=50;camera.allowMSAA=false;
                camera.cullingMask=1<<Layer;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(0.018f,0.025f,0.04f);
                var data=camera.GetUniversalAdditionalCameraData();data.SetRenderer(rendererIndex);data.requiresDepthTexture=true;
                var view=cameraGo.GetComponent<VolumeSmokeCamera>();view.simulation=simulation;
                simulation.sourceCamera=camera;view.validationCaptureDeltaTime=1f/60f;
                simulation.Initialize();simulation.paused=true;
                UnityEngine.Object.DestroyImmediate(Capture(camera));
                var empty=Capture(camera);
                for(int i=0;i<240;i++)
                {
                    simulation.StepSimulation(1f/60f);
                    if(i%60==59)File.WriteAllText("Library/VolumeSmoke3D-progress.txt",$"Simulated {i+1}/240 three-dimensional steps");
                }
                var density=Read(simulation.Density.rt);double mass=0;int zSlices=0;float maxInside=0;
                var mask=Read(simulation.Obstacle.rt);int size=simulation.AllocatedResolution;
                Require(density.Length==size*size*size,"Diagnostic readback must include every 3D texture layer.");
                for(int z=0;z<size;z++)
                {
                    bool hasDensity=false;
                    for(int y=0;y<size;y++)for(int x=0;x<size;x++)
                    {
                        int index=x+size*(y+size*z);float value=density[index];
                        Require(float.IsFinite(value)&&value>=0,"Density must be finite and non-negative.");mass+=value;
                        if(value>0.01f)hasDensity=true;if(mask[index]>0.5f)maxInside=Mathf.Max(maxInside,value);
                    }
                    if(hasDensity)zSlices++;
                }
                Require(mass>100&&zSlices>size/8,"Smoke must occupy multiple depth layers in a 3D volume.");
                Require(maxInside<0.00001f,"Density must not occupy the reconstructed model interval.");
                double before=Rms(Read(simulation.Divergence.rt)),after=Rms(Read(simulation.ProjectedDivergence.rt));
                Require(after<before*0.6,"3D pressure projection must substantially reduce divergence.");
                report.AppendLine($"PASS: {size}^3 grid, screen source captured {simulation.SourceCaptureCount} times, density mass {mass:F1}, smoke in {zSlices} depth layers, zero density in reconstructed solid cells.");
                report.AppendLine($"PASS: divergence RMS {before:F6} -> {after:F6} after {simulation.pressureIterations} pressure iterations.");
                var front=Capture(camera);int changed=DifferentPixels(empty,front);Require(changed>3000,"Expected visible 3D raymarched smoke.");
                File.WriteAllBytes(Root+"/Preview/Front.png",front.EncodeToPNG());File.WriteAllBytes(Root+"/Preview/SourceOnly.png",empty.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(front);UnityEngine.Object.DestroyImmediate(empty);
                Vector3 originalPosition=camera.transform.position;Quaternion originalRotation=camera.transform.rotation;
                var densityTexture=simulation.Density.rt;int steps=simulation.StepCount;
                camera.transform.RotateAround(simulation.DomainBounds.center,Vector3.up,90);
                var side=Capture(camera);File.WriteAllBytes(Root+"/Preview/Side.png",side.EncodeToPNG());UnityEngine.Object.DestroyImmediate(side);
                camera.transform.RotateAround(simulation.DomainBounds.center,Vector3.up,90);
                var back=Capture(camera);File.WriteAllBytes(Root+"/Preview/Back.png",back.EncodeToPNG());UnityEngine.Object.DestroyImmediate(back);
                Require(simulation.Density.rt==densityTexture&&simulation.StepCount==steps,"Camera rotation must not reset or advance a paused volume.");
                var unchanged=Read(simulation.Density.rt);Require(Equal(density,unchanged),"Orbiting must preserve every voxel of the same smoke state.");
                var resized=Capture(camera,960,540);UnityEngine.Object.DestroyImmediate(resized);
                Require(simulation.Density.rt==densityTexture&&Equal(density,Read(simulation.Density.rt)),"Changing screen size must preserve the world-space volume.");
                report.AppendLine($"PASS: front/side/back renders; camera orbit and 960x540 target preserve all {density.Length} density voxels exactly.");
                camera.transform.SetPositionAndRotation(originalPosition,originalRotation);
                UnityEngine.Object.DestroyImmediate(Capture(camera));
                foreach(VolumeSmokeCamera.SourceDisplay mode in Enum.GetValues(typeof(VolumeSmokeCamera.SourceDisplay)))
                {
                    if(mode==VolumeSmokeCamera.SourceDisplay.Smoke)continue;
                    view.sourceDisplay=mode;var image=Capture(camera);
                    File.WriteAllBytes(Root+"/Preview/Source-"+mode+".png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
                }
                view.sourceDisplay=VolumeSmokeCamera.SourceDisplay.Smoke;
                Vector3 sourceCentroid=SourceCentroid(simulation,out double sourceRate);
                int oldCaptureHeight=view.captureHeight;view.captureHeight=512;
                UnityEngine.Object.DestroyImmediate(Capture(camera));
                SourceCentroid(simulation,out double highResolutionRate);
                Require(Math.Abs(highResolutionRate/sourceRate-1)<0.35,"Source injection must not scale with capture pixel count.");
                view.captureHeight=oldCaptureHeight;
                UnityEngine.Object.DestroyImmediate(Capture(camera));
                Vector3 originalSourcePosition=source.transform.position;Quaternion originalSourceRotation=source.transform.rotation;
                source.transform.position+=new Vector3(0.65f,0.1f,0);
                source.transform.rotation=Quaternion.Euler(0,25,0)*originalSourceRotation;
                UnityEngine.Object.DestroyImmediate(Capture(camera));
                Vector3 movedCentroid=SourceCentroid(simulation,out double movedRate);
                Require(movedCentroid.x>sourceCentroid.x+0.2f&&movedRate>0,"Reconstructed emission must follow model translation and rotation.");
                Require(Equal(density,Read(simulation.Density.rt)),"Updating a moving source must preserve existing smoke history.");
                float[] motion=Read(simulation.SurfaceMotion.rt,TextureFormat.RGBAFloat);float maxSpeed=0;
                for(int i=0;i<motion.Length;i+=4)if(motion[i+3]>0)maxSpeed=Mathf.Max(maxSpeed,new Vector3(motion[i],motion[i+1],motion[i+2]).magnitude);
                Require(maxSpeed>0.1f&&maxSpeed<=simulation.maxSourceSpeed+0.1f,"Screen reconstruction must capture and clamp rigid surface motion.");
                simulation.StepSimulation(1f/60f);
                UnityEngine.Object.DestroyImmediate(Capture(camera)); // Current pose is stationary: surface motion returns to zero.
                for(int i=0;i<59;i++)simulation.StepSimulation(1f/60f);
                var moving=Capture(camera);File.WriteAllBytes(Root+"/Preview/MovingSource.png",moving.EncodeToPNG());UnityEngine.Object.DestroyImmediate(moving);
                report.AppendLine($"PASS: source centroid follows translation ({sourceCentroid.x:F3} -> {movedCentroid.x:F3}); inherited motion <= {maxSpeed:F2}; old smoke preserved.");
                report.AppendLine($"PASS: capture height 256 -> 512 changes voxel source rate by {Math.Abs(highResolutionRate/sourceRate-1)*100:F1}% rather than multiplying emission by pixel count.");
                source.transform.SetPositionAndRotation(originalSourcePosition,originalSourceRotation);
                Mesh readable=simulation.source.sharedMesh;Mesh gpuOnly=UnityEngine.Object.Instantiate(readable);
                try
                {
                    gpuOnly.UploadMeshData(true);simulation.source.sharedMesh=gpuOnly;
                    UnityEngine.Object.DestroyImmediate(Capture(camera));SourceCentroid(simulation,out double gpuOnlyRate);
                    Require(gpuOnlyRate>0,"Source capture must work without CPU-readable vertex data.");
                }
                finally{simulation.source.sharedMesh=readable;UnityEngine.Object.DestroyImmediate(gpuOnly);}
                report.AppendLine("PASS: GPU-only (Read/Write disabled) mesh still produces a reconstructed source.");
                simulation.ResetSimulation();Require(Rms(Read(simulation.Density.rt))==0,"Reset must clear density.");
                simulation.enabled=false;Require(!simulation.IsInitialized,"Disable must release volume textures.");
                simulation.enabled=true;simulation.paused=false;view.validationCaptureDeltaTime=0;
                Require(source.GetComponent<Renderer>().enabled,"Original model must remain visible.");
                report.AppendLine("PASS: explicit reset clears density; disable releases resources; original mesh remains visible. Earlier screen-space smoke example retained.");
                EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene,ScenePath);AssetDatabase.SaveAssets();
                report.AppendLine("Saved "+ScenePath);File.WriteAllText(Status,report.ToString());Debug.Log(report.ToString());
            }
            finally
            {
                EditorSceneManager.CloseScene(scene,true);if(original.IsValid()&&original.isLoaded)SceneManager.SetActiveScene(original);
                foreach(var light in suspended)if(light)light.enabled=true;
                foreach(var renderer in suspendedRenderers)if(renderer)renderer.enabled=true;
            }
            AssetDatabase.Refresh();
        }
        static int InstallRenderer(Shader shader,Shader captureShader)
        {
            var pipeline=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;if(!pipeline)throw new InvalidOperationException("URP required.");
            string path=Root+"/Generated/VolumeRenderer.asset";var data=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if(!data){AssetDatabase.CopyAsset("Assets/Settings/PC_Renderer.asset",path);data=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);data.rendererFeatures.Clear();}
            VolumeSmokeFeature feature=null;foreach(var item in data.rendererFeatures)if(item is VolumeSmokeFeature volume)feature=volume;
            if(!feature){feature=ScriptableObject.CreateInstance<VolumeSmokeFeature>();feature.name="Volume Smoke 3D";AssetDatabase.AddObjectToAsset(feature,data);data.rendererFeatures.Add(feature);}
            feature.volumeShader=shader;feature.sourceCaptureShader=captureShader;feature.Create();feature.SetActive(true);
            var settings=new SerializedObject(data);settings.FindProperty("m_RenderingMode").intValue=0;settings.FindProperty("m_IntermediateTextureMode").intValue=1;settings.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(feature);EditorUtility.SetDirty(data);data.SetDirty();
            var pipelineSettings=new SerializedObject(pipeline);var list=pipelineSettings.FindProperty("m_RendererDataList");int index=-1;
            for(int i=0;i<list.arraySize;i++)if(list.GetArrayElementAtIndex(i).objectReferenceValue==data)index=i;
            if(index<0){index=list.arraySize;list.arraySize++;list.GetArrayElementAtIndex(index).objectReferenceValue=data;pipelineSettings.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(pipeline);}
            AssetDatabase.SaveAssets();return index;
        }
        static Material MaterialAsset(string name,Shader shader,Color color)
        {
            string path=Root+"/Generated/"+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material){material=new Material(shader);AssetDatabase.CreateAsset(material,path);}material.SetColor("_BaseColor",color);EditorUtility.SetDirty(material);return material;
        }
        static void NewLight(string name,Vector3 rotation,Color color,float intensity)
        {
            var go=new GameObject(name,typeof(Light));go.layer=Layer;go.transform.rotation=Quaternion.Euler(rotation);
            var light=go.GetComponent<Light>();light.type=LightType.Directional;light.color=color;light.intensity=intensity;light.cullingMask=1<<Layer;
        }
        static Vector3 SourceCentroid(VolumeSmokeSimulation simulation,out double total)
        {
            float[] source=Read(simulation.SurfaceSource.rt,TextureFormat.RGBAFloat);
            int size=simulation.AllocatedResolution;total=0;Vector3 weighted=Vector3.zero;
            for(int z=0;z<size;z++)for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float weight=source[(x+size*(y+size*z))*4+3];total+=weight;
                weighted+=new Vector3(x+0.5f,y+0.5f,z+0.5f)*weight;
            }
            Require(total>0,"A non-empty source mask must reconstruct valid 3D emission cells.");
            return simulation.DomainBounds.min+weighted/(float)total/size*simulation.DomainBounds.size.x;
        }
        static float[] Read(RenderTexture texture,TextureFormat format=TextureFormat.RFloat)
        {
            var request=AsyncGPUReadback.Request(texture,0,format);request.WaitForCompletion();
            if(request.hasError)throw new InvalidOperationException("3D texture readback failed.");
            int layerLength=request.GetData<float>(0).Length;
            var result=new float[layerLength*request.layerCount];
            for(int layer=0;layer<request.layerCount;layer++)
            {
                var data=request.GetData<float>(layer);
                for(int i=0;i<data.Length;i++)result[layer*layerLength+i]=data[i];
            }
            return result;
        }
        static double Rms(float[] values){double sum=0;foreach(float value in values){Require(float.IsFinite(value),"Diagnostic field must be finite.");sum+=value*value;}return Math.Sqrt(sum/values.Length);}
        static bool Equal(float[] a,float[] b){if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
        static Texture2D Capture(Camera camera,int width=1280,int height=800)
        {
            var target=RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32);var previous=RenderTexture.active;
            var image=new Texture2D(width,height,TextureFormat.RGB24,false);
            try{RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});RenderTexture.active=target;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();return image;}
            catch{UnityEngine.Object.DestroyImmediate(image);throw;}finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);}
        }
        static int DifferentPixels(Texture2D a,Texture2D b)
        {
            var aa=a.GetPixels32();var bb=b.GetPixels32();int count=0;
            for(int i=0;i<aa.Length;i++)if(Mathf.Abs(aa[i].r-bb[i].r)>3||Mathf.Abs(aa[i].g-bb[i].g)>3||Mathf.Abs(aa[i].b-bb[i].b)>3)count++;return count;
        }
        static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    }
}
