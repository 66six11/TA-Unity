// Connectivity and far-side outline adapted from SloanePixelartURP.
// Copyright (c) 2024 Sloane. MIT; see ../LICENSE.Sloane.txt.
Shader "Hidden/TA/PixelConnectivityOutline"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off
        HLSLINCLUDE
        #include "UnityCG.cginc"
        sampler2D _AlbedoBuffer,_NormalBuffer,_DepthBuffer,_Connectivity;
        float4 _InformationSize,_PixelStep,_Background,_OutlineTint;
        float4x4 _CaptureInverseProjection,_CaptureWorldToView;
        float _Threshold,_Enabled,_SolidColor,_View;
        int _ConnectivitySteps;
        float2 Direction(int k)
        {
            return k==0 ? float2(1,0) : k==1 ? float2(-1,0) : k==2 ? float2(0,1) : float2(0,-1);
        }
        float2 SampleUV(float2 uv) { return (floor(uv*_InformationSize.xy+0.0001)+0.5)*_InformationSize.zw; }
        float4 Depth(float2 uv) { return tex2Dlod(_DepthBuffer,float4(SampleUV(uv),0,0)); }
        float3 Position(float2 uv,float depth)
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
            float4 a=mul(_CaptureInverseProjection,float4(clip,nearZ,1));
            float4 b=mul(_CaptureInverseProjection,float4(clip,farZ,1));
            float3 p=a.xyz/a.w,q=b.xyz/b.w;
            return p+(q-p)*((-depth-p.z)/(q.z-p.z));
        }
        float3 ViewNormal(float2 uv)
        {
            float3 n=tex2Dlod(_NormalBuffer,float4(SampleUV(uv),0,0)).xyz;
            n=mul((float3x3)_CaptureWorldToView,n);
            return n*rsqrt(max(dot(n,n),1e-8));
        }
        bool PairConnected(float2 a,float2 b)
        {
            float4 da=Depth(a),db=Depth(b);
            if(da.a<0.5 && db.a<0.5) return true;
            if((da.a<0.5)!=(db.a<0.5)) return false;
            float3 delta=Position(b,db.r)-Position(a,da.r);
            float3 na=ViewNormal(a),nb=ViewNormal(b);
            // Equivalent to predicting neighbor eye depth from each endpoint's
            // tangent plane. Disconnect only when BOTH predictions fail.
            float errorA=abs(dot(na,delta))/max(abs(na.z),0.05);
            float errorB=abs(dot(nb,delta))/max(abs(nb.z),0.05);
            return errorA<=_Threshold || errorB<=_Threshold;
        }
        float Connection(float2 center,float2 target)
        {
            if(any(target<0) || any(target>=1)) return 3;
            float4 a=Depth(center),b=Depth(target);
            float da=a.a>0.5 ? a.r : 1e6;
            float db=b.a>0.5 ? b.r : 1e6;
            float closer=da<=db ? 1 : 0;
            float connected=1;
            float2 previous=center;
            [loop] for(int j=1;j<=_ConnectivitySteps;j++)
            {
                float2 next=lerp(center,target,(float)j/_ConnectivitySteps);
                if(!PairConnected(previous,next)) { connected=0; break; }
                previous=next;
            }
            // Exact integer values in a half-float texture: bit0 connected,
            // bit1 center closer-or-equal. Directions: R right G left B up A down.
            return connected+2*closer;
        }
        ENDHLSL
        Pass
        {
            Name "Connectivity"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i):SV_Target
            {
                float4 result=0;
                [unroll] for(int k=0;k<4;k++) result[k]=Connection(i.uv,i.uv+Direction(k)*_PixelStep.xy);
                return result;
            }
            ENDHLSL
        }
        Pass
        {
            Name "Outline"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i):SV_Target
            {
                float4 links=tex2D(_Connectivity,i.uv);
                float3 ink=0; float weight=0,connectedCount=0;
                [unroll] for(int k=0;k<4;k++)
                {
                    uint bits=(uint)round(links[k]);
                    connectedCount+=(bits&1)!=0 ? 1 : 0;
                    if((bits&1)==0 && (bits&2)==0)
                    {
                        float2 uv=SampleUV(i.uv+Direction(k)*_PixelStep.xy);
                        float4 owner=tex2D(_AlbedoBuffer,uv);
                        if(owner.a>0.5)
                        {
                            ink+=_SolidColor>0.5 ? _OutlineTint.rgb : owner.rgb*_OutlineTint.rgb;
                            weight++;
                        }
                    }
                }
                if(_View>4.5 && _View<5.5) { float m=weight>0 ? 1 : 0; return float4(m.xxx,1); }
                if(_View>5.5) return float4(connectedCount/4,weight/4,0,1);
                float4 base=tex2D(_AlbedoBuffer,SampleUV(i.uv));
                float3 color=base.a>0.5 ? base.rgb : _Background.rgb;
                if(weight>0 && _Enabled>0.5) color=ink/weight;
                return float4(color,1);
            }
            ENDHLSL
        }
    }
}
