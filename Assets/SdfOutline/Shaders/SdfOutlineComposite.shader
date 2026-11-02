Shader "TA/SDF Outline Composite"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            Texture2D<float4> _Background;
            Texture2D<float> _Mask, _Sdf;
            Texture2D<float2> _Seeds;
                        TEXTURE2D(_PaperGrain);
            SAMPLER(sampler_PaperGrain);
            Texture2D<float2> _ProjectionBounds;
            float4 _Dashes, _ProjectionAxis, _Spikes, _SpikePattern;
            float4 _ContourLines, _LineStyle, _LineColor;
            float4 _OutlineColor, _Settings, _Debug;
            float SampleDistance(float2 pixel)
            {
                float2 q=clamp(pixel,float2(0,0),_Debug.yz-1);
                int2 lo=(int2)floor(q),hi=min(lo+1,(int2)_Debug.yz-1);float2 f=frac(q);
                return lerp(lerp(_Sdf.Load(int3(lo,0)),_Sdf.Load(int3(hi.x,lo.y,0)),f.x),
                    lerp(_Sdf.Load(int3(lo.x,hi.y,0)),_Sdf.Load(int3(hi,0)),f.x),f.y);
            }
            float ProjectionU(int3 p)
            {
                float2 seed=_Seeds.Load(p);
                float2 limits=_ProjectionBounds.Load(int3(0,0,0));
                if(seed.x<0||limits.y<limits.x)return -1;
                // Wider, bilinear difference suppresses binary-mask and rounded-seed normal noise.
                const float radius=3;
                float2 normal=float2(SampleDistance(seed+float2(radius,0))-SampleDistance(seed-float2(radius,0)),
                    SampleDistance(seed+float2(0,radius))-SampleDistance(seed-float2(0,radius)));
                normal*=rsqrt(max(dot(normal,normal),1e-8));
                // A continuous approximate footpoint is shared by both dashes and spike geometry.
                float2 boundary=(float2)p.xy-_Sdf.Load(p)*normal;
                float pad=0.5*(abs(_ProjectionAxis.x)+abs(_ProjectionAxis.y));
                float v=saturate((dot(boundary,_ProjectionAxis.xy)-(limits.x-pad))/max(1,limits.y-limits.x+2*pad));
                return dot(normal,_ProjectionAxis.zw)>=0?0.5*v:1-0.5*v;
            }
            float DashCoverage(float u)
            {
                if(_Dashes.x<0.5)return 1;
                if(u<0||_Dashes.z<=0)return 0;
                if(_Dashes.z>=1)return 1;
                float count=max(1,round(_Dashes.y));
                float phase=frac((u+_Dashes.w)*count);
                float q=abs(phase-0.5)-_Dashes.z*0.5;
                // Differentiate U through sin/cos so the 0/1 seam does not inflate AA.
                float2 circular=float2(cos(u*6.2831853),sin(u*6.2831853));
                float aa=clamp((length(ddx(circular))+length(ddy(circular)))*count/6.2831853*0.5,0.005,0.2);
                return 1-smoothstep(-aa,aa,q);
            }
            float SpikeRandom(float id,float salt)
            {
                return frac(sin(id*127.1+_Spikes.w*31.7+salt*74.7)*43758.5453);
            }
            float SpikeField(float u,float distance)
            {
                float count=max(2,round(_SpikePattern.x));
                float z=frac(u+_SpikePattern.y)*count;
                float id=floor(z),local=frac(z);
                // Independent continuous spike pattern; the dash mask is applied only after shaping.
                float variation=saturate(_SpikePattern.z);
                float peak=lerp(0.5,lerp(0.3,0.7,SpikeRandom(id,1)),variation);
                float height=_Spikes.y*lerp(1,lerp(0.45,1,SpikeRandom(id,2)),variation);
                float ridge=local<peak?local/max(peak,0.01):(1-local)/max(1-peak,0.01);
                return distance-_Spikes.z-height*ridge;
            }
            float SpikeCoverage(float u,float d,float2 pixel)
            {
                float shapeU=max(0,u);
                float f=SpikeField(shapeU,d);
                // F is an implicit function, not a signed distance: correct thickness locally.
                float slope=clamp(length(float2(ddx(f),ddy(f))),1,12);
                float distance=abs(f)/slope;
                float coverage=1-smoothstep(max(0,_Settings.x*0.5-0.5),_Settings.x*0.5+0.5,distance);
                // Reject internal branch jumps rather than stretching them into horizontal spikes.
                float2 circularU=float2(cos(shapeU*6.2831853),sin(shapeU*6.2831853));
                float discontinuity=(length(ddx(circularU))+length(ddy(circularU)))/6.2831853;
                float continuity=1-smoothstep(0.02,0.08,discontinuity);
                return coverage*continuity*(u>=0&&d>=0&&_Settings.x>0?1:0);
            }
            float4 _PaperSettings;
            float ContourPattern(float d, float mask)
            {
                float spacing=max(3,_ContourLines.y);
                float radius=abs(d);
                // Level sets of the SDF run tangentially around the silhouette.
                float nearest=abs(frac(radius/spacing+0.5)-0.5)*spacing;
                float halfWidth=min(_ContourLines.z,spacing*0.65)*0.5;
                float aa=max(0.5,min(2,fwidth(d))*0.5);
                float coverage=1-smoothstep(halfWidth-aa,halfWidth+aa,nearest);
                float region=_LineStyle.x<0.5 ? 1-mask : _LineStyle.x<1.5 ? mask : 1;
                float range=max(8,_ContourLines.w);
                float fade=1-smoothstep(max(0,range-spacing),range,radius);
                return coverage*region*fade*smoothstep(1,3,radius);
            }
            struct Varyings { float4 positionCS:SV_POSITION; };
            Varyings Vert(uint id:SV_VertexID) { Varyings o; o.positionCS=GetFullScreenTriangleVertexPosition(id); return o; }
            float4 Frag(Varyings input):SV_Target
            {
                int3 p=int3(input.positionCS.xy,0);
                float d=_Sdf.Load(p), mask=_Mask.Load(p);
                if (_Settings.w==1) return float4(mask.xxx,1);
                if (_Settings.w==5) return _Background.Load(p);
                if (_Settings.w==3)
                {
                    float2 seed=_Seeds.Load(p);
                    return float4(seed.x<0 ? float3(0,0,0) : float3(seed / max(_Debug.yz,1),0),1);
                }
                if (_Settings.w==2 || _Settings.w==4)
                {
                    float band=1-saturate(abs(d)/max(_Debug.x,1));
                    float3 color=d<0 ? float3(0.1,0.45,1) : float3(1,0.25,0.05);
                                        if (_Settings.w==4)
                    {
                        float period=abs(frac(abs(d)/4+0.5)-0.5)*4;
                        float contour=1-smoothstep(0.25,0.8,period);
                        return float4(lerp(color*0.12,color,contour)*band,1);
                    }
                    return float4(color*band,1);
                }
                float u=ProjectionU(p);
                if(_Settings.w==8)
                {
                    float3 tint=lerp(float3(0.05,0.25,1),float3(1,0.2,0.05),saturate(u));
                    return float4(u<0?float3(0,0,0):tint*(1-saturate(abs(d)/16)),1);
                }
                float dashCoverage=DashCoverage(u);
                float rawSpikeCoverage=SpikeCoverage(u,d,input.positionCS.xy);
                float spikeCoverage=rawSpikeCoverage*dashCoverage;
                if(_Settings.w==10)return float4(rawSpikeCoverage.xxx,1);
                if(_Settings.w==9)return float4(spikeCoverage.xxx,1);
                float contourLines=ContourPattern(d,mask);
                if (_Settings.w==7) return float4(contourLines.xxx,1);
                // Screen-locked sampling avoids temporal swimming. One tile spans 512 rendered pixels at scale=1.
                float grain=SAMPLE_TEXTURE2D(_PaperGrain,sampler_PaperGrain,input.positionCS.xy/(512*max(0.25,_PaperSettings.y))).r;
                if (_Settings.w==6) return float4(grain.xxx,1);
                float noise=clamp((grain-_PaperSettings.w)*3,-1,1);
                d += noise*_PaperSettings.z*_PaperSettings.x;
                float width=max(0,_Settings.x), aa=max(0.5,_Settings.y);
                float distance=_Settings.z==1 ? -d : _Settings.z==2 ? abs(d) : d;
                float extent=_Settings.z==2 ? width*0.5 : width;
                float alpha=(1-smoothstep(extent-aa*0.5,extent+aa*0.5,distance));
                alpha *= _Settings.z==0 ? 1-mask : _Settings.z==1 ? mask : 1;
                alpha *= width>0 ? _OutlineColor.a*dashCoverage : 0;
                if(_Spikes.x>0.5)alpha=spikeCoverage*_OutlineColor.a;
                float4 background=_Background.Load(p);
                alpha *= 1-saturate(noise)*_PaperSettings.x*0.5;
                                float lineAlpha=contourLines*_ContourLines.x*saturate(_LineStyle.y)*_LineColor.a;
                float3 linedBackground=lerp(background.rgb,_LineColor.rgb,lineAlpha);
                float3 result=lerp(linedBackground,_OutlineColor.rgb,alpha);
                result=max(0,result + noise*_PaperSettings.x*(result*0.45+0.025));
                return float4(result,background.a);
            }
            ENDHLSL
        }
    }
}
