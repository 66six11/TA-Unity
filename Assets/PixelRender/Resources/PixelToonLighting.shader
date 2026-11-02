Shader "Hidden/TA/PixelToonLighting"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off
        HLSLINCLUDE
        #include "UnityCG.cginc"
        sampler2D _AlbedoBuffer,_NormalBuffer,_DepthBuffer,_Emission,_DirectRadiance,_GI,_Connectivity,_AAConnectivity;
        float4 _InformationSize,_PixelStep,_Ambient,_Background,_OutlineTint;
        float4x4 _InverseProjection,_ViewToWorld,_WorldToView;
        float4 _OutputSize;
        float _AAThreshold,_AAStrength,_ConnectivityTolerance;
        float4 _LightPositions[16],_LightColors[16],_LightDirections[16],_LightSpots[16];
        float4 _LightMasks[16];
        int _LightCount,_Bands,_View;
        float _GIIntensity,_Exposure,_ToneMapping,_Outline,_SolidColor;
        float2 SampleUV(float2 uv) { return (floor(uv*_InformationSize.xy+.0001)+.5)*_InformationSize.zw; }
        float3 WorldPosition(float2 uv,float depth)
        {
            float2 clip=uv*2-1;
            #if UNITY_UV_STARTS_AT_TOP
                clip.y=-clip.y;
            #endif
            #if defined(UNITY_REVERSED_Z)
                float nearZ=1,farZ=0;
            #else
                float nearZ=UNITY_NEAR_CLIP_VALUE,farZ=1;
            #endif
            float4 a=mul(_InverseProjection,float4(clip,nearZ,1)),b=mul(_InverseProjection,float4(clip,farZ,1));
            float3 p=a.xyz/a.w,q=b.xyz/b.w;
            return mul(_ViewToWorld,float4(p+(q-p)*((-depth-p.z)/(q.z-p.z)),1)).xyz;
        }
        float3 Display(float3 color)
        {
            color=max(color*_Exposure,0);
            // Bounded HDR shoulder with exact black, applied after GI transport.
            return _ToneMapping>.5 ? color/(1+color) : color;
        }
        ENDHLSL
        Pass
        {
            Name "DeferredToonLighting"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i):SV_Target
            {
                float2 uv=SampleUV(i.uv); float4 depth=tex2D(_DepthBuffer,uv);
                if(depth.a<.5) return 0;
                float3 p=WorldPosition(uv,depth.r),n=normalize(tex2D(_NormalBuffer,uv).xyz),sum=0;
                uint layer=1u << (uint)round(depth.g);
                float connected=tex2D(_AAConnectivity,i.uv).g;
                [loop] for(int j=0;j<_LightCount;j++)
                {
                    uint mask=(uint)_LightMasks[j].x | ((uint)_LightMasks[j].y << 16);
                    if((mask&layer)==0) continue;
                    float3 l=_LightDirections[j].xyz; float attenuation=1;
                    if(_LightPositions[j].w>.5)
                    {
                        float3 delta=_LightPositions[j].xyz-p; float d2=max(dot(delta,delta),.0001);
                        l=delta*rsqrt(d2);
                        float fade=saturate(1-d2*_LightDirections[j].w);
                        attenuation=fade*fade/max(1,d2);
                        if(_LightSpots[j].z>.5) attenuation*=saturate((dot(l,_LightDirections[j].xyz)-_LightSpots[j].x)*_LightSpots[j].y);
                    }
                    float diffuse=saturate(dot(n,l));
                    float band=floor(diffuse*(_Bands-.0001))/(_Bands-1);
                    float singleLevel=1.0/(_Bands-1);
                    if(connected<_AAThreshold && band>singleLevel+.0001) band-=_AAStrength*singleLevel;
                    sum+=band*_LightColors[j].rgb*attenuation;
                }
                return float4(tex2D(_AlbedoBuffer,uv).rgb*sum+tex2D(_Emission,uv).rgb,1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "CompositeAndOutline"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment frag
            float3 Shade(float2 uv)
            {
                float2 sample=SampleUV(uv);
                float4 albedo=tex2D(_AlbedoBuffer,sample);
                if(albedo.a<.5) return _View==0 || _View==1 ? _Background.rgb : 0;
                float3 direct=tex2D(_DirectRadiance,sample).rgb;
                float3 indirect=tex2D(_GI,uv).rgb*_GIIntensity;
                if(_View==2) return Display(indirect);
                if(_View==3) return Display(direct);
                return Display(direct+(_View==0 ? indirect+albedo.rgb*_Ambient.rgb : 0));
            }
            float4 frag(v2f_img i):SV_Target
            {
                float3 color=Shade(i.uv);
                if(_View==0 && _Outline>.5)
                {
                    float4 links=tex2D(_Connectivity,i.uv); float3 ink=0; float weight=0;
                    [unroll] for(int k=0;k<4;k++)
                    {
                        uint bits=(uint)round(links[k]);
                        if((bits&1)==0 && (bits&2)==0)
                        {
                            float2 dir=k==0 ? float2(1,0) : k==1 ? float2(-1,0) : k==2 ? float2(0,1) : float2(0,-1);
                            float2 uv=i.uv+dir*_PixelStep.xy;
                            if(tex2D(_AlbedoBuffer,SampleUV(uv)).a>.5)
                            { ink+=_SolidColor>.5 ? _OutlineTint.rgb : Shade(uv)*_OutlineTint.rgb; weight++; }
                        }
                    }
                    if(weight>0) color=ink/weight;
                }
                return float4(color,1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "LocalConnectivityForAA"
            HLSLPROGRAM
            #pragma target 4.0
            #pragma vertex vert_img
            #pragma fragment frag
            bool Linked(float4 a,float4 b,float3 pa,float3 pb,float3 na,float3 nb)
            {
                if(a.a<.5 || b.a<.5) return a.a<.5 && b.a<.5;
                float3 delta=pb-pa;
                float za=abs(mul((float3x3)_WorldToView,na).z),zb=abs(mul((float3x3)_WorldToView,nb).z);
                return abs(dot(na,delta))/max(za,.05)<=_ConnectivityTolerance || abs(dot(nb,delta))/max(zb,.05)<=_ConnectivityTolerance;
            }
            float4 frag(v2f_img input):SV_Target
            {
                float2 centerUV=SampleUV(input.uv);
                float4 center=tex2Dlod(_DepthBuffer,float4(centerUV,0,0));
                if(center.a<.5) return 0;
                float3 centerP=WorldPosition(centerUV,center.r),centerN=tex2Dlod(_NormalBuffer,float4(centerUV,0,0)).xyz;
                int2 size=(int2)clamp(ceil(_InformationSize.xy/_OutputSize.xy),1,8);
                int count=size.x*size.y;
                float4 depths[64]; float3 points[64],normals[64]; int connected[64];
                float2 pixel=floor(input.uv*_OutputSize.xy);
                int seed=0;float nearest=1e10;
                [loop] for(int k=0;k<count;k++)
                {
                    int2 cell=int2(k%size.x,k/size.x);
                    float2 uv=SampleUV((pixel+(cell+.5)/size)*_OutputSize.zw);
                    depths[k]=tex2Dlod(_DepthBuffer,float4(uv,0,0));
                    points[k]=WorldPosition(uv,depths[k].r);
                    normals[k]=tex2Dlod(_NormalBuffer,float4(uv,0,0)).xyz;
                    connected[k]=0;
                    float distance=dot(uv-centerUV,uv-centerUV);
                    if(distance<nearest) { nearest=distance;seed=k; }
                }
                connected[seed]=Linked(center,depths[seed],centerP,points[seed],centerN,normals[seed]) ? 1 : 0;
                [loop] for(int iteration=0;iteration<count;iteration++)
                {
                    bool changed=false;
                    [loop] for(int k=0;k<count;k++)
                    {
                        if(connected[k]!=0) continue;
                        [unroll] for(int direction=0;direction<4;direction++)
                        {
                            int2 cell=int2(k%size.x,k/size.x);
                            cell+=direction==0 ? int2(1,0) : direction==1 ? int2(-1,0) : direction==2 ? int2(0,1) : int2(0,-1);
                            if(any(cell<0) || any(cell>=size)) continue;
                            int other=cell.y*size.x+cell.x;
                            if(connected[other]!=0 && Linked(depths[k],depths[other],points[k],points[other],normals[k],normals[other]))
                            { connected[k]=1;changed=true;break; }
                        }
                    }
                    if(!changed) break;
                }
                float total=0,visible=0;
                [loop] for(int k=0;k<count;k++)
                {
                    total+=connected[k];
                    visible+=(connected[k]!=0 || (depths[k].a>.5 && depths[k].r<center.r)) ? 1 : 0;
                }
                float ratio=visible/count;
                return float4(total/count,ratio,ratio<_AAThreshold ? 1 : 0,1);
            }
            ENDHLSL
        }
    }
}
