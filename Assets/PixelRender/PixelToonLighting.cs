using UnityEngine;
using UnityEngine.Rendering;

namespace TA.PixelRender
{
    [ExecuteAlways, RequireComponent(typeof(PixelCamera))]
    public sealed class PixelToonLighting : MonoBehaviour
    {
        public enum LightingView { Composite, DirectOnly, GIOnly, SourceRadiance, Off }
        public LightingView view=LightingView.Composite;
        [Range(2,6)] public int lightBands=3;
        [Header("Connectivity artistic AA")]
        public bool connectivityAA=true;
        [Range(0,1)] public float aaThreshold=.875f;
        [Range(0,1)] public float aaStrength=1;
        public RenderTexture AAConnectivity { get; private set; }
        [ColorUsage(false,true)] public Color ambientColor=new Color(.12f,.16f,.23f);
        [Min(0)] public float ambientIntensity=.5f;
        [Min(0)] public float directIntensity=1;
        [Min(0)] public float giIntensity=1;
        [Min(.01f)] public float exposure=1;
        [Tooltip("Compress HDR for display only. GI always uses unclamped linear radiance.")]
        public bool toneMapping=true;
        public RenderTexture DirectRadiance { get; private set; }
        public int CapturedLightCount { get; private set; }
        public int OmittedLightCount { get; private set; }
        const int MaxLights=16;
        readonly Vector4[] positions=new Vector4[MaxLights],colors=new Vector4[MaxLights],directions=new Vector4[MaxLights],spots=new Vector4[MaxLights];
        readonly Vector4[] masks=new Vector4[MaxLights];
        Material material;
        void OnEnable()
        {
            var shader=Resources.Load<Shader>("PixelToonLighting");
            if(shader) material=new Material(shader) { hideFlags=HideFlags.HideAndDontSave };
            else Debug.LogError("PixelToonLighting shader missing",this);
        }
        public bool RecordLighting(CommandBuffer cmd,PixelCamera surface,Camera camera)
        {
            if(!material || view==LightingView.Off) return false;
            if(!DirectRadiance || !DirectRadiance.IsCreated() || DirectRadiance.width!=surface.Albedo.width || DirectRadiance.height!=surface.Albedo.height ||
                !AAConnectivity || AAConnectivity.width!=surface.Output.width || AAConnectivity.height!=surface.Output.height)
            {
                Release();
                DirectRadiance=new RenderTexture(surface.Albedo.width,surface.Albedo.height,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear)
                { name="Pixel toon direct + emission radiance",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave };
                DirectRadiance.Create();
                AAConnectivity=new RenderTexture(surface.Output.width,surface.Output.height,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear)
                { name="Pixel AA local connectivity",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave };
                AAConnectivity.Create();
            }
            CapturedLightCount=OmittedLightCount=0;
            foreach(var root in gameObject.scene.GetRootGameObjects()) foreach(var light in root.GetComponentsInChildren<Light>())
            {
                if(!light.isActiveAndEnabled || light.intensity<=0 || (light.cullingMask & surface.captureLayers.value)==0 ||
                    (light.type!=LightType.Directional && light.type!=LightType.Point && light.type!=LightType.Spot)) continue;
                if(CapturedLightCount==MaxLights) { OmittedLightCount++; continue; }
                int j=CapturedLightCount++;
                var p=light.transform.position; var d=-light.transform.forward;
                positions[j]=new Vector4(p.x,p.y,p.z,light.type==LightType.Directional ? 0 : 1);
                directions[j]=new Vector4(d.x,d.y,d.z,1/Mathf.Max(light.range*light.range,.0001f));
                Color color=light.color.linear*light.intensity*directIntensity;
                // Unity's correlated-temperature conversion already returns linear RGB.
                if(light.useColorTemperature) color*=Mathf.CorrelatedColorTemperatureToRGB(light.colorTemperature);
                colors[j]=new Vector4(color.r,color.g,color.b,0);
                uint mask=(uint)light.cullingMask;
                masks[j]=new Vector4(mask&65535u,mask>>16,0,0);
                float outer=Mathf.Cos(light.spotAngle*.5f*Mathf.Deg2Rad),inner=Mathf.Cos(light.innerSpotAngle*.5f*Mathf.Deg2Rad);
                spots[j]=new Vector4(outer,1/Mathf.Max(inner-outer,.0001f),light.type==LightType.Spot ? 1 : 0,0);
            }
            material.SetInt("_LightCount",CapturedLightCount); material.SetInt("_Bands",Mathf.Clamp(lightBands,2,6));
            material.SetVectorArray("_LightPositions",positions); material.SetVectorArray("_LightColors",colors);
            material.SetVectorArray("_LightDirections",directions); material.SetVectorArray("_LightSpots",spots); material.SetVectorArray("_LightMasks",masks);
            material.SetTexture("_AlbedoBuffer",surface.Albedo); material.SetTexture("_NormalBuffer",surface.WorldNormal);
            material.SetTexture("_DepthBuffer",surface.LinearDepth); material.SetTexture("_Emission",surface.Radiance);
            material.SetVector("_InformationSize",new Vector4(surface.Albedo.width,surface.Albedo.height,1f/surface.Albedo.width,1f/surface.Albedo.height));
            material.SetMatrix("_InverseProjection",GL.GetGPUProjectionMatrix(camera.projectionMatrix,true).inverse);
            material.SetMatrix("_ViewToWorld",camera.cameraToWorldMatrix);
            material.SetMatrix("_WorldToView",camera.worldToCameraMatrix);
            material.SetVector("_OutputSize",new Vector4(surface.Output.width,surface.Output.height,1f/surface.Output.width,1f/surface.Output.height));
            material.SetFloat("_AAThreshold",Mathf.Clamp01(aaThreshold));
            material.SetFloat("_AAStrength",connectivityAA ? Mathf.Clamp01(aaStrength) : 0);
            material.SetFloat("_ConnectivityTolerance",Mathf.Max(.001f,surface.connectivityThreshold));
            material.SetTexture("_AAConnectivity",AAConnectivity);
            if(connectivityAA && aaStrength>0) cmd.Blit(surface.LinearDepth,AAConnectivity,material,2);
            else { cmd.SetRenderTarget(AAConnectivity); cmd.ClearRenderTarget(false,true,Color.white); }
            cmd.Blit(surface.Albedo,DirectRadiance,material,0);
            return true;
        }
        public void RecordComposite(CommandBuffer cmd,PixelCamera surface,Camera camera,Texture gi)
        {
            material.SetTexture("_DirectRadiance",DirectRadiance); material.SetTexture("_GI",gi ? gi : Texture2D.blackTexture);
            material.SetTexture("_Connectivity",surface.Connectivity);
            material.SetVector("_PixelStep",new Vector4(1f/surface.Output.width,1f/surface.Output.height,0,0));
            material.SetColor("_Ambient",ambientColor.linear*ambientIntensity); material.SetColor("_Background",camera.backgroundColor.linear);
            material.SetColor("_OutlineTint",surface.outlineTint.linear);
            material.SetFloat("_SolidColor",surface.solidOutlineColor ? 1 : 0);
            material.SetFloat("_Outline",surface.outlineEnabled ? 1 : 0);
            material.SetFloat("_GIIntensity",Mathf.Max(0,giIntensity)); material.SetFloat("_Exposure",Mathf.Max(.01f,exposure));
            material.SetFloat("_ToneMapping",toneMapping ? 1 : 0); material.SetInt("_View",(int)view);
            // Buffer debug views retain priority over the final shaded presentation.
            if(surface.view==PixelCamera.BufferView.Outlined) cmd.Blit(DirectRadiance,surface.Output,material,1);
        }
        void Release()
        {
            foreach(var rt in new[]{DirectRadiance,AAConnectivity}) if(rt) { rt.Release(); Dispose(rt); }
            DirectRadiance=AAConnectivity=null;
        }
        void OnDisable() { Release(); Dispose(material); material=null; }
        static void Dispose(Object obj) { if(!obj) return; if(Application.isPlaying) Destroy(obj); else DestroyImmediate(obj); }
    }
}
