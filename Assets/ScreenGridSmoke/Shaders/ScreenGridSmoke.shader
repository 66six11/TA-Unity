Shader "TA/Screen Grid Smoke"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_Background); TEXTURE2D(_SceneDepth); TEXTURE2D(_Dye);
        TEXTURE2D(_Velocity); TEXTURE2D(_Pressure); TEXTURE2D(_Curl); TEXTURE2D(_Mask);
        float4 _Settings, _GridTexel;
        struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
        Varyings MaskVert(float3 positionOS : POSITION)
        {
            Varyings o; o.positionCS = TransformObjectToHClip(positionOS); o.uv = 0; return o;
        }
        float MaskFrag(Varyings i) : SV_Target { return 1; }
        Varyings FullscreenVert(uint id : SV_VertexID)
        {
            Varyings o; o.positionCS = GetFullScreenTriangleVertexPosition(id); o.uv = GetFullScreenTriangleTexCoord(id); return o;
        }
        float Density(float2 uv) { return SAMPLE_TEXTURE2D_LOD(_Dye, sampler_LinearClamp, uv, 0).a; }
        float4 Composite(Varyings i) : SV_Target
        {
            float2 uv = i.uv;
            float4 dye = SAMPLE_TEXTURE2D_LOD(_Dye, sampler_LinearClamp, uv, 0);
            float density = max(0, dye.a);
            float mask = SAMPLE_TEXTURE2D_LOD(_Mask, sampler_PointClamp, uv, 0).r;
            int mode = (int)_Settings.x;
            if (mode == 1) return float4((1 - exp(-density)).xxx, 1);
            if (mode == 2)
            {
                float2 v = SAMPLE_TEXTURE2D_LOD(_Velocity, sampler_LinearClamp, uv, 0).xy / _Settings.w;
                return float4(0.5 + v.x * 2, 0.5 - v.y * 2, saturate(length(v) * 4), 1);
            }
            if (mode == 3)
            {
                float p = SAMPLE_TEXTURE2D_LOD(_Pressure, sampler_LinearClamp, uv, 0).r;
                return float4(p > 0 ? p * 0.02 : 0, 0.03, p < 0 ? -p * 0.02 : 0, 1);
            }
            if (mode == 4)
            {
                float curl = SAMPLE_TEXTURE2D_LOD(_Curl, sampler_LinearClamp, uv, 0).r;
                return float4(max(0, curl) * 0.3, 0.02, max(0, -curl) * 0.3, 1);
            }
            if (mode == 5) return float4(mask.xxx, 1);
            float3 background = SAMPLE_TEXTURE2D_LOD(_Background, sampler_LinearClamp, uv, 0).rgb;
            float sceneRaw = SAMPLE_TEXTURE2D_LOD(_SceneDepth, sampler_PointClamp, uv, 0).r;
            float sceneDepth = LinearEyeDepth(sceneRaw, _ZBufferParams);
            // A single depth plane is an artistic occlusion approximation for this 2D simulation.
            if (mask > 0.5 || sceneDepth < _Settings.z * 0.96) return float4(background, 1);
            float2 gradient = float2(Density(uv + float2(_GridTexel.x, 0)) - Density(uv - float2(_GridTexel.x, 0)),
                                     Density(uv + float2(0, _GridTexel.y)) - Density(uv - float2(0, _GridTexel.y)));
            float shade = clamp(0.8 + dot(gradient, float2(-0.7, -0.5)) * 1.5, 0.35, 1.35);
            float3 pigment = dye.rgb / max(density, 1e-5);
            float opacity = 1 - exp(-density * _Settings.y);
            float3 smoke = pigment * shade + float3(0.04, 0.07, 0.085) * saturate(length(gradient));
            return float4(lerp(background, smoke, opacity), 1);
        }
        ENDHLSL
        Pass
        {
            Name "ModelSilhouette"
            Blend One One
            BlendOp Max
            HLSLPROGRAM
            #pragma vertex MaskVert
            #pragma fragment MaskFrag
            ENDHLSL
        }
        Pass
        {
            Name "SmokeComposite"
            HLSLPROGRAM
            #pragma vertex FullscreenVert
            #pragma fragment Composite
            ENDHLSL
        }
    }
}
