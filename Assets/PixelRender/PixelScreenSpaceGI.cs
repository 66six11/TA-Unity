using UnityEngine;
using UnityEngine.Rendering;

namespace TA.PixelRender
{
    [ExecuteAlways, RequireComponent(typeof(PixelCamera))]
    public sealed class PixelScreenSpaceGI : MonoBehaviour
    {
        public enum GIView { GIOnly, SourceRadiance, HitRatio, RawGI, Off }
        public GIView view=GIView.GIOnly;
        [Range(32,540)] public int giHeight=90;
        [Range(4,64)] public int rayCount=16;
        [Range(8,96)] public int raySteps=48;
        [Min(0.1f)] public float radius=5;
        [Min(0.001f)] public float thickness=0.18f;
        [Min(0.001f)] public float normalBias=0.04f;
        public bool spatialDenoise=true;
        [Min(0.01f)] public float filterDepthSigma=0.3f;
        [Tooltip("Optional linear HDR outgoing radiance aligned to the information buffers. Overrides toon direct + emission, or emission when lighting is off. Never use final display output.")]
        public Texture externalRadiance;
        public RenderTexture RawGI { get; private set; }
        public RenderTexture IndirectDiffuse { get; private set; }
        Material material;
        bool warnedSize;

        void OnEnable()
        {
            var shader=Resources.Load<Shader>("PixelSSGI");
            if(shader) material=new Material(shader) { hideFlags=HideFlags.HideAndDontSave };
            else Debug.LogError("PixelSSGI shader missing",this);
        }
        RenderTexture Target(string label,int width,int height)
        {
            var rt=new RenderTexture(width,height,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear)
            { name=label,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp,antiAliasing=1,hideFlags=HideFlags.HideAndDontSave };
            rt.Create(); return rt;
        }
        // Transport only: optional upstream direct radiance is read, never evaluated here.
        public void Record(CommandBuffer cmd,PixelCamera surface,Camera camera,Texture sourceRadiance=null,bool present=true)
        {
            if(!material || view==GIView.Off) return;
            int h=Mathf.Clamp(giHeight,32,540),w=Mathf.RoundToInt(h*16f/9f);
            if(!RawGI || !RawGI.IsCreated() || RawGI.width!=w || RawGI.height!=h)
            {
                Release(); RawGI=Target("SSGI raw diffuse",w,h); IndirectDiffuse=Target("SSGI filtered diffuse",w,h);
            }
            Texture input=sourceRadiance ? sourceRadiance : surface.Radiance;
            if(externalRadiance)
            {
                if(externalRadiance.width==surface.Albedo.width && externalRadiance.height==surface.Albedo.height)
                { input=externalRadiance; warnedSize=false; }
                else if(!warnedSize) { Debug.LogWarning("External GI radiance must match information buffer dimensions; using pipeline radiance instead.",this); warnedSize=true; }
            }
            Matrix4x4 projection=GL.GetGPUProjectionMatrix(camera.projectionMatrix,true);
            material.SetTexture("_AlbedoBuffer",surface.Albedo); material.SetTexture("_NormalBuffer",surface.WorldNormal);
            material.SetTexture("_DepthBuffer",surface.LinearDepth); material.SetTexture("_Radiance",input);
            material.SetTexture("_RawGI",RawGI); material.SetTexture("_FilteredGI",IndirectDiffuse);
            material.SetMatrix("_Projection",projection); material.SetMatrix("_InverseProjection",projection.inverse);
            material.SetMatrix("_WorldToView",camera.worldToCameraMatrix);
            material.SetVector("_InformationSize",new Vector4(surface.Albedo.width,surface.Albedo.height,1f/surface.Albedo.width,1f/surface.Albedo.height));
            material.SetVector("_GIStep",new Vector4(1f/w,1f/h,0,0));
            material.SetInt("_RayCount",Mathf.Clamp(rayCount,4,64)); material.SetInt("_RaySteps",Mathf.Clamp(raySteps,8,96));
            material.SetFloat("_Radius",Mathf.Max(0.1f,radius)); material.SetFloat("_Thickness",Mathf.Max(0.001f,thickness));
            material.SetFloat("_Bias",Mathf.Max(0.001f,normalBias)); material.SetFloat("_FilterSigma",Mathf.Max(0.01f,filterDepthSigma));
            material.SetFloat("_View",(int)view);
            cmd.Blit(surface.Radiance,RawGI,material,0);
            if(spatialDenoise) cmd.Blit(RawGI,IndirectDiffuse,material,1);
            else cmd.Blit(RawGI,IndirectDiffuse);
            // GI debug presentation replaces the output, never feeds back into radiance.
            if(present) cmd.Blit(IndirectDiffuse,surface.Output,material,2);
        }
        void Release()
        {
            foreach(var rt in new[]{RawGI,IndirectDiffuse}) if(rt) { rt.Release(); Dispose(rt); }
            RawGI=IndirectDiffuse=null;
        }
        void OnDisable() { Release(); Dispose(material); material=null; }
        static void Dispose(Object obj) { if(!obj) return; if(Application.isPlaying) Destroy(obj); else DestroyImmediate(obj); }
    }
}
