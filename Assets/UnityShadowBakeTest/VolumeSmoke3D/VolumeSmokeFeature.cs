using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace TA.VolumeSmoke3D
{
    public sealed class VolumeSmokeFeature:ScriptableRendererFeature
    {
        public Shader volumeShader;
        public Shader sourceCaptureShader;
        Material material,captureMaterial;
        VolumePass pass;
        public override void Create()
        {
            CoreUtils.Destroy(material);
            CoreUtils.Destroy(captureMaterial);
            material=volumeShader?CoreUtils.CreateEngineMaterial(volumeShader):null;
            Shader capture=sourceCaptureShader?sourceCaptureShader:Shader.Find("TA/Volume Source Capture");
            captureMaterial=capture?CoreUtils.CreateEngineMaterial(capture):null;
            pass=new VolumePass{renderPassEvent=RenderPassEvent.BeforeRenderingTransparents,requiresIntermediateTexture=true};
            pass.ConfigureInput(ScriptableRenderPassInput.Depth);
        }
        public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData renderingData)
        {
            Camera camera=renderingData.cameraData.camera;
            if(!material||renderingData.cameraData.isPreviewCamera||!camera.TryGetComponent<VolumeSmokeCamera>(out var view)||
                !view.isActiveAndEnabled||!view.simulation||!view.simulation.IsInitialized)return;
            if((camera.cullingMask&(1<<view.simulation.gameObject.layer))==0)return;
            pass.Setup(view,material,captureMaterial);renderer.EnqueuePass(pass);
        }
        protected override void Dispose(bool disposing){CoreUtils.Destroy(material);CoreUtils.Destroy(captureMaterial);}
        sealed class VolumePass:ScriptableRenderPass
        {
            VolumeSmokeCamera view;Material material,captureMaterial;
            readonly MaterialPropertyBlock properties=new MaterialPropertyBlock();
            readonly MaterialPropertyBlock captureProperties=new MaterialPropertyBlock();
            public void Setup(VolumeSmokeCamera settings,Material shaderMaterial,Material sourceMaterial){view=settings;material=shaderMaterial;captureMaterial=sourceMaterial;}
            sealed class CaptureData
            {
                public Mesh mesh;public Matrix4x4 matrix,previous,vp,view;
                public Material material;public MaterialPropertyBlock properties;public float delta;public int pass;
            }
            sealed class ReconstructData
            {
                public ComputeShader compute;public int kernel,size;
                public TextureHandle front,back,normal,motion,obstacle,source,sourceMotion;
                public Matrix4x4 vp,inverseVP,view;
                public Vector4 domain,parameters;public float maxSpeed;
            }
            sealed class PassData
            {
                public Material material;public MaterialPropertyBlock properties;
                public TextureHandle density,lighting,background,depth;
                public TextureHandle sourceNormal,sourceDepth,sourceMotion;
                public bool capture;
                public Vector4 sourceDebug;
                public Vector4 boundsMin,boundsMax,settings,color;
                public Vector4 volumeDebug,ambient;
                public TextureHandle obstacle,source,velocity,divergence;
            }
            sealed class LightingData
            {
                public ComputeShader compute;public int kernel,size,count,steps;
                public TextureHandle density,output;
                public Vector4 boundsMin,boundsSize,camera;
                public float extinction,anisotropy;
                public Vector4[] positions,colors,directions,cones;
            }
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frameData)
            {
                var resources=frameData.Get<UniversalResourceData>();
                if(resources.isActiveTargetBackBuffer||!resources.cameraDepthTexture.IsValid())return;
                var simulation=view.simulation;
                var cameraData=frameData.Get<UniversalCameraData>();
                Camera camera=cameraData.camera;
                Camera captureCamera=simulation.sourceCamera?simulation.sourceCamera:Camera.main;
                bool capture=captureMaterial&&simulation.source&&simulation.source.sharedMesh&&camera==captureCamera;
                TextureHandle front=default,normal=default,motion=default;
                if(capture)
                {
                    int height=Mathf.Clamp(view.captureHeight,64,1024);
                    int width=Mathf.Max(1,Mathf.RoundToInt(height*cameraData.cameraTargetDescriptor.width/(float)cameraData.cameraTargetDescriptor.height));
                    TextureHandle ColorTarget(string name,GraphicsFormat format)=>graph.CreateTexture(new TextureDesc(width,height)
                    {name=name,colorFormat=format,clearBuffer=true,clearColor=Color.clear,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp});
                    front=ColorTarget("Source / front device depth",GraphicsFormat.R32_SFloat);
                    normal=ColorTarget("Source / world normals and mask",GraphicsFormat.R16G16B16A16_SFloat);
                    motion=ColorTarget("Source / world surface motion",GraphicsFormat.R16G16B16A16_SFloat);
                    var back=ColorTarget("Source / farthest back eye depth",GraphicsFormat.R32_SFloat);
                    var depthAttachment=graph.CreateTexture(new TextureDesc(width,height)
                    {name="Source / closest surface Z",depthBufferBits=DepthBits.Depth32,clearBuffer=true});
                    Matrix4x4 captureView=cameraData.GetViewMatrix();
                    Matrix4x4 captureVP=GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(),true)*captureView;
                    Matrix4x4 matrix=simulation.source.transform.localToWorldMatrix;
                    float delta=view.validationCaptureDeltaTime>0?view.validationCaptureDeltaTime:
                        (Application.isPlaying?Mathf.Max(Time.deltaTime,0.0001f):1f/60f);
                    for(int i=0;i<2;i++)
                    {
                        using var builder=graph.AddRasterRenderPass<CaptureData>(i==0?"Smoke source: front mask / depth / normal / motion":"Smoke source: farthest back depth",out var data);
                        data.mesh=simulation.source.gameObject.activeInHierarchy?simulation.source.sharedMesh:null;data.matrix=matrix;data.previous=simulation.PreviousSourceMatrix;
                        data.vp=captureVP;data.view=captureView;data.material=captureMaterial;data.properties=captureProperties;data.delta=delta;data.pass=i;
                        builder.SetRenderAttachment(i==0?front:back,0);
                        if(i==0)
                        {
                            builder.SetRenderAttachment(normal,1);builder.SetRenderAttachment(motion,2);
                            builder.SetRenderAttachmentDepth(depthAttachment,AccessFlags.Write);
                        }
                        builder.SetRenderFunc(static(CaptureData d,RasterGraphContext ctx)=>
                        {
                            var props=d.properties;
                            if(!d.mesh)return;
                            props.SetMatrix("_PreviousSourceToWorld",d.previous);props.SetFloat("_CaptureDeltaTime",d.delta);
                            props.SetMatrix("_SourceCaptureVP",d.vp);props.SetMatrix("_SourceCaptureView",d.view);
                            for(int submesh=0;submesh<d.mesh.subMeshCount;submesh++)
                                if(d.mesh.GetTopology(submesh)==MeshTopology.Triangles)ctx.cmd.DrawMesh(d.mesh,d.matrix,d.material,submesh,d.pass,props);
                        });
                    }
                    using(var builder=graph.AddComputePass<ReconstructData>("Smoke source: reconstruct pixels into 3D source fields",out var data))
                    {
                        data.compute=simulation.SourceCompute;data.kernel=simulation.ScreenSourceKernel;data.size=simulation.AllocatedResolution;
                        data.front=front;data.back=back;data.normal=normal;data.motion=motion;
                        data.obstacle=graph.ImportTexture(simulation.Obstacle);data.source=graph.ImportTexture(simulation.SurfaceSource);data.sourceMotion=graph.ImportTexture(simulation.SurfaceMotion);
                        data.vp=captureVP;data.inverseVP=captureVP.inverse;data.view=captureView;
                        Bounds bounds=simulation.DomainBounds;data.domain=new Vector4(bounds.min.x,bounds.min.y,bounds.min.z,bounds.size.x);
                        bool gl=SystemInfo.graphicsDeviceType==GraphicsDeviceType.OpenGLCore||SystemInfo.graphicsDeviceType==GraphicsDeviceType.OpenGLES3;
                        data.parameters=new Vector4(SystemInfo.graphicsUVStartsAtTop?-1:1,gl?2:1,gl?-1:0,bounds.size.x/data.size*Mathf.Clamp(simulation.sourceBandCells,1,4));
                        data.maxSpeed=Mathf.Max(0.1f,simulation.maxSourceSpeed);
                        builder.UseTexture(front);builder.UseTexture(back);builder.UseTexture(normal);builder.UseTexture(motion);
                        builder.UseTexture(data.obstacle,AccessFlags.Write);builder.UseTexture(data.source,AccessFlags.Write);builder.UseTexture(data.sourceMotion,AccessFlags.Write);
                        builder.AllowPassCulling(false);
                        builder.SetRenderFunc(static(ReconstructData d,ComputeGraphContext ctx)=>
                        {
                            var cmd=ctx.cmd;int k=d.kernel;ComputeShader cs=d.compute;
                            cmd.SetComputeIntParam(cs,Shader.PropertyToID("_Size"),d.size);
                            cmd.SetComputeMatrixParam(cs,Shader.PropertyToID("_CaptureVP"),d.vp);cmd.SetComputeMatrixParam(cs,Shader.PropertyToID("_CaptureInverseVP"),d.inverseVP);
                            cmd.SetComputeMatrixParam(cs,Shader.PropertyToID("_CaptureView"),d.view);cmd.SetComputeVectorParam(cs,Shader.PropertyToID("_CaptureDomainMinSize"),d.domain);
                            cmd.SetComputeVectorParam(cs,Shader.PropertyToID("_CaptureParameters"),d.parameters);cmd.SetComputeFloatParam(cs,Shader.PropertyToID("_MaxSourceSpeed"),d.maxSpeed);
                            cmd.SetComputeTextureParam(cs,k,"_CapturedDepth",d.front);cmd.SetComputeTextureParam(cs,k,"_CapturedBackDepth",d.back);
                            cmd.SetComputeTextureParam(cs,k,"_CapturedNormal",d.normal);cmd.SetComputeTextureParam(cs,k,"_CapturedMotion",d.motion);
                            cmd.SetComputeTextureParam(cs,k,"_ObstacleOut",d.obstacle);cmd.SetComputeTextureParam(cs,k,"_SurfaceSourceOut",d.source);cmd.SetComputeTextureParam(cs,k,"_SurfaceMotionOut",d.sourceMotion);
                            int groups=(d.size+3)/4;cmd.DispatchCompute(cs,k,groups,groups,groups);
                        });
                    }
                    simulation.MarkSourceCaptureRecorded(matrix);
                }
                // A camera owns its transient irradiance volume. Recompute even while paused:
                // moving a light or changing opacity must not require advancing the fluid.
                TextureHandle sceneLighting;
                using(var builder=graph.AddComputePass<LightingData>("3D smoke: scene lights / attenuation / smoke shadow",out var data))
                {
                    data.count=0; // RenderGraph pools PassData instances across frames.
                    data.compute=simulation.SourceCompute;data.kernel=data.compute.FindKernel("SceneLighting");data.size=simulation.AllocatedResolution;
                    data.steps=Mathf.Clamp(view.lightSteps,4,32);data.extinction=Mathf.Max(0,view.extinction);data.anisotropy=Mathf.Clamp(view.anisotropy,-0.8f,0.8f);
                    Bounds bounds=simulation.DomainBounds;data.boundsMin=bounds.min;data.boundsSize=bounds.size;data.camera=camera.transform.position;
                    data.positions=new Vector4[8];data.colors=new Vector4[8];data.directions=new Vector4[8];data.cones=new Vector4[8];
                    var lights=frameData.Get<UniversalLightData>().visibleLights;
                    uint renderingMask=1; // Match source renderer when URP rendering layers are enabled.
                    if(simulation.source&&simulation.source.TryGetComponent<Renderer>(out var sourceRenderer))renderingMask=sourceRenderer.renderingLayerMask;
                    foreach(var visible in lights)
                    {
                        Light light=visible.light;
                        if(!light||!light.isActiveAndEnabled||(light.cullingMask&(1<<simulation.gameObject.layer))==0)continue;
                        if(light.type!=LightType.Directional&&light.type!=LightType.Point&&light.type!=LightType.Spot)continue;
                        if(frameData.Get<UniversalLightData>().supportsLightLayers&&
                            ((uint)light.GetUniversalAdditionalLightData().renderingLayers&renderingMask)==0)continue;
                        int index=data.count++;Vector3 position=visible.localToWorldMatrix.GetColumn(3),direction=visible.localToWorldMatrix.GetColumn(2);
                        data.positions[index]=new Vector4(position.x,position.y,position.z,light.type==LightType.Directional?0:light.type==LightType.Point?1:2);
                        Color color=visible.finalColor;data.colors[index]=new Vector4(color.r,color.g,color.b,1/Mathf.Max(0.0001f,visible.range*visible.range));
                        data.directions[index]=new Vector4(direction.x,direction.y,direction.z,0);
                        float outer=Mathf.Cos(visible.spotAngle*0.5f*Mathf.Deg2Rad),inner=Mathf.Cos(light.innerSpotAngle*0.5f*Mathf.Deg2Rad);
                        data.cones[index]=new Vector4(outer,1/Mathf.Max(0.001f,inner-outer),0,0);
                        if(data.count>=Mathf.Clamp(view.maxVolumeLights,1,8))break;
                    }
                    view.activeVolumeLights=data.count;
                    data.density=graph.ImportTexture(simulation.Density);
                    sceneLighting=data.output=graph.CreateTexture(new TextureDesc(data.size,data.size)
                    {name="Smoke / scene incident RGB and light T",dimension=TextureDimension.Tex3D,slices=data.size,colorFormat=GraphicsFormat.R16G16B16A16_SFloat,enableRandomWrite=true,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp});
                    builder.UseTexture(data.density);builder.UseTexture(data.output,AccessFlags.Write);
                    builder.SetRenderFunc(static(LightingData d,ComputeGraphContext context)=>
                    {
                        var cmd=context.cmd;var cs=d.compute;int k=d.kernel;
                        cmd.SetComputeIntParam(cs,Shader.PropertyToID("_Size"),d.size);
                        cmd.SetComputeIntParam(cs,Shader.PropertyToID("_VolumeLightCount"),d.count);cmd.SetComputeIntParam(cs,Shader.PropertyToID("_VolumeLightSteps"),d.steps);
                        cmd.SetComputeVectorArrayParam(cs,Shader.PropertyToID("_VolumeLightPosition"),d.positions);cmd.SetComputeVectorArrayParam(cs,Shader.PropertyToID("_VolumeLightColor"),d.colors);
                        cmd.SetComputeVectorArrayParam(cs,Shader.PropertyToID("_VolumeLightDirection"),d.directions);cmd.SetComputeVectorArrayParam(cs,Shader.PropertyToID("_VolumeLightCone"),d.cones);
                        cmd.SetComputeVectorParam(cs,Shader.PropertyToID("_LightingBoundsMin"),d.boundsMin);cmd.SetComputeVectorParam(cs,Shader.PropertyToID("_LightingBoundsSize"),d.boundsSize);
                        cmd.SetComputeVectorParam(cs,Shader.PropertyToID("_LightingCamera"),d.camera);
                        cmd.SetComputeFloatParam(cs,Shader.PropertyToID("_LightingExtinction"),d.extinction);cmd.SetComputeFloatParam(cs,Shader.PropertyToID("_LightingAnisotropy"),d.anisotropy);
                        cmd.SetComputeTextureParam(cs,k,"_DensityRead",d.density);cmd.SetComputeTextureParam(cs,k,"_SceneLightOut",d.output);
                        int groups=(d.size+3)/4;cmd.DispatchCompute(cs,k,groups,groups,groups);
                    });
                }
                var desc=graph.GetTextureDesc(resources.activeColorTexture);
                desc.name="3D smoke / raymarched camera color";desc.clearBuffer=false;desc.depthBufferBits=DepthBits.None;
                var output=graph.CreateTexture(desc);
                using(var builder=graph.AddRasterRenderPass<PassData>("3D smoke: raymarch world volume",out var data))
                {
                    data.material=material;data.properties=properties;
                    data.density=graph.ImportTexture(simulation.Density);data.lighting=sceneLighting;
                    data.obstacle=graph.ImportTexture(simulation.Obstacle);data.source=graph.ImportTexture(simulation.SurfaceSource);
                    data.velocity=graph.ImportTexture(simulation.Velocity);data.divergence=graph.ImportTexture(simulation.ProjectedDivergence);
                    data.volumeDebug=new Vector4((int)view.volumeDisplay,(int)view.sliceAxis,Mathf.Clamp01(view.slicePosition),Mathf.Max(0.1f,view.debugGain));
                    Color ambient=view.ambientLight.linear;data.ambient=new Vector4(ambient.r,ambient.g,ambient.b,Mathf.Max(0,view.scatteringAlbedo));
                    data.background=resources.activeColorTexture;data.depth=resources.cameraDepthTexture;
                    data.capture=capture;data.sourceNormal=normal;data.sourceDepth=front;data.sourceMotion=motion;
                    data.sourceDebug=new Vector4(capture?(int)view.sourceDisplay:0,0,0,0);
                    Bounds bounds=simulation.DomainBounds;data.boundsMin=bounds.min;data.boundsMax=bounds.max;
                    data.settings=new Vector4(view.extinction,Mathf.Clamp(view.raySteps,32,192),bounds.size.x/simulation.AllocatedResolution,0);
                    Color color=view.smokeColor.linear;data.color=new Vector4(color.r,color.g,color.b,1);
                    builder.UseTexture(data.density);builder.UseTexture(data.lighting);builder.UseTexture(data.background);builder.UseTexture(data.depth);
                    builder.UseTexture(data.obstacle);builder.UseTexture(data.source);builder.UseTexture(data.velocity);builder.UseTexture(data.divergence);
                    if(capture){builder.UseTexture(normal);builder.UseTexture(front);builder.UseTexture(motion);}
                    builder.SetRenderAttachment(output,0);
                    builder.SetRenderFunc(static(PassData d,RasterGraphContext context)=>
                    {
                        var props=d.properties;
                        props.SetTexture("_Density",d.density);props.SetTexture("_Lighting",d.lighting);
                        props.SetTexture("_Background",d.background);props.SetTexture("_SceneDepth",d.depth);
                        props.SetVector("_BoundsMin",d.boundsMin);props.SetVector("_BoundsMax",d.boundsMax);
                        props.SetVector("_VolumeSettings",d.settings);props.SetVector("_SmokeColor",d.color);
                        props.SetVector("_SourceDebug",d.sourceDebug);
                        props.SetVector("_VolumeDebug",d.volumeDebug);props.SetVector("_VolumeAmbient",d.ambient);
                        props.SetTexture("_VolumeObstacle",d.obstacle);props.SetTexture("_VolumeSource",d.source);
                        props.SetTexture("_VolumeVelocity",d.velocity);props.SetTexture("_VolumeDivergence",d.divergence);
                        if(d.capture)
                        {
                            props.SetTexture("_SourceNormal",d.sourceNormal);props.SetTexture("_SourceDepth",d.sourceDepth);props.SetTexture("_SourceMotion",d.sourceMotion);
                        }
                        context.cmd.DrawProcedural(Matrix4x4.identity,d.material,0,MeshTopology.Triangles,3,1,props);
                    });
                }
                resources.cameraColor=output;
            }
        }
    }
}
