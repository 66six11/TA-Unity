using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;

namespace TA.SdfOutline
{
    public sealed class SdfOutlineFeature : ScriptableRendererFeature
    {
        public ComputeShader distanceCompute;
        public Shader maskShader, compositeShader;
        public LayerMask targetLayers = 0;
        public Color outlineColor = new Color(1, 0.65f, 0.05f, 1);
        [Range(0, 64)] public float width = 4;
        [Range(0.5f, 4)] public float softness = 1;
        public enum Placement { Outside, Inside, Centered }
        public enum Display { Composite, Mask, SignedDistance, Seeds, DistanceBands, Original, PaperGrain, ContourLines, ProjectionCoordinate, SpikeDashes, SpikeShape }
        public Placement placement;
        public Display display;
        public Texture2D paperTexture;
        [Range(0, 1)] public float paperMidpoint = 0.6356f;
        Texture2D loadedPaper;
        Material maskMaterial, compositeMaterial;
        OutlinePass pass;
        public override void Create()
        {
            loadedPaper = paperTexture ? paperTexture : Resources.Load<Texture2D>("SdfPaperGrain");
            CoreUtils.Destroy(maskMaterial); CoreUtils.Destroy(compositeMaterial);
            maskMaterial = maskShader ? CoreUtils.CreateEngineMaterial(maskShader) : null;
            compositeMaterial = compositeShader ? CoreUtils.CreateEngineMaterial(compositeShader) : null;
            pass = new OutlinePass(this) { renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing, requiresIntermediateTexture = true };
            pass.ConfigureInput(ScriptableRenderPassInput.Depth);
        }
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
        {
            if (!distanceCompute || !maskMaterial || !compositeMaterial || !SystemInfo.supportsComputeShaders ||
                data.cameraData.isPreviewCamera || data.cameraData.xr.enabled || data.cameraData.renderType == CameraRenderType.Overlay || targetLayers.value == 0) return;
            renderer.EnqueuePass(pass);
        }
        protected override void Dispose(bool disposing) { CoreUtils.Destroy(maskMaterial); CoreUtils.Destroy(compositeMaterial); }
        sealed class OutlinePass : ScriptableRenderPass
        {
            readonly SdfOutlineFeature owner;
            public OutlinePass(SdfOutlineFeature owner) { this.owner = owner; }
            sealed class MaskData { public RendererListHandle renderers;public bool isolated;public Renderer target;public Material material;public int submeshes; }
            sealed class ComputeData
            {
                public ComputeShader shader; public int kernel, width, height, step;
                public TextureHandle mask, input, output;
            }
            sealed class BoundsData
            {
                public ComputeShader shader;public int kernel,width,height;public bool first;
                public TextureHandle input,output;public Vector4 axis;
            }
            sealed class CompositeData
            {
                public Material material; public TextureHandle background, mask, sdf, seeds;
                public Texture2D paper;
                public TextureHandle projectionBounds;
                public Color color, lineColor; public Vector4 settings, debug, paperSettings, lines, lineStyle, dashes, projectionAxis, spikes, spikePattern;
            }
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var camera = frameData.Get<UniversalCameraData>();
                if (resources.isActiveTargetBackBuffer || !resources.cameraDepthTexture.IsValid()) return;
                int w = camera.cameraTargetDescriptor.width, h = camera.cameraTargetDescriptor.height;
                TextureHandle Texture(string name, GraphicsFormat format, bool randomWrite = false) => graph.CreateTexture(new TextureDesc(w, h)
                { name = name, colorFormat = format, enableRandomWrite = randomWrite, clearBuffer = true, clearColor = Color.clear, filterMode = FilterMode.Point });
                var cameraControls=camera.camera.GetComponent<SdfOutlineControls>();
                var mask = Texture("SDF / visible silhouette", GraphicsFormat.R8_UNorm);
                var rendering = frameData.Get<UniversalRenderingData>();
                var drawing = RenderingUtils.CreateDrawingSettings(new ShaderTagId("UniversalForward"), rendering, camera,
                    frameData.Get<UniversalLightData>(), camera.defaultOpaqueSortFlags);
                drawing.SetShaderPassName(1, new ShaderTagId("UniversalForwardOnly"));
                drawing.SetShaderPassName(2, new ShaderTagId("SRPDefaultUnlit"));
                drawing.SetShaderPassName(3, new ShaderTagId("UniversalGBuffer"));
                drawing.overrideMaterial = owner.maskMaterial;
                var filtering = new FilteringSettings(RenderQueueRange.opaque, owner.targetLayers);
                var list = graph.CreateRendererList(new RendererListParams(rendering.cullResults, drawing, filtering));
                using (var builder = graph.AddRasterRenderPass<MaskData>("SDF: capture visible outline", out var data))
                {
                    data.renderers = list;
                    data.isolated=cameraControls&&cameraControls.isActiveAndEnabled&&cameraControls.singleConvexTarget;
                    data.target=data.isolated?cameraControls.convexTarget:null;data.material=owner.maskMaterial;data.submeshes=1;
                    if(data.target)
                    {
                        Mesh mesh=null;
                        if(data.target is SkinnedMeshRenderer skinned)mesh=skinned.sharedMesh;
                        else if(data.target.TryGetComponent<MeshFilter>(out var filter))mesh=filter.sharedMesh;
                        if(mesh)data.submeshes=mesh.subMeshCount;
                        if(!data.target.enabled||!data.target.gameObject.activeInHierarchy||(camera.camera.cullingMask&(1<<data.target.gameObject.layer))==0)data.target=null;
                    }
                    if(!data.isolated)builder.UseRendererList(list);
                    builder.UseTexture(resources.cameraDepthTexture);
                    builder.SetRenderAttachment(mask, 0);
                    builder.SetRenderFunc(static (MaskData d, RasterGraphContext ctx) =>
                    {
                        if(!d.isolated)ctx.cmd.DrawRendererList(d.renderers);
                        else if(d.target)for(int i=0;i<d.submeshes;i++)ctx.cmd.DrawRenderer(d.target,d.material,i,0);
                    });
                }
                float angle=cameraControls?cameraControls.projectionAngle*Mathf.Deg2Rad:0;
                var axis=new Vector4(Mathf.Cos(angle),Mathf.Sin(angle),-Mathf.Sin(angle),Mathf.Cos(angle));
                TextureHandle bounds=mask; int bw=w,bh=h;bool first=true;
                do
                {
                    int ow=(bw+7)/8,oh=(bh+7)/8;
                    var output=graph.CreateTexture(new TextureDesc(ow,oh){name="SDF / projection extent reduction",colorFormat=GraphicsFormat.R32G32_SFloat,enableRandomWrite=true});
                    using(var builder=graph.AddComputePass<BoundsData>("SDF: reduce projection extent",out var d))
                    {
                        d.shader=owner.distanceCompute;d.kernel=d.shader.FindKernel(first?"ProjectionBounds":"ReduceBounds");
                        d.input=bounds;d.output=output;d.width=bw;d.height=bh;d.first=first;d.axis=axis;
                        builder.UseTexture(bounds);builder.UseTexture(output,AccessFlags.Write);
                        builder.SetRenderFunc(static(BoundsData d,ComputeGraphContext ctx)=>
                        {
                            ctx.cmd.SetComputeVectorParam(d.shader,Shader.PropertyToID("_BoundsSize"),new Vector4(d.width,d.height,0,0));
                            ctx.cmd.SetComputeVectorParam(d.shader,Shader.PropertyToID("_ProjectionAxis"),d.axis);
                            ctx.cmd.SetComputeTextureParam(d.shader,d.kernel,d.first?"_Mask":"_BoundsInput",d.input);
                            ctx.cmd.SetComputeTextureParam(d.shader,d.kernel,"_BoundsOutput",d.output);
                            ctx.cmd.DispatchCompute(d.shader,d.kernel,(d.width+7)/8,(d.height+7)/8,1);
                        });
                    }
                    bounds=output;bw=ow;bh=oh;first=false;
                }while(bw>1||bh>1);
                var seed = Texture("SDF / boundary seeds", GraphicsFormat.R32G32_SFloat, true);
                Dispatch("Seed", mask, default, seed, 0);
                // Each jump writes a distinct graph resource: no in-place UAV read/write hazards.
                int step = Mathf.NextPowerOfTwo(Mathf.Max(w, h)) / 2;
                for (; step >= 1; step /= 2)
                {
                    var next = Texture("SDF / jump " + step, GraphicsFormat.R32G32_SFloat, true);
                    Dispatch("Jump", mask, seed, next, step); seed = next;
                }
                var refined = Texture("SDF / JFA+1 refinement", GraphicsFormat.R32G32_SFloat, true);
                Dispatch("Jump", mask, seed, refined, 1);
                var sdf = Texture("SDF / signed pixel distance", GraphicsFormat.R32_SFloat, true);
                Dispatch("Distance", mask, refined, sdf, 0);
                void Dispatch(string kernel, TextureHandle m, TextureHandle input, TextureHandle output, int jump)
                {
                    using var builder = graph.AddComputePass<ComputeData>("SDF: " + kernel + " " + jump, out var d);
                    d.shader = owner.distanceCompute; d.kernel = d.shader.FindKernel(kernel);
                    d.width = w; d.height = h; d.step = jump; d.mask = m; d.input = input; d.output = output;
                    if (kernel != "Jump") builder.UseTexture(m);
                    if (input.IsValid()) builder.UseTexture(input);
                    builder.UseTexture(output, AccessFlags.Write);
                    builder.SetRenderFunc(static (ComputeData d, ComputeGraphContext ctx) =>
                    {
                        var cmd = ctx.cmd; var cs = d.shader; int k = d.kernel;
                        cmd.SetComputeVectorParam(cs, Shader.PropertyToID("_Size"), new Vector4(d.width, d.height, 0, 0));
                        cmd.SetComputeIntParam(cs, Shader.PropertyToID("_Step"), d.step);
                        if (k != cs.FindKernel("Jump")) cmd.SetComputeTextureParam(cs, k, "_Mask", d.mask);
                        if (d.input.IsValid()) cmd.SetComputeTextureParam(cs, k, "_Seeds", d.input);
                        cmd.SetComputeTextureParam(cs, k, k == cs.FindKernel("Distance") ? "_DistanceOut" : "_SeedsOut", d.output);
                        cmd.DispatchCompute(cs, k, (d.width + 7) / 8, (d.height + 7) / 8, 1);
                    });
                }
                var desc = graph.GetTextureDesc(resources.activeColorTexture);
                desc.name = "SDF / outlined camera"; desc.clearBuffer = false; desc.depthBufferBits = DepthBits.None;
                var result = graph.CreateTexture(desc);
                using (var builder = graph.AddRasterRenderPass<CompositeData>("SDF: composite", out var d))
                {
                    d.material = owner.compositeMaterial; d.background = resources.activeColorTexture; d.mask = mask; d.sdf = sdf;
                                        var controls = camera.camera.GetComponent<SdfOutlineControls>();
                    bool controlled = controls && controls.isActiveAndEnabled;
                    d.color = (controlled ? controls.outlineColor : owner.outlineColor).linear;
                    d.paper = owner.loadedPaper ? owner.loadedPaper : Texture2D.grayTexture;
                    d.paperSettings = new Vector4(owner.loadedPaper && (!controlled || controls.paperEnabled) ? (controlled ? controls.paperStrength : 0.45f) : 0,
                        controlled ? controls.paperScale : 1, controlled ? controls.edgeRoughness : 0.8f, owner.paperMidpoint);
                    d.lines = new Vector4(controlled && controls.linesEnabled ? 1 : 0, controlled ? controls.lineSpacing : 9,
                        controlled ? controls.lineWidth : 1, controlled ? controls.lineRange : 64);
                    d.lineStyle = new Vector4(controlled ? (int)controls.lineRegion : 0, controlled ? controls.lineOpacity : 0.7f, 0, 0);
                    d.lineColor = controlled ? controls.lineColor.linear : Color.cyan;
                    d.projectionBounds=bounds;d.projectionAxis=axis;
                    d.dashes=new Vector4(controlled&&controls.dashesEnabled?1:0,controlled?controls.dashCount:20,controlled?controls.dashDuty:0.65f,controlled?controls.dashPhase:0);
                    d.spikes=new Vector4(controlled&&controls.spikeDashesEnabled?1:0,controlled?controls.spikeHeight:18,controlled?controls.spikeOffset:3,controlled?controls.spikeSeed:17);
                    d.spikePattern=new Vector4(controlled?controls.spikeCount:48,controlled?controls.spikePhase:0,controlled?controls.spikeRandomness:0.65f,0);
                    d.seeds = refined;
                    d.debug = new Vector4(controlled ? controls.distanceRange : 32, w, h, 0);
                    d.settings = new Vector4(controlled ? controls.width : owner.width, controlled ? controls.softness : owner.softness, (int)(controlled ? controls.placement : owner.placement), (int)(controlled ? controls.display : owner.display));
                    builder.UseTexture(d.background); builder.UseTexture(mask); builder.UseTexture(sdf); builder.UseTexture(refined); builder.UseTexture(bounds); builder.SetRenderAttachment(result, 0);
                    builder.SetRenderFunc(static (CompositeData d, RasterGraphContext ctx) =>
                    {
                        var p = new MaterialPropertyBlock();
                        p.SetTexture("_Background", d.background); p.SetTexture("_Mask", d.mask); p.SetTexture("_Sdf", d.sdf);
                        p.SetTexture("_ProjectionBounds",d.projectionBounds);p.SetVector("_ProjectionAxis",d.projectionAxis);p.SetVector("_Dashes",d.dashes);p.SetVector("_Spikes",d.spikes);p.SetVector("_SpikePattern",d.spikePattern);
                        p.SetVector("_ContourLines", d.lines); p.SetVector("_LineStyle", d.lineStyle); p.SetColor("_LineColor", d.lineColor);
                        p.SetTexture("_PaperGrain", d.paper); p.SetVector("_PaperSettings", d.paperSettings);
                        p.SetTexture("_Seeds", d.seeds); p.SetVector("_Debug", d.debug);
                        p.SetColor("_OutlineColor", d.color); p.SetVector("_Settings", d.settings);
                        ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, 0, MeshTopology.Triangles, 3, 1, p);
                    });
                }
                resources.cameraColor = result;
            }
        }
    }
}
