using UnityEngine;
using UnityEngine.Rendering;

namespace TA.PixelRender
{
    [ExecuteAlways, RequireComponent(typeof(Camera))]
    public sealed class PixelCamera : MonoBehaviour
    {
        public enum BufferView { Outlined, WorldNormal, LinearDepth, Coverage, Albedo, OutlineMask, Connectivity }
        [Range(32,1080)] public int informationHeight = 360;
        [Range(32,540)] public int verticalResolution = 180;
        public BufferView view;
        public LayerMask captureLayers = 1 << 29;
        [Header("Sloane-style connectivity outline")]
        public bool outlineEnabled = true;
        [Min(0.001f)] public float connectivityThreshold = 0.05f;
        public bool solidOutlineColor;
        public Color outlineTint = new Color(0.2f,0.2f,0.2f,1);
        public RenderTexture Output { get; private set; }
        public RenderTexture Albedo { get; private set; }
        public RenderTexture WorldNormal { get; private set; }
        public RenderTexture LinearDepth { get; private set; }
        public RenderTexture Radiance { get; private set; }
        public RenderTexture Connectivity { get; private set; }
        public int UnsupportedMaterials { get; private set; }
        CommandBuffer commands;
        Material preview;
        Material connectivityMaterial;
        int captureMask;
        float previousAspect;
        Camera source;
        Camera display;
        RenderTexture previous;
        void OnEnable()
        {
            source = GetComponent<Camera>(); previous = source.targetTexture;
            captureMask = source.cullingMask; previousAspect = source.aspect;
            source.cullingMask = 0;
            commands = new CommandBuffer { name = "Pixel surface information" };
            var shader = Resources.Load<Shader>("PixelBufferPreview");
            if(shader) preview = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            else Debug.LogError("Missing PixelBufferPreview shader",this);
            var connectivityShader=Resources.Load<Shader>("PixelConnectivityOutline");
            if(connectivityShader) connectivityMaterial=new Material(connectivityShader) { hideFlags=HideFlags.HideAndDontSave };
            else Debug.LogError("Missing PixelConnectivityOutline shader",this);
            var go = new GameObject("Pixel display") { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(transform, false);
            display = go.AddComponent<Camera>();
            display.cullingMask = 0;
            display.clearFlags = CameraClearFlags.SolidColor;
            display.backgroundColor = Color.black;
            display.depth = source.depth + 1;
            EnsureTargets();
            RenderPipelineManager.endCameraRendering += RenderInformation;
        }
        void LateUpdate() { EnsureTargets(); }
        public void EnsureTargets()
        {
            if(!source || !isActiveAndEnabled) return;
            int height = Mathf.Clamp(verticalResolution, 32, 540);
            int width = Mathf.RoundToInt(height * 16f / 9f);
            int infoH = Mathf.Clamp(informationHeight,32,1080), infoW = Mathf.RoundToInt(infoH*16f/9f);
            if (Output && Albedo && Output.IsCreated() && Albedo.IsCreated() && Output.width == width && Output.height == height && Albedo.height==infoH && Albedo.width==infoW) return;
            Release();
            Albedo = Target("Pixel Albedo",infoW,infoH,RenderTextureFormat.ARGBHalf,24);
            WorldNormal = Target("Pixel World Normal",infoW,infoH,RenderTextureFormat.ARGBHalf);
            LinearDepth = Target("Pixel Linear Depth",infoW,infoH,RenderTextureFormat.ARGBFloat);
            Radiance = Target("Pixel source radiance (emission)",infoW,infoH,RenderTextureFormat.ARGBHalf);
            Output = Target("Pixel output",width,height,RenderTextureFormat.ARGB32,24);
            Connectivity = Target("Pixel four-way connectivity",width,height,RenderTextureFormat.ARGBHalf);
            source.targetTexture = Output;
            source.aspect = 16f / 9f;
        }
        static RenderTexture Target(string label,int w,int h,RenderTextureFormat format,int depth=0)
        {
            var rt = new RenderTexture(w,h,depth,format,RenderTextureReadWrite.Linear) { name=label, filterMode=FilterMode.Point, wrapMode=TextureWrapMode.Clamp, antiAliasing=1, useMipMap=false, hideFlags=HideFlags.HideAndDontSave };
            rt.Create(); return rt;
        }
        void RenderInformation(ScriptableRenderContext context,Camera camera)
        {
            if(camera==source) RenderNow();
        }
        // Also used by the offline validator. No GPU readback in the frame loop.
        public void RenderNow()
        {
            if(!isActiveAndEnabled || commands==null) return;
            EnsureTargets();
            if(!preview || !Albedo || !Output) return;
            commands.Clear();
            commands.SetRenderTarget(new RenderTargetIdentifier[]{Albedo,WorldNormal,LinearDepth,Radiance},Albedo.depthBuffer);
            commands.SetViewport(new Rect(0,0,Albedo.width,Albedo.height));
            commands.ClearRenderTarget(true,true,Color.clear);
            commands.SetGlobalMatrix("_PixelCaptureVP",GL.GetGPUProjectionMatrix(source.projectionMatrix,true)*source.worldToCameraMatrix);
            commands.SetGlobalMatrix("_PixelCaptureView",source.worldToCameraMatrix);
            UnsupportedMaterials=0;
            foreach(var root in gameObject.scene.GetRootGameObjects())
            foreach(var renderer in root.GetComponentsInChildren<Renderer>())
            {
                if(!renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy || renderer.gameObject.scene!=gameObject.scene || (captureLayers.value & (1<<renderer.gameObject.layer))==0) continue;
                var materials=renderer.sharedMaterials;
                for(int i=0;i<materials.Length;i++)
                {
                    var material=materials[i]; if(!material) continue;
                    int pass=material.FindPass("PixelSurface");
                    if(pass<0) { UnsupportedMaterials++; continue; }
                    commands.SetGlobalFloat("_PixelCaptureLayer",renderer.gameObject.layer);
                    commands.DrawRenderer(renderer,material,i,pass);
                }
            }
            preview.SetTexture("_AlbedoBuffer",Albedo); preview.SetTexture("_NormalBuffer",WorldNormal); preview.SetTexture("_DepthBuffer",LinearDepth);
            preview.SetVector("_InformationSize",new Vector4(Albedo.width,Albedo.height,1f/Albedo.width,1f/Albedo.height));
            preview.SetFloat("_BufferView",view==BufferView.Albedo ? 0 : (int)view);
            preview.SetVector("_DepthRange",new Vector4(source.nearClipPlane,source.farClipPlane,0,0));
            preview.SetColor("_Background",source.backgroundColor.linear);
            if(connectivityMaterial)
            {
                connectivityMaterial.SetTexture("_AlbedoBuffer",Albedo);
                connectivityMaterial.SetTexture("_NormalBuffer",WorldNormal);
                connectivityMaterial.SetTexture("_DepthBuffer",LinearDepth);
                connectivityMaterial.SetTexture("_Connectivity",Connectivity);
                connectivityMaterial.SetVector("_InformationSize",new Vector4(Albedo.width,Albedo.height,1f/Albedo.width,1f/Albedo.height));
                connectivityMaterial.SetVector("_PixelStep",new Vector4(1f/Output.width,1f/Output.height,0,0));
                connectivityMaterial.SetInt("_ConnectivitySteps",Mathf.Clamp(Mathf.CeilToInt(Mathf.Max((float)Albedo.width/Output.width,(float)Albedo.height/Output.height)),1,64));
                connectivityMaterial.SetMatrix("_CaptureInverseProjection",GL.GetGPUProjectionMatrix(source.projectionMatrix,true).inverse);
                connectivityMaterial.SetMatrix("_CaptureWorldToView",source.worldToCameraMatrix);
                connectivityMaterial.SetFloat("_Threshold",Mathf.Max(0.001f,connectivityThreshold));
                connectivityMaterial.SetFloat("_Enabled",outlineEnabled ? 1 : 0);
                connectivityMaterial.SetFloat("_SolidColor",solidOutlineColor ? 1 : 0);
                connectivityMaterial.SetColor("_OutlineTint",outlineTint.linear);
                connectivityMaterial.SetColor("_Background",source.backgroundColor.linear);
                connectivityMaterial.SetFloat("_View",(int)view);
                commands.Blit(Albedo,Connectivity,connectivityMaterial,0);
                if(view==BufferView.Outlined || (int)view>=5) commands.Blit(Albedo,Output,connectivityMaterial,1);
                else commands.Blit(Albedo,Output,preview);
            }
            else commands.Blit(Albedo,Output,preview);
            var lighting=GetComponent<PixelToonLighting>();
            bool lit=lighting && lighting.isActiveAndEnabled && lighting.RecordLighting(commands,this,source);
            var gi=GetComponent<PixelScreenSpaceGI>();
            bool hasGI=gi && gi.isActiveAndEnabled && gi.view!=PixelScreenSpaceGI.GIView.Off;
            if(hasGI) gi.Record(commands,this,source,lit ? lighting.DirectRadiance : null,!lit);
            if(lit) lighting.RecordComposite(commands,this,source,hasGI ? gi.IndirectDiffuse : null);
            Graphics.ExecuteCommandBuffer(commands);
        }
        void OnGUI()
        {
            if (!Output) return;
            GUI.depth = 100;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.blackTexture);
            float scale = Mathf.Min((float)Screen.width / Output.width, (float)Screen.height / Output.height);
            if (scale >= 1) scale = Mathf.Floor(scale);
            float w = Output.width * scale, h = Output.height * scale;
            GUI.DrawTexture(new Rect((Screen.width-w)/2, (Screen.height-h)/2, w, h), Output, ScaleMode.StretchToFill, false);
        }
        void OnDisable()
        {
            RenderPipelineManager.endCameraRendering -= RenderInformation;
            if (source) { source.targetTexture = previous; source.cullingMask=captureMask; source.aspect=previousAspect; }
            Release();
            commands?.Release(); commands=null;
            Dispose(preview); preview=null;
            Dispose(connectivityMaterial); connectivityMaterial=null;
            if (display)
            {
                if (Application.isPlaying) Destroy(display.gameObject); else DestroyImmediate(display.gameObject);
            }
        }
        void Release()
        {
            if (source && source.targetTexture == Output) source.targetTexture = previous;
            ReleaseTarget(Output); ReleaseTarget(Albedo); ReleaseTarget(WorldNormal); ReleaseTarget(LinearDepth);
            ReleaseTarget(Radiance); Radiance=null;
            ReleaseTarget(Connectivity); Connectivity=null;
            Output = Albedo = WorldNormal = LinearDepth = null;
        }
        static void ReleaseTarget(RenderTexture rt) { if(rt) { rt.Release(); Dispose(rt); } }
        static void Dispose(Object obj) { if(!obj) return; if(Application.isPlaying) Destroy(obj); else DestroyImmediate(obj); }
    }
}
