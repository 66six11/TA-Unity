Shader "TA/Volume Smoke 3D"
{
    SubShader
    {
        Tags{"RenderPipeline"="UniversalPipeline"}
        Pass
        {
            Name "WorldVolumeRaymarch"
            Cull Off ZWrite Off ZTest Always
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE3D(_Density);TEXTURE3D(_Lighting);
            TEXTURE2D(_Background);TEXTURE2D(_SceneDepth);
            TEXTURE2D(_SourceNormal);TEXTURE2D(_SourceDepth);TEXTURE2D(_SourceMotion);
            float4 _SourceDebug;
            TEXTURE3D(_VolumeObstacle);TEXTURE3D(_VolumeSource);TEXTURE3D(_VolumeVelocity);TEXTURE3D(_VolumeDivergence);
            float4 _VolumeDebug,_VolumeAmbient;
            float4 _BoundsMin,_BoundsMax,_VolumeSettings,_SmokeColor;
            struct Varyings{float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;};
            Varyings Vert(uint id:SV_VertexID)
            {
                Varyings o;o.positionCS=GetFullScreenTriangleVertexPosition(id);o.uv=GetFullScreenTriangleTexCoord(id);return o;
            }
            float4 Frag(Varyings i):SV_Target
            {
                if(_SourceDebug.x>0.5)
                {
                    float4 normal=SAMPLE_TEXTURE2D_LOD(_SourceNormal,sampler_PointClamp,i.uv,0);
                    int mode=(int)_SourceDebug.x;
                    if(mode==1)return float4(normal.aaa,1);
                    if(normal.a<0.5)return float4(0.01,0.01,0.01,1);
                    if(mode==2)
                    {
                        float depth=SAMPLE_TEXTURE2D_LOD(_SourceDepth,sampler_PointClamp,i.uv,0).r;
                        float eye=LinearEyeDepth(depth,_ZBufferParams);
                        return float4((1-saturate(eye/15)).xxx,1);
                    }
                    if(mode==3)return float4(normal.xyz*0.5+0.5,1);
                    float3 motion=SAMPLE_TEXTURE2D_LOD(_SourceMotion,sampler_PointClamp,i.uv,0).xyz;
                    return float4(saturate(abs(motion)*0.1),1);
                }
                float3 background=SAMPLE_TEXTURE2D_LOD(_Background,sampler_LinearClamp,i.uv,0).rgb;
                int display=(int)_VolumeDebug.x;
                if(display>=4)
                {
                    // X slice displays YZ; Y displays XZ; Z displays XY. Slice is world-axis aligned.
                    float2 plane=float2(i.uv.x,i.uv.y);
                    float3 uvw=_VolumeDebug.y<0.5?float3(_VolumeDebug.z,plane.x,plane.y):
                        _VolumeDebug.y<1.5?float3(plane.x,_VolumeDebug.z,plane.y):float3(plane,_VolumeDebug.z);
                    float gain=_VolumeDebug.w;
                    if(display==4){float d=SAMPLE_TEXTURE3D_LOD(_Density,sampler_LinearClamp,uvw,0).r;return float4((1-exp(-d*gain)).xxx,1);}
                    float4 light=SAMPLE_TEXTURE3D_LOD(_Lighting,sampler_LinearClamp,uvw,0);
                    if(display==5)return float4(1-exp(-light.rgb*gain),1);
                    if(display==6)return float4(light.aaa,1);
                    if(display==7)return float4(SAMPLE_TEXTURE3D_LOD(_VolumeObstacle,sampler_PointClamp,uvw,0).rrr,1);
                    if(display==8){float4 s=SAMPLE_TEXTURE3D_LOD(_VolumeSource,sampler_LinearClamp,uvw,0);return float4((s.xyz*0.5+0.5)*s.a,1);}
                    if(display==9){float3 v=SAMPLE_TEXTURE3D_LOD(_VolumeVelocity,sampler_LinearClamp,uvw,0).xyz*_VolumeSettings.z;return float4(1-exp(-abs(v)*gain),1);}
                    float divergence=SAMPLE_TEXTURE3D_LOD(_VolumeDivergence,sampler_LinearClamp,uvw,0).r;
                    return float4(saturate(divergence*gain),0,saturate(-divergence*gain),1);
                }
                float raw=SAMPLE_TEXTURE2D_LOD(_SceneDepth,sampler_PointClamp,i.uv,0).r;
                #if !UNITY_REVERSED_Z
                raw=lerp(UNITY_NEAR_CLIP_VALUE,1,raw);
                #endif
                float3 scenePosition=ComputeWorldSpacePosition(i.uv,raw,UNITY_MATRIX_I_VP);
                float3 origin=_WorldSpaceCameraPos;
                float3 direction=normalize(scenePosition-origin);
                float sceneDistance=length(scenePosition-origin);
                // Safe signed reciprocals, including camera rays parallel to a volume face.
                float3 inverseDirection=float3(
                    (direction.x>=0?1:-1)/max(abs(direction.x),1e-7),
                    (direction.y>=0?1:-1)/max(abs(direction.y),1e-7),
                    (direction.z>=0?1:-1)/max(abs(direction.z),1e-7));
                float3 a=(_BoundsMin.xyz-origin)*inverseDirection;
                float3 b=(_BoundsMax.xyz-origin)*inverseDirection;
                float3 entry=min(a,b),exit=max(a,b);
                float start=max(0,max(entry.x,max(entry.y,entry.z)));
                float end=min(sceneDistance,min(exit.x,min(exit.y,exit.z)));
                if(end<=start)return float4(display==2?float3(1,1,1):display>0?float3(0,0,0):background,1);
                int count=(int)_VolumeSettings.y;
                float step=(end-start)/count;
                float transmittance=1;float3 scattering=0;float integratedDensity=0;
                [loop]for(int j=0;j<count;j++)
                {
                    float3 world=origin+direction*(start+(j+0.5)*step);
                    float3 uvw=(world-_BoundsMin.xyz)/(_BoundsMax.xyz-_BoundsMin.xyz);
                    float density=max(0,SAMPLE_TEXTURE3D_LOD(_Density,sampler_LinearClamp,uvw,0).r);
                    integratedDensity+=density*step;
                    if(density>0.0001)
                    {
                        float3 light=SAMPLE_TEXTURE3D_LOD(_Lighting,sampler_LinearClamp,uvw,0).rgb;
                        float stepTransmittance=exp(-density*_VolumeSettings.x*step);
                        float3 illumination=(_VolumeAmbient.rgb+light)*_VolumeAmbient.w;
                        scattering+=transmittance*(1-stepTransmittance)*_SmokeColor.rgb*illumination;
                        transmittance*=stepTransmittance;
                        if(transmittance<0.01&&display!=1)break;
                    }
                }
                if(display==1)return float4((1-exp(-integratedDensity*_VolumeDebug.w)).xxx,1);
                if(display==2)return float4(transmittance.xxx,1);
                if(display==3)return float4(scattering,1);
                return float4(scattering+background*transmittance,1);
            }
            ENDHLSL
        }
    }
}
