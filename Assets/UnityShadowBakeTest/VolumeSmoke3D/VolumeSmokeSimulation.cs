using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace TA.VolumeSmoke3D
{
    [ExecuteAlways]
    public sealed class VolumeSmokeSimulation : MonoBehaviour
    {
        public MeshFilter source;
        public ComputeShader solver;
        [Tooltip("Camera used to capture the moving source. Defaults to Camera.main.")]
        public Camera sourceCamera;
        [Range(1, 4)] public float sourceBandCells = 2;
        [Range(0, 2)] public float inheritSourceMotion = 1;
        [Min(0.1f)] public float maxSourceSpeed = 15;
        [Range(32, 128)] public int resolution = 64;
        [Min(1)] public float domainSize = 6;
        [Range(20,80)] public int pressureIterations = 32;
        [Min(0)] public float emission = 3;
        [Min(0)] public float outwardForce = 0.7f;
        [Min(0)] public float buoyancy = 1;
        [Min(0)] public float vorticity = 4;
        [Min(0)] public float swirl = 0.5f;
        [Min(0)] public float dissipation = 0.35f;
        [Min(0)] public float velocityDrag = 0.2f;
        public Vector3 wind = new Vector3(0.05f,0,0);
        public bool paused;

        RTHandle[] velocity, density, pressure;
        RTHandle curl, divergence, projectedDivergence;
        RTHandle obstacle, surfaceSource, surfaceMotion;
        ComputeShader compute;
        int[] kernels;
        int densityIndex;
        public bool IsInitialized => density != null;
        public RTHandle Density => IsInitialized ? density[densityIndex] : null;
        public RTHandle Velocity => IsInitialized ? velocity[0] : null;
        public RTHandle Divergence => divergence;
        public RTHandle ProjectedDivergence => projectedDivergence;
        public RTHandle Obstacle => obstacle;
        public RTHandle SurfaceSource => surfaceSource;
        public RTHandle SurfaceMotion => surfaceMotion;
        public ComputeShader SourceCompute => compute;
        public int ScreenSourceKernel => kernels[10];
        public Matrix4x4 PreviousSourceMatrix { get; private set; }
        public int SourceCaptureCount { get; private set; }
        public Bounds DomainBounds { get; private set; }
        public int AllocatedResolution { get; private set; }
        public int StepCount { get; private set; }
        float simulationTime;
        int lastCaptureFrame=-1;
        bool sourceFieldsValid;
        Vector3 lastSourceCenter;

        void OnEnable()
        {
            if (!Application.IsPlaying(gameObject)) return;
            try { Initialize(); }
            catch(Exception e) { Release(); Debug.LogError("Volume smoke initialization: "+e.Message,this); enabled=false; }
        }
        void Update()
        {
            if (!Application.IsPlaying(gameObject)||!IsInitialized||paused)return;
            if(sourceFieldsValid&&Time.frameCount-lastCaptureFrame>1)ClearSourceFields();
            StepSimulation(Time.deltaTime);
        }

        [ContextMenu("Rebuild volume")]
        public void Initialize()
        {
            Release();
            try
            {
                if (!source || !source.sharedMesh || !solver) throw new InvalidOperationException("Assign Source and Solver.");
                if (!SystemInfo.supportsComputeShaders || !SystemInfo.supports3DTextures) throw new InvalidOperationException("Compute and 3D textures are required.");
                AllocatedResolution = Mathf.Clamp(resolution,32,128);
                DomainBounds = new Bounds(transform.position, Vector3.one * Mathf.Max(1,domainSize));
                // Geometry is captured by the renderer each frame. No CPU vertex read or static voxel bake.
                obstacle = Allocate("Dynamic source obstacle",GraphicsFormat.R32_SFloat);
                surfaceSource = Allocate("Reconstructed surface normal and rate",GraphicsFormat.R16G16B16A16_SFloat);
                surfaceMotion = Allocate("Reconstructed source velocity",GraphicsFormat.R16G16B16A16_SFloat);
                velocity = new[] { Allocate("Velocity A",GraphicsFormat.R32G32B32A32_SFloat), Allocate("Velocity B",GraphicsFormat.R32G32B32A32_SFloat) };
                density = new[] { Allocate("Density A",GraphicsFormat.R32_SFloat), Allocate("Density B",GraphicsFormat.R32_SFloat) };
                pressure = new[] { Allocate("Pressure A",GraphicsFormat.R32_SFloat), Allocate("Pressure B",GraphicsFormat.R32_SFloat) };
                curl = Allocate("Vorticity",GraphicsFormat.R32G32B32A32_SFloat);
                divergence = Allocate("Divergence before",GraphicsFormat.R32_SFloat);
                projectedDivergence = Allocate("Divergence after",GraphicsFormat.R32_SFloat);
                compute=Instantiate(solver); compute.hideFlags=HideFlags.HideAndDontSave;
                string[] names={"Clear","AdvectVelocity","Forces","ComputeCurl","Confinement","ComputeDivergence","Jacobi","Project","AdvectDensity","ClearSources","ReconstructScreenSource"};
                kernels=new int[names.Length];
                for(int i=0;i<names.Length;i++) kernels[i]=compute.FindKernel(names[i]);
                ResetSimulation();
            }
            catch { Release(); throw; }
        }

        RTHandle Allocate(string name, GraphicsFormat format)
        {
            var texture=new RenderTexture(new RenderTextureDescriptor(AllocatedResolution,AllocatedResolution)
            {
                graphicsFormat=format, dimension=TextureDimension.Tex3D, volumeDepth=AllocatedResolution,
                enableRandomWrite=true, depthBufferBits=0, msaaSamples=1, useMipMap=false, sRGB=false
            }) {name="3D smoke / "+name,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave};
            if (!texture.Create()) { DestroyImmediate(texture); throw new InvalidOperationException("Could not allocate 3D texture: "+name); }
            return RTHandles.Alloc(texture);
        }
        void Bind(int kernel,string name,RTHandle texture) => compute.SetTexture(kernel,name,texture.rt);
        void Dispatch(int kernel) { int groups=(AllocatedResolution+3)/4; compute.Dispatch(kernel,groups,groups,groups); }

        public void ResetSimulation()
        {
            if (!IsInitialized) return;
            compute.SetInt("_Size",AllocatedResolution);
            int k=kernels[0];
            Bind(k,"_CurlOut",curl);
            for(int i=0;i<2;i++)
            {
                Bind(k,"_VelocityOut",velocity[i]); Bind(k,"_DensityOut",density[i]); Bind(k,"_PressureOut",pressure[i]);
                Bind(k,"_DivergenceOut",i==0?divergence:projectedDivergence); Dispatch(k);
            }
            densityIndex=0; simulationTime=0; StepCount=0;
            ClearSourceFields();
            PreviousSourceMatrix=source.transform.localToWorldMatrix; SourceCaptureCount=0;
            lastSourceCenter=(source.transform.TransformPoint(source.sharedMesh.bounds.center)-DomainBounds.min)/DomainBounds.size.x;
        }

        void ClearSourceFields()
        {
            int k=kernels[9]; Bind(k,"_ObstacleOut",obstacle); Bind(k,"_SurfaceSourceOut",surfaceSource); Bind(k,"_SurfaceMotionOut",surfaceMotion); Dispatch(k);
            sourceFieldsValid=false;
        }

        public void MarkSourceCaptureRecorded(Matrix4x4 matrix)
        {
            PreviousSourceMatrix=matrix; SourceCaptureCount++;lastCaptureFrame=Time.frameCount;sourceFieldsValid=true;
        }

        public void StepSimulation(float deltaTime)
        {
            if (!IsInitialized || deltaTime<=0) return;
            float dt=Mathf.Min(deltaTime,1f/30f); simulationTime+=dt; StepCount++;
            float cell=DomainBounds.size.x/AllocatedResolution;
            if(source&&source.sharedMesh)lastSourceCenter=(source.transform.TransformPoint(source.sharedMesh.bounds.center)-DomainBounds.min)/DomainBounds.size.x;
            else if(sourceFieldsValid)ClearSourceFields();
            Vector3 center=lastSourceCenter;
            compute.SetFloat("_Dt",dt); compute.SetFloat("_TimeValue",simulationTime); compute.SetFloat("_CellSize",cell);
            compute.SetFloat("_Dissipation",Mathf.Max(0,dissipation));
            compute.SetVector("_Source",new Vector4(center.x,center.y,center.z,Mathf.Max(0,swirl)));
            compute.SetVector("_Forces",new Vector4(Mathf.Max(0,emission),Mathf.Max(0,outwardForce)/cell,Mathf.Max(0,buoyancy)/cell,Mathf.Max(0,vorticity)));
            compute.SetVector("_WindDrag",new Vector4(wind.x/cell,wind.y/cell,wind.z/cell,Mathf.Max(0,velocityDrag)));
            compute.SetFloat("_InheritSourceMotion",Mathf.Max(0,inheritSourceMotion));
            for(int i=1;i<=8;i++) Bind(kernels[i],"_Obstacle",obstacle);
            Bind(kernels[2],"_SurfaceSourceRead",surfaceSource); Bind(kernels[2],"_SurfaceMotionRead",surfaceMotion);
            Bind(kernels[8],"_SurfaceSourceRead",surfaceSource);
            RTHandle v=velocity[0], temp=velocity[1];
            void SwapVelocity() { (v,temp)=(temp,v); }
            int k=kernels[1]; Bind(k,"_VelocityRead",v); Bind(k,"_VelocityOut",temp); Dispatch(k); SwapVelocity();
            k=kernels[2]; Bind(k,"_VelocityRead",v); Bind(k,"_VelocityOut",temp); Bind(k,"_DensityRead",Density); Dispatch(k); SwapVelocity();
            k=kernels[3]; Bind(k,"_VelocityRead",v); Bind(k,"_CurlOut",curl); Dispatch(k);
            k=kernels[4]; Bind(k,"_VelocityRead",v); Bind(k,"_VelocityOut",temp); Bind(k,"_CurlRead",curl); Dispatch(k); SwapVelocity();
            k=kernels[5]; Bind(k,"_VelocityRead",v); Bind(k,"_DivergenceOut",divergence); Dispatch(k);
            RTHandle p=pressure[0],pTemp=pressure[1]; k=kernels[6]; Bind(k,"_DivergenceRead",divergence);
            int iterations=Mathf.Clamp(pressureIterations/2*2,20,80);
            for(int i=0;i<iterations;i++)
            {
                Bind(k,"_PressureRead",p); Bind(k,"_PressureOut",pTemp); Dispatch(k); (p,pTemp)=(pTemp,p);
            }
            k=kernels[7]; Bind(k,"_VelocityRead",v); Bind(k,"_VelocityOut",temp); Bind(k,"_PressureRead",p); Dispatch(k); SwapVelocity();
            k=kernels[5]; Bind(k,"_VelocityRead",v); Bind(k,"_DivergenceOut",projectedDivergence); Dispatch(k);
            k=kernels[8]; Bind(k,"_VelocityRead",v); Bind(k,"_DensityRead",Density); Bind(k,"_DensityOut",density[1-densityIndex]); Dispatch(k);
            densityIndex=1-densityIndex;
        }

        void OnDisable()=>Release();
        void OnDestroy()=>Release();
        void Release()
        {
            void Free(RTHandle handle)
            {
                if(handle==null)return;
                var texture=handle.rt; handle.Release();
                if(texture) {texture.Release(); if(Application.isPlaying)Destroy(texture);else DestroyImmediate(texture);}
            }
            if(velocity!=null)foreach(var rt in velocity)Free(rt);
            if(density!=null)foreach(var rt in density)Free(rt);
            if(pressure!=null)foreach(var rt in pressure)Free(rt);
            Free(curl);Free(divergence);Free(projectedDivergence);
            Free(obstacle);Free(surfaceSource);Free(surfaceMotion);
            if(compute){if(Application.isPlaying)Destroy(compute);else DestroyImmediate(compute);}
            velocity=density=pressure=null;curl=divergence=projectedDivergence=null;obstacle=surfaceSource=surfaceMotion=null;compute=null;
        }
        void OnDrawGizmosSelected()
        {
            Gizmos.color=new Color(0.2f,0.8f,1,0.7f);
            Gizmos.DrawWireCube(IsInitialized?DomainBounds.center:transform.position,Vector3.one*(IsInitialized?DomainBounds.size.x:domainSize));
        }
    }
}
