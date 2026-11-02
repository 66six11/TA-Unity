using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug=UnityEngine.Debug;

namespace TA.SdfOutline.Editor
{
    [InitializeOnLoad]
    public static class SdfPerformanceTest
    {
        [Serializable] public class Result
        {
            public string mode;public int width,height,samples;public double cpuMeanMs,cpuMedianMs,cpuP95Ms;
            public string[] gpuNames;public double[] gpuMeanMs;public int[] gpuValidSamples;
        }
        [Serializable] public class Report
        {
            public string time,unity,gpu,api,cpu,note;public Result[] results;
        }
        struct Case{public int w,h,mode;public Case(int w,int h,int mode){this.w=w;this.h=h;this.mode=mode;}}
        const int Warmup=30,Samples=90;
        static readonly string[] Modes={"Disabled","SDF","Dashed","SpikeDashed","Full","FullOrbit"};
        static readonly List<Case> cases=new List<Case>();
        static readonly List<Result> results=new List<Result>();
        static readonly List<double> cpuSamples=new List<double>();
        static readonly List<string> names=new List<string>();
        static readonly List<Recorder> recorders=new List<Recorder>();
        static List<double>[] gpuSamples;
        static Camera camera;static SdfOutlineControls controls;static SdfOutlineFeature feature;
        static RenderTexture target;static Vector3 position;static Quaternion rotation;static bool cameraEnabled,featureEnabled,controlsEnabled;
        static string savedControls;static bool running,savedProfiler,savedGpuArea;static int caseIndex,frame;static double started;
        static SdfPerformanceTest(){EditorApplication.update+=Poll;AssemblyReloadEvents.beforeAssemblyReload+=Cleanup;}
        [MenuItem("Tools/TA/SDF Outline/Run Performance Test")]
        public static void Start()
        {
            if(running)return;
            foreach(var root in UnityEngine.SceneManagement.SceneManager.GetSceneByPath("Assets/SdfOutline/SdfOutlineDemo.unity").GetRootGameObjects())
                if(root.TryGetComponent<Camera>(out var c))camera=c;
            if(!camera)throw new InvalidOperationException("Open SdfOutlineDemo first.");
            controls=camera.GetComponent<SdfOutlineControls>();
            var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/SdfOutline/Demo/SdfOutlineRenderer.asset");
            foreach(var f in renderer.rendererFeatures)if(f is SdfOutlineFeature sdf)feature=sdf;
            if(!feature||!controls)throw new InvalidOperationException("Demo feature or controls missing.");
            savedControls=EditorJsonUtility.ToJson(controls);controlsEnabled=controls.enabled;controls.enabled=true;
            position=camera.transform.position;rotation=camera.transform.rotation;cameraEnabled=camera.enabled;camera.enabled=false;featureEnabled=feature.isActive;
            savedProfiler=Profiler.enabled;savedGpuArea=Profiler.GetAreaEnabled(ProfilerArea.GPU);
            Profiler.SetAreaEnabled(ProfilerArea.GPU,true);Profiler.enabled=true;
            cases.Clear();results.Clear();names.Clear();recorders.Clear();
            foreach(var resolution in new[]{new Vector2Int(1280,720),new Vector2Int(1920,1080),new Vector2Int(2560,1440)})
                for(int mode=0;mode<5;mode++)cases.Add(new Case(resolution.x,resolution.y,mode));
            cases.Add(new Case(1920,1080,5));cases.Add(new Case(1920,1080,0));
            caseIndex=0;running=true;started=EditorApplication.timeSinceStartup;BeginCase();
            File.WriteAllText("Library/SdfPerformance-status.txt","Running 17 cases, 30 warmup + 90 samples each.");
        }
        static void Poll()
        {
            if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            if(!running)
            {
                if(!File.Exists("Library/SdfPerformance.request"))return;
                try{File.Delete("Library/SdfPerformance.request");Start();}catch(Exception e){Cleanup();File.WriteAllText("Library/SdfPerformance-status.txt",e.ToString());}
                return;
            }
            try{Tick();}catch(Exception e){Cleanup();File.WriteAllText("Library/SdfPerformance-status.txt",e.ToString());Debug.LogException(e);}
        }
        static void BeginCase()
        {
            if(target){target.Release();UnityEngine.Object.DestroyImmediate(target);}
            var test=cases[caseIndex];target=new RenderTexture(test.w,test.h,24,RenderTextureFormat.ARGB32){name="SDF performance target",antiAliasing=1};target.Create();
            camera.transform.SetPositionAndRotation(position,rotation);
            feature.SetActive(test.mode!=0);controls.display=SdfOutlineFeature.Display.Composite;
            controls.dashesEnabled=test.mode>=2;controls.spikeDashesEnabled=test.mode>=3;controls.paperEnabled=test.mode>=4;controls.linesEnabled=test.mode>=4;
            // Fixed parameters for reproducibility; restore the user's complete controls afterward.
            controls.width=5;controls.dashCount=20;controls.dashDuty=0.65f;controls.dashPhase=0;controls.projectionAngle=0;
            controls.spikeCount=48;controls.spikePhase=0;controls.spikeHeight=18;controls.spikeOffset=3;controls.spikeRandomness=0.65f;controls.spikeSeed=17;
            controls.paperStrength=0.45f;controls.paperScale=1;controls.edgeRoughness=0.8f;
            controls.lineSpacing=9;controls.lineWidth=1;controls.lineRange=64;controls.lineOpacity=0.7f;
            cpuSamples.Clear();frame=0;
            gpuSamples=new List<double>[names.Count];for(int i=0;i<names.Count;i++)gpuSamples[i]=new List<double>();
        }
        static void Discover()
        {
            var available=new List<string>();Sampler.GetNames(available);
            foreach(var name in available)
            {
                if(!name.StartsWith("SDF:")||names.Contains(name))continue;
                var recorder=Recorder.Get(name);if(!recorder.isValid)continue;
                recorder.enabled=true;names.Add(name);recorders.Add(recorder);
            }
            gpuSamples=new List<double>[names.Count];for(int i=0;i<names.Count;i++)gpuSamples[i]=new List<double>();
        }
        static void Tick()
        {
            var test=cases[caseIndex];
            if(frame==Warmup-10)Discover();
            // GPU recorder results refer to completed frames, so read before submitting the next request.
            if(frame>=Warmup)
                for(int i=0;i<recorders.Count;i++){long ns=recorders[i].gpuElapsedNanoseconds;if(ns>0)gpuSamples[i].Add(ns/1e6);}
            if(test.mode==5)
            {
                float a=(frame-Warmup)*0.3f;var pivot=new Vector3(0,0.3f,0);
                camera.transform.position=pivot+Quaternion.Euler(0,a,0)*(position-pivot);camera.transform.LookAt(pivot);
            }
            long begin=Stopwatch.GetTimestamp();
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
            double ms=(Stopwatch.GetTimestamp()-begin)*1000.0/Stopwatch.Frequency;
            if(frame>=Warmup)cpuSamples.Add(ms);
            frame++;
            if(frame<Warmup+Samples)return;
            var cpu=cpuSamples.ToArray();Array.Sort(cpu);
            var result=new Result{mode=Modes[test.mode],width=test.w,height=test.h,samples=cpu.Length,cpuMeanMs=Mean(cpu),cpuMedianMs=cpu[cpu.Length/2],cpuP95Ms=cpu[(int)(cpu.Length*0.95)],gpuNames=names.ToArray(),gpuMeanMs=new double[names.Count],gpuValidSamples=new int[names.Count]};
            for(int i=0;i<names.Count;i++){result.gpuMeanMs[i]=Mean(gpuSamples[i]);result.gpuValidSamples[i]=gpuSamples[i].Count;}
            results.Add(result);caseIndex++;
            File.WriteAllText("Library/SdfPerformance-status.txt",$"Completed {caseIndex}/{cases.Count}: {test.w}x{test.h} {result.mode}, CPU submit median {result.cpuMedianMs:F3} ms; elapsed {EditorApplication.timeSinceStartup-started:F0}s.");
            if(caseIndex<cases.Count){BeginCase();return;}
            var report=new Report{time=DateTimeOffset.Now.ToString("o"),unity=Application.unityVersion,gpu=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString(),cpu=SystemInfo.processorType,
                note="Unity Editor offscreen SubmitRenderRequest, ARGB32, 1x MSAA; 30 warmup+90 samples per case. CPU is request submission wall time, not whole frame. GPU data is positive Recorder.gpuElapsedNanoseconds samples for SDF markers only, may be unavailable or include other cameras. No CPU/GPU synchronous wait. Profiler enabled during capture. UI excluded. Disabled at 1080p repeated at end to assess drift. Orbit changes camera 0.3 degree/sample. Toggle cases do not remove the common SDF passes or guarantee compiler skips all disabled shader math.",results=results.ToArray()};
            File.WriteAllText("Library/SdfPerformance.json",JsonUtility.ToJson(report,true));
            Cleanup();File.WriteAllText("Library/SdfPerformance-status.txt","PASS: 17 cases captured; Library/SdfPerformance.json");
        }
        static double Mean(IReadOnlyList<double> values){if(values.Count==0)return 0;double sum=0;foreach(double value in values)sum+=value;return sum/values.Count;}
        static void Cleanup()
        {
            if(!running)return;running=false;
            foreach(var recorder in recorders)recorder.enabled=false;
            if(camera){camera.enabled=cameraEnabled;camera.transform.SetPositionAndRotation(position,rotation);}
            if(controls){EditorJsonUtility.FromJsonOverwrite(savedControls,controls);controls.enabled=controlsEnabled;}
            if(feature)feature.SetActive(featureEnabled);
            if(target){target.Release();UnityEngine.Object.DestroyImmediate(target);target=null;}
            Profiler.SetAreaEnabled(ProfilerArea.GPU,savedGpuArea);Profiler.enabled=savedProfiler;
        }
    }
}
