using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace TA.ScreenSpaceFluid
{
    public sealed class ScreenSpaceFluidFeature : ScriptableRendererFeature
    {
        public Shader fluidShader;
        Material material;
        FluidPass pass;

        public override void Create()
        {
            CoreUtils.Destroy(material);
            material = fluidShader ? CoreUtils.CreateEngineMaterial(fluidShader) : null;
            pass = new FluidPass { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents, requiresIntermediateTexture = true };
            pass.ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var camera = renderingData.cameraData.camera;
            if (!material || renderingData.cameraData.isPreviewCamera || !camera.TryGetComponent<ScreenSpaceFluidView>(out var view) ||
                !view.isActiveAndEnabled || !view.emitter || !view.emitter.IsInitialized) return;
            if ((camera.cullingMask & (1 << view.emitter.gameObject.layer)) == 0) return;
            pass.Setup(view, material);
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing) => CoreUtils.Destroy(material);

        sealed class FluidPass : ScriptableRenderPass
        {
            ScreenSpaceFluidView view;
            Material material;
            readonly MaterialPropertyBlock[] particleProperties = { new MaterialPropertyBlock(), new MaterialPropertyBlock() };
            readonly MaterialPropertyBlock[] filterProperties = new MaterialPropertyBlock[10];
            readonly MaterialPropertyBlock compositeProperties = new MaterialPropertyBlock();
            public FluidPass()
            {
                for (int i = 0; i < filterProperties.Length; i++) filterProperties[i] = new MaterialPropertyBlock();
            }
            public void Setup(ScreenSpaceFluidView settings, Material shaderMaterial) { view = settings; material = shaderMaterial; }

            sealed class ParticleData
            {
                public Material material;
                public GraphicsBuffer particles;
                public TextureHandle sceneDepth;
                public int count, shaderPass;
                public Vector4 viewport;
                public MaterialPropertyBlock properties;
            }
            sealed class FilterData
            {
                public Material material;
                public TextureHandle depth;
                public Vector4 filter;
                public MaterialPropertyBlock properties;
            }
            sealed class CompositeData
            {
                public Material material;
                public TextureHandle background, sceneDepth, raw, smooth, thickness;
                public Vector4 viewport, settings, absorption, water;
                public MaterialPropertyBlock properties;
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var camera = frameData.Get<UniversalCameraData>();
                if (resources.isActiveTargetBackBuffer || !resources.cameraDepthTexture.IsValid()) return;
                int width = camera.cameraTargetDescriptor.width, height = camera.cameraTargetDescriptor.height;
                var viewport = new Vector4(1f / width, 1f / height, width, height);
                TextureHandle MakeTexture(string name, GraphicsFormat format, Color clear)
                {
                    var desc = new TextureDesc(width, height)
                    {
                        name = name, colorFormat = format, clearBuffer = true, clearColor = clear,
                        filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
                        msaaSamples = MSAASamples.None
                    };
                    return graph.CreateTexture(desc);
                }
                var raw = MakeTexture("Fluid / raw sphere depth", GraphicsFormat.R32_SFloat, Color.white * 1000000);
                var thickness = MakeTexture("Fluid / accumulated thickness", GraphicsFormat.R16_SFloat, Color.clear);
                var particleBuffer = graph.ImportBuffer(view.emitter.ParticleBuffer);

                for (int i = 0; i < 2; i++)
                {
                    using var builder = graph.AddRasterRenderPass<ParticleData>(i == 0 ? "Fluid: sphere depth" : "Fluid: thickness", out var data);
                    data.material = material; data.particles = view.emitter.ParticleBuffer;
                    data.sceneDepth = resources.cameraDepthTexture;
                    data.count = view.emitter.AllocatedCount; data.shaderPass = i; data.viewport = viewport;
                    data.properties = particleProperties[i];
                    builder.UseBuffer(particleBuffer, AccessFlags.Read);
                    builder.UseTexture(data.sceneDepth, AccessFlags.Read);
                    builder.SetRenderAttachment(i == 0 ? raw : thickness, 0);
                    builder.SetRenderFunc(static (ParticleData d, RasterGraphContext context) =>
                    {
                        var props = d.properties;
                        props.SetBuffer("_Particles", d.particles);
                        props.SetTexture("_SceneDepth", d.sceneDepth);
                        props.SetVector("_FluidViewport", d.viewport);
                        context.cmd.DrawProcedural(Matrix4x4.identity, d.material, d.shaderPass, MeshTopology.Triangles, 6, d.count, props);
                    });
                }

                TextureHandle smooth = raw;
                int iterations = Mathf.Clamp(view.smoothingIterations, 1, 5);
                for (int i = 0; i < iterations * 2; i++)
                {
                    var destination = MakeTexture("Fluid / bilateral " + i, GraphicsFormat.R32_SFloat, Color.white * 1000000);
                    using var builder = graph.AddRasterRenderPass<FilterData>(i % 2 == 0 ? "Fluid: bilateral X" : "Fluid: bilateral Y", out var data);
                    data.material = material; data.depth = smooth;
                    data.properties = filterProperties[i];
                    float stride = Mathf.Clamp(view.filterRadius, 1, 8) / 4;
                    data.filter = new Vector4(i % 2 == 0 ? stride / width : 0,
                        i % 2 == 1 ? stride / height : 0, Mathf.Max(0.01f, view.depthSigma), 0);
                    builder.UseTexture(smooth, AccessFlags.Read);
                    builder.SetRenderAttachment(destination, 0);
                    builder.SetRenderFunc(static (FilterData d, RasterGraphContext context) =>
                    {
                        var props = d.properties;
                        props.SetTexture("_FluidDepth", d.depth);
                        props.SetVector("_Filter", d.filter);
                        context.cmd.DrawProcedural(Matrix4x4.identity, d.material, 2, MeshTopology.Triangles, 3, 1, props);
                    });
                    smooth = destination;
                }

                var outputDesc = graph.GetTextureDesc(resources.activeColorTexture);
                outputDesc.name = "Fluid / composite camera color";
                outputDesc.clearBuffer = false;
                outputDesc.depthBufferBits = DepthBits.None;
                var output = graph.CreateTexture(outputDesc);
                using (var builder = graph.AddRasterRenderPass<CompositeData>("Fluid: reconstruct normals and shade", out var data))
                {
                    data.material = material;
                    data.properties = compositeProperties;
                    data.background = resources.activeColorTexture; data.sceneDepth = resources.cameraDepthTexture;
                    data.raw = raw; data.smooth = smooth; data.thickness = thickness; data.viewport = viewport;
                    data.settings = new Vector4(view.thicknessScale, view.refraction, (int)view.display, camera.camera.farClipPlane);
                    data.absorption = new Vector4(view.absorption.x, view.absorption.y, view.absorption.z, 0);
                    Color water = view.waterColor.linear;
                    data.water = new Vector4(water.r, water.g, water.b, 1);
                    builder.UseTexture(data.background); builder.UseTexture(data.sceneDepth);
                    builder.UseTexture(raw); builder.UseTexture(smooth); builder.UseTexture(thickness);
                    builder.SetRenderAttachment(output, 0);
                    builder.SetRenderFunc(static (CompositeData d, RasterGraphContext context) =>
                    {
                        var props = d.properties;
                        props.SetTexture("_Background", d.background); props.SetTexture("_SceneDepth", d.sceneDepth);
                        props.SetTexture("_RawDepth", d.raw); props.SetTexture("_FluidDepth", d.smooth);
                        props.SetTexture("_Thickness", d.thickness); props.SetVector("_FluidViewport", d.viewport);
                        props.SetVector("_FluidSettings", d.settings); props.SetVector("_Absorption", d.absorption);
                        props.SetVector("_WaterColor", d.water);
                        context.cmd.DrawProcedural(Matrix4x4.identity, d.material, 3, MeshTopology.Triangles, 3, 1, props);
                    });
                }
                resources.cameraColor = output;
            }
        }
    }
}
