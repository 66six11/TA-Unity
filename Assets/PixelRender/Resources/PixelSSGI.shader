Shader "Hidden/TA/PixelSSGI"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off
        HLSLINCLUDE
        #include "UnityCG.cginc"
        sampler2D _AlbedoBuffer,_NormalBuffer,_DepthBuffer,_Radiance,_RawGI,_FilteredGI;
        float4x4 _Projection,_InverseProjection,_WorldToView;
        float4 _InformationSize,_GIStep;
        float _Radius,_Thickness,_Bias,_FilterSigma,_View;
        int _RayCount,_RaySteps;
        float2 SampleUV(float2 uv) { return (floor(uv*_InformationSize.xy+0.0001)+0.5)*_InformationSize.zw; }
        float4 Depth(float2 uv) { return tex2Dlod(_DepthBuffer,float4(SampleUV(uv),0,0)); }
        float3 Normal(float2 uv)
        {
            float3 n=tex2Dlod(_NormalBuffer,float4(SampleUV(uv),0,0)).xyz;
            n=mul((float3x3)_WorldToView,n);
            return n*rsqrt(max(dot(n,n),1e-8));
        }
        float3 Position(float2 uv,float eyeDepth)
        {
            float2 clip=SampleUV(uv)*2-1;
            #if UNITY_UV_STARTS_AT_TOP
                clip.y=-clip.y;
            #endif
            #if defined(UNITY_REVERSED_Z)
                float nearZ=1,farZ=0;
            #else
                float nearZ=UNITY_NEAR_CLIP_VALUE,farZ=1;
            #endif
            float4 a=mul(_InverseProjection,float4(clip,nearZ,1));
            float4 b=mul(_InverseProjection,float4(clip,farZ,1));
            float3 p=a.xyz/a.w,q=b.xyz/b.w;
            return p+(q-p)*((-eyeDepth-p.z)/(q.z-p.z));
        }
        float2 Project(float3 p)
        {
            float4 clip=mul(_Projection,float4(p,1));
            float2 xy=clip.xy/clip.w;
            #if UNITY_UV_STARTS_AT_TOP
                xy.y=-xy.y;
            #endif
            return xy*0.5+0.5;
        }
        float Hash(float2 p) { return frac(sin(dot(p,float2(12.9898,78.233)))*43758.5453); }
        ENDHLSL
        Pass
        {
            Name "TraceSingleBounce"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i):SV_Target
            {
                float4 receiver=Depth(i.uv);
                if(receiver.a<0.5) return 0;
                float3 n=Normal(i.uv),p=Position(i.uv,receiver.r);
                float3 tangent=normalize(cross(abs(n.z)<0.9 ? float3(0,0,1) : float3(0,1,0),n));
                float3 bitangent=cross(n,tangent);
                float rotation=Hash(floor(i.uv/_GIStep.xy))*6.2831853;
                float stepLength=_Radius/_RaySteps;
                float3 sum=0; float hits=0;
                [loop] for(int ray=0;ray<_RayCount;ray++)
                {
                    // Deterministic cosine-weighted hemisphere; no temporal jitter.
                    float u=(ray+0.5)/_RayCount;
                    float angle=rotation+ray*2.39996323;
                    float3 direction=tangent*(sqrt(u)*cos(angle))+bitangent*(sqrt(u)*sin(angle))+n*sqrt(1-u);
                    float3 origin=p+n*_Bias;
                    [loop] for(int step=1;step<=_RaySteps;step++)
                    {
                        float distance=step*stepLength;
                        float3 samplePoint=origin+direction*distance;
                        if(samplePoint.z>=-0.001) break;
                        float2 uv=Project(samplePoint);
                        if(any(uv<0) || any(uv>=1)) break;
                        float4 scene=Depth(uv);
                        if(scene.a<0.5) continue;
                        float delta=-samplePoint.z-scene.r;
                        if(delta<0) continue;
                        // First depth crossing blocks the ray even if radiance is zero.
                        if(delta<=_Thickness+stepLength*abs(direction.z) && distance>2*_Bias)
                        {
                            float3 hitPosition=Position(uv,scene.r);
                            if(length(hitPosition-p)>2*_Bias && dot(Normal(uv),-direction)>0.01)
                            {
                                sum+=max(tex2Dlod(_Radiance,float4(SampleUV(uv),0,0)).rgb,0);
                                hits++;
                            }
                        }
                        break;
                    }
                }
                // Lambert BRDF / pi cancels cosine-weighted PDF. No extra 1/r^2.
                float3 albedo=tex2Dlod(_AlbedoBuffer,float4(SampleUV(i.uv),0,0)).rgb;
                return float4(albedo*sum/_RayCount,hits/_RayCount);
            }
            ENDHLSL
        }
        Pass
        {
            Name "SpatialDenoise"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i):SV_Target
            {
                float4 center=Depth(i.uv);
                if(center.a<0.5) return 0;
                float3 n=Normal(i.uv); float4 sum=0; float weight=0;
                [unroll] for(int y=-1;y<=1;y++)
                [unroll] for(int x=-1;x<=1;x++)
                {
                    float2 uv=i.uv+float2(x,y)*_GIStep.xy;
                    if(any(uv<0) || any(uv>=1)) continue;
                    float4 other=Depth(uv);
                    if(other.a<0.5) continue;
                    float w=pow(saturate(dot(n,Normal(uv))),32)*exp(-abs(other.r-center.r)/_FilterSigma)/(1+x*x+y*y);
                    sum+=tex2Dlod(_RawGI,float4(uv,0,0))*w; weight+=w;
                }
                return sum/max(weight,1e-6);
            }
            ENDHLSL
        }
        Pass
        {
            Name "GIOnlyPresentation"
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i):SV_Target
            {
                if(_View>0.5 && _View<1.5) return float4(tex2D(_Radiance,SampleUV(i.uv)).rgb,1);
                float4 raw=tex2D(_RawGI,i.uv);
                if(_View>1.5 && _View<2.5) return float4(raw.aaa,1);
                if(_View>2.5) return float4(raw.rgb,1);
                return float4(tex2D(_FilteredGI,i.uv).rgb,1);
            }
            ENDHLSL
        }
    }
}

