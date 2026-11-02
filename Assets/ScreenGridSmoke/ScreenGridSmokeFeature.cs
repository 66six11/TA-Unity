using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace TA.ScreenGridSmoke
{
    public sealed class ScreenGridSmokeFeature : ScriptableRendererFeature
    {
        public ComputeShader solver;
        public Shader smokeShader;
        Material material;
        SmokePass pass;
        public override void Create()
        {
            CoreUtils.Destroy(material);
            material = smokeShader ? CoreUtils.CreateEngineMaterial(smokeShader) : null;
            pass = new SmokePass(solver) { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents, requiresIntermediateTexture = true };
            pass.ConfigureInput(ScriptableRenderPassInput.Depth);
        }
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;
            if (!material || !solver || renderingData.cameraData.isPreviewCamera ||
                !camera.TryGetComponent<ScreenGridSmokeView>(out var view) || !view.isActiveAndEnabled ||
                !view.source || !view.source.sharedMesh) return;
            if ((camera.cullingMask & (1 << view.source.gameObject.layer)) == 0) return;
            pass.Setup(view, material);
            renderer.EnqueuePass(pass);
        }
        protected override void Dispose(bool disposing) => CoreUtils.Destroy(material);

        sealed class SmokePass : ScriptableRenderPass
        {
            readonly ComputeShader solver;
            readonly int[] kernels;
            readonly MaterialPropertyBlock compositeProperties = new MaterialPropertyBlock();
            ScreenGridSmokeView view;
            Material material;
            public SmokePass(ComputeShader compute)
            {
                solver = compute;
                if (!compute) return;
                string[] names = { "Clear", "AdvectVelocity", "Forces", "ComputeCurl", "Confinement", "ComputeDivergence", "Jacobi", "Project", "AdvectDye" };
                kernels = new int[names.Length];
                for (int i = 0; i < names.Length; i++) kernels[i] = compute.FindKernel(names[i]);
            }
            public void Setup(ScreenGridSmokeView settings, Material shaderMaterial) { view = settings; material = shaderMaterial; }
            sealed class MaskData
            {
                public Mesh mesh;
                public Matrix4x4 matrix;
                public Material material;
            }
            sealed class SimulationData
            {
                public ComputeShader solver;
                public int[] kernels;
                public TextureHandle v0, v1, dyeRead, dyeWrite, p0, p1, curl, divergence, projectedDivergence, mask;
                public int width, height, iterations;
                public bool reset;
                public float dt, time, dissipation;
                public Vector4 source, forces, flow, color;
            }
            sealed class CompositeData
            {
                public Material material;
                public MaterialPropertyBlock properties;
                public TextureHandle background, sceneDepth, dye, velocity, pressure, curl, mask;
                public Vector4 settings, texel;
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                if (resources.isActiveTargetBackBuffer || !resources.cameraDepthTexture.IsValid()) return;
                Camera camera = cameraData.camera;
                view.Prepare(camera, cameraData.cameraTargetDescriptor.width, cameraData.cameraTargetDescriptor.height);
                int width = view.Width, height = view.Height;
                var mask = graph.CreateTexture(new TextureDesc(width, height)
                {
                    name = "Grid smoke / projected source obstacle", colorFormat = GraphicsFormat.R8_UNorm,
                    clearBuffer = true, clearColor = Color.clear, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
                });
                using (var builder = graph.AddRasterRenderPass<MaskData>("Grid smoke: project model silhouette", out var data))
                {
                    data.mesh = view.source.sharedMesh; data.matrix = view.source.transform.localToWorldMatrix; data.material = material;
                    builder.SetRenderAttachment(mask, 0);
                    builder.SetRenderFunc(static (MaskData d, RasterGraphContext ctx) =>
                    {
                        for (int i = 0; i < d.mesh.subMeshCount; i++)
                            if (d.mesh.GetTopology(i) == MeshTopology.Triangles) ctx.cmd.DrawMesh(d.mesh, d.matrix, d.material, i, 0);
                    });
                }
                var v0 = graph.ImportTexture(view.Velocity[0]); var v1 = graph.ImportTexture(view.Velocity[1]);
                var dyeRead = graph.ImportTexture(view.Dye[view.CurrentDye]);
                var dyeWrite = graph.ImportTexture(view.Dye[1 - view.CurrentDye]);
                var p0 = graph.ImportTexture(view.Pressure[0]); var p1 = graph.ImportTexture(view.Pressure[1]);
                var curl = graph.ImportTexture(view.Curl); var divergence = graph.ImportTexture(view.Divergence);
                var projectedDivergence = graph.ImportTexture(view.ProjectedDivergence);
                float dt = view.GetDeltaTime();
                Vector3 center = view.source.transform.TransformPoint(view.source.sharedMesh.bounds.center);
                Vector3 uv = camera.WorldToViewportPoint(center);
                float sourceDepth = -camera.worldToCameraMatrix.MultiplyPoint(center).z;
                float radiusWS = view.source.sharedMesh.bounds.extents.magnitude * Mathf.Max(
                    Mathf.Abs(view.source.transform.lossyScale.x), Mathf.Abs(view.source.transform.lossyScale.y), Mathf.Abs(view.source.transform.lossyScale.z));
                float radiusUV = Mathf.Abs(camera.WorldToViewportPoint(center + camera.transform.up * radiusWS).y - uv.y) * 0.65f;
                if (SystemInfo.graphicsUVStartsAtTop) uv.y = 1 - uv.y;
                if (dt > 0 || view.NeedsReset)
                {
                    using var builder = graph.AddComputePass<SimulationData>("Grid smoke: advection / forces / pressure projection / dye", out var data);
                    data.solver = solver; data.kernels = kernels;
                    data.v0 = v0; data.v1 = v1; data.dyeRead = dyeRead; data.dyeWrite = dyeWrite;
                    data.p0 = p0; data.p1 = p1; data.curl = curl; data.divergence = divergence;
                    data.projectedDivergence = projectedDivergence; data.mask = mask;
                    data.width = width; data.height = height;
                    data.iterations = Mathf.Clamp(view.pressureIterations / 2 * 2, 20, 100);
                    data.reset = view.NeedsReset; data.dt = dt; view.SimulationTime += dt; data.time = view.SimulationTime;
                    data.dissipation = Mathf.Max(0, view.dissipation);
                    data.source = new Vector4(uv.x, uv.y, Mathf.Clamp(radiusUV, 0.05f, 0.35f), 0);
                    data.forces = new Vector4(Mathf.Max(0, view.emission), Mathf.Max(0, view.outwardForce), Mathf.Max(0, view.buoyancy), Mathf.Max(0, view.vorticity));
                    data.flow = new Vector4(view.wind.x, SystemInfo.graphicsUVStartsAtTop ? -view.wind.y : view.wind.y, Mathf.Max(0, view.vortexStrength), Mathf.Max(0, view.velocityDrag));
                    Color color = view.smokeColor.linear; data.color = new Vector4(color.r, color.g, color.b, 1);
                    foreach (var handle in new[] { v0, v1, dyeRead, dyeWrite, p0, p1, curl, divergence, projectedDivergence }) builder.UseTexture(handle, AccessFlags.ReadWrite);
                    builder.UseTexture(mask, AccessFlags.Read);
                    builder.AllowPassCulling(false); // Persistent histories are outputs even in mask/debug modes.
                    builder.SetRenderFunc(static (SimulationData d, ComputeGraphContext ctx) => ExecuteSimulation(d, ctx));
                    view.MarkResetRecorded();
                    if (dt > 0) { view.CurrentDye = 1 - view.CurrentDye; dyeRead = dyeWrite; }
                }

                var outputDesc = graph.GetTextureDesc(resources.activeColorTexture);
                outputDesc.name = "Grid smoke / camera composite"; outputDesc.clearBuffer = false; outputDesc.depthBufferBits = DepthBits.None;
                var output = graph.CreateTexture(outputDesc);
                using (var builder = graph.AddRasterRenderPass<CompositeData>("Grid smoke: composite advected dye", out var data))
                {
                    data.material = material; data.properties = compositeProperties;
                    data.background = resources.activeColorTexture; data.sceneDepth = resources.cameraDepthTexture;
                    data.dye = dyeRead; data.velocity = v0; data.pressure = p0; data.curl = curl; data.mask = mask;
                    data.settings = new Vector4((int)view.display, view.opacity, sourceDepth, height);
                    data.texel = new Vector4(1f / width, 1f / height, width, height);
                    foreach (var handle in new[] { data.background, data.sceneDepth, dyeRead, v0, p0, curl, mask }) builder.UseTexture(handle);
                    builder.SetRenderAttachment(output, 0);
                    builder.SetRenderFunc(static (CompositeData d, RasterGraphContext ctx) =>
                    {
                        var props = d.properties;
                        props.SetTexture("_Background", d.background); props.SetTexture("_SceneDepth", d.sceneDepth);
                        props.SetTexture("_Dye", d.dye); props.SetTexture("_Velocity", d.velocity);
                        props.SetTexture("_Pressure", d.pressure); props.SetTexture("_Curl", d.curl); props.SetTexture("_Mask", d.mask);
                        props.SetVector("_Settings", d.settings); props.SetVector("_GridTexel", d.texel);
                        ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, 1, MeshTopology.Triangles, 3, 1, props);
                    });
                }
                resources.cameraColor = output;
            }

            static void ExecuteSimulation(SimulationData d, ComputeGraphContext ctx)
            {
                var cmd = ctx.cmd;
                ComputeShader cs = d.solver;
                int gx = (d.width + 7) / 8, gy = (d.height + 7) / 8;
                void Bind(int k, string name, TextureHandle texture) => cmd.SetComputeTextureParam(cs, k, name, texture);
                void Dispatch(int k) => cmd.DispatchCompute(cs, k, gx, gy, 1);
                cmd.SetComputeIntParams(cs, "_Size", d.width, d.height);
                cmd.SetComputeFloatParam(cs, Shader.PropertyToID("_Dt"), d.dt);
                cmd.SetComputeFloatParam(cs, Shader.PropertyToID("_TimeValue"), d.time);
                cmd.SetComputeFloatParam(cs, Shader.PropertyToID("_Dissipation"), d.dissipation);
                cmd.SetComputeVectorParam(cs, Shader.PropertyToID("_Source"), d.source);
                cmd.SetComputeVectorParam(cs, Shader.PropertyToID("_Forces"), d.forces);
                cmd.SetComputeVectorParam(cs, Shader.PropertyToID("_Flow"), d.flow);
                cmd.SetComputeVectorParam(cs, Shader.PropertyToID("_DyeColor"), d.color);
                if (d.reset)
                {
                    int k = d.kernels[0];
                    Bind(k, "_CurlOut", d.curl);
                    for (int i = 0; i < 2; i++)
                    {
                        Bind(k, "_VelocityOut", i == 0 ? d.v0 : d.v1);
                        Bind(k, "_DyeOut", i == 0 ? d.dyeRead : d.dyeWrite);
                        Bind(k, "_PressureOut", i == 0 ? d.p0 : d.p1);
                        Bind(k, "_DivergenceOut", i == 0 ? d.divergence : d.projectedDivergence);
                        Dispatch(k);
                    }
                }
                if (d.dt <= 0) return;
                TextureHandle velocity = d.v0, temporaryVelocity = d.v1;
                void SwapVelocity() { (velocity, temporaryVelocity) = (temporaryVelocity, velocity); }
                int kernel = d.kernels[1];
                Bind(kernel, "_Mask", d.mask); Bind(kernel, "_VelocityRead", velocity); Bind(kernel, "_VelocityOut", temporaryVelocity); Dispatch(kernel); SwapVelocity();
                kernel = d.kernels[2];
                Bind(kernel, "_Mask", d.mask); Bind(kernel, "_DyeRead", d.dyeRead); Bind(kernel, "_VelocityRead", velocity); Bind(kernel, "_VelocityOut", temporaryVelocity); Dispatch(kernel); SwapVelocity();
                kernel = d.kernels[3];
                Bind(kernel, "_Mask", d.mask); Bind(kernel, "_VelocityRead", velocity); Bind(kernel, "_CurlOut", d.curl); Dispatch(kernel);
                kernel = d.kernels[4];
                Bind(kernel, "_Mask", d.mask); Bind(kernel, "_VelocityRead", velocity); Bind(kernel, "_VelocityOut", temporaryVelocity); Bind(kernel, "_CurlRead", d.curl); Dispatch(kernel); SwapVelocity();
                kernel = d.kernels[5];
                Bind(kernel, "_Mask", d.mask); Bind(kernel, "_VelocityRead", velocity); Bind(kernel, "_DivergenceOut", d.divergence); Dispatch(kernel);
                TextureHandle pressure = d.p0, temporaryPressure = d.p1;
                kernel = d.kernels[6];
                Bind(kernel, "_Mask", d.mask); Bind(kernel, "_DivergenceRead", d.divergence);
                for (int i = 0; i < d.iterations; i++)
                {
                    Bind(kernel, "_PressureRead", pressure); Bind(kernel, "_PressureOut", temporaryPressure); Dispatch(kernel);
                    (pressure, temporaryPressure) = (temporaryPressure, pressure);
                }
                kernel = d.kernels[7];
                Bind(kernel, "_Mask", d.mask); Bind(kernel, "_VelocityRead", velocity); Bind(kernel, "_VelocityOut", temporaryVelocity); Bind(kernel, "_PressureRead", pressure); Dispatch(kernel); SwapVelocity();
                // Four velocity swaps and an even pressure iteration count leave both in slot 0.
                kernel = d.kernels[5];
                Bind(kernel, "_Mask", d.mask); Bind(kernel, "_VelocityRead", velocity); Bind(kernel, "_DivergenceOut", d.projectedDivergence); Dispatch(kernel);
                kernel = d.kernels[8];
                Bind(kernel, "_Mask", d.mask); Bind(kernel, "_VelocityRead", velocity);
                Bind(kernel, "_DyeRead", d.dyeRead); Bind(kernel, "_DyeOut", d.dyeWrite); Dispatch(kernel);
            }
        }
    }
}
