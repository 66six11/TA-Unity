Shader "TA/Screen Space Fluid"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "../../SurfaceDustGpu/Shaders/SurfaceDustParticle.hlsl"
        StructuredBuffer<DustParticle> _Particles;
        TEXTURE2D(_SceneDepth);
        TEXTURE2D(_FluidDepth);
        TEXTURE2D(_RawDepth);
        TEXTURE2D(_Thickness);
        TEXTURE2D(_Background);
        float4 _FluidViewport, _Filter, _FluidSettings, _Absorption, _WaterColor;
        #define NO_FLUID 999999.0

        float ReadFluid(float2 uv) { return SAMPLE_TEXTURE2D_LOD(_FluidDepth, sampler_PointClamp, uv, 0).r; }
        float SceneEyeDepth(float2 uv)
        {
            float raw = SAMPLE_TEXTURE2D_LOD(_SceneDepth, sampler_PointClamp, uv, 0).r;
            if (unity_OrthoParams.w > 0.5)
            {
                #if UNITY_REVERSED_Z
                raw = 1 - raw;
                #endif
                return lerp(_ProjectionParams.y, _ProjectionParams.z, raw);
            }
            return LinearEyeDepth(raw, _ZBufferParams);
        }
        struct ParticleVaryings
        {
            float4 positionCS : SV_POSITION;
            float2 circle : TEXCOORD0;
            float3 centerVS : TEXCOORD1;
            float radius : TEXCOORD2;
        };
        ParticleVaryings ParticleVert(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
        {
            const float2 corners[6] = { float2(-1,-1), float2(-1,1), float2(1,1), float2(-1,-1), float2(1,1), float2(1,-1) };
            DustParticle p = _Particles[instanceID];
            float progress = saturate(p.age / max(0.001, p.lifetime));
            float fade = smoothstep(0, 0.08, progress) * (1 - smoothstep(0.7, 1, progress));
            float radius = p.age < 0 ? 0 : p.size * sqrt(fade);
            float3 center = TransformWorldToView(p.position);
            float3 surface = center + float3(corners[vertexID] * radius, 0);
            ParticleVaryings o;
            o.positionCS = mul(UNITY_MATRIX_P, float4(surface, 1));
            o.circle = corners[vertexID]; o.centerVS = center; o.radius = radius;
            return o;
        }
        float SphereDepth(ParticleVaryings i, out float halfChord)
        {
            float r2 = dot(i.circle, i.circle);
            clip(1 - r2);
            clip(i.radius - 0.0001);
            halfChord = i.radius * sqrt(saturate(1 - r2));
            float depth = -i.centerVS.z - halfChord;
            clip(depth - _ProjectionParams.y);
            return depth;
        }
        float DepthFrag(ParticleVaryings i) : SV_Target
        {
            float chord;
            return SphereDepth(i, chord);
        }
        float ThicknessFrag(ParticleVaryings i) : SV_Target
        {
            float chord;
            float depth = SphereDepth(i, chord);
            float scene = SceneEyeDepth(i.positionCS.xy * _FluidViewport.xy);
            // Clip each chord against opaque geometry, including the source model.
            return max(0, min(chord * 2, scene - depth));
        }
        struct FullscreenVaryings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
        FullscreenVaryings FullscreenVert(uint id : SV_VertexID)
        {
            FullscreenVaryings o;
            o.positionCS = GetFullScreenTriangleVertexPosition(id);
            o.uv = GetFullScreenTriangleTexCoord(id);
            return o;
        }
        float FilterFrag(FullscreenVaryings i) : SV_Target
        {
            float center = ReadFluid(i.uv);
            if (center >= NO_FLUID) return 1000000;
            float sum = 0, weightSum = 0;
            [unroll] for (int x = -4; x <= 4; x++)
            {
                // Linear sampling keeps fractional filter strides from producing alternating-pixel bands.
                // Samples interpolating toward the invalid-depth sentinel receive effectively zero range weight.
                float sampleDepth = SAMPLE_TEXTURE2D_LOD(_FluidDepth, sampler_LinearClamp, i.uv + _Filter.xy * x, 0).r;
                if (sampleDepth >= NO_FLUID) continue;
                float delta = (sampleDepth - center) / _Filter.z;
                float weight = exp(-0.125 * x * x - 0.5 * delta * delta);
                sum += sampleDepth * weight; weightSum += weight;
            }
            return sum / max(weightSum, 1e-6);
        }
        float3 ViewPosition(float2 uv, float depth)
        {
            float2 ndc = uv * 2 - 1;
            #if UNITY_UV_STARTS_AT_TOP
            ndc.y = -ndc.y;
            #endif
            float4 position = mul(UNITY_MATRIX_I_P, float4(ndc, 0.5, 1));
            position.xyz /= position.w;
            if (unity_OrthoParams.w > 0.5) return float3(position.xy, -depth);
            return position.xyz * (-depth / position.z);
        }
        float3 ReconstructNormal(float2 uv, float depth)
        {
            float2 dx = float2(_FluidViewport.x, 0), dy = float2(0, _FluidViewport.y);
            float left = ReadFluid(uv - dx), right = ReadFluid(uv + dx);
            float bottom = ReadFluid(uv - dy), top = ReadFluid(uv + dy);
            float3 center = ViewPosition(uv, depth);
            float3 tx = abs(right - depth) < abs(depth - left)
                ? ViewPosition(uv + dx, right) - center : center - ViewPosition(uv - dx, left);
            float3 ty = abs(top - depth) < abs(depth - bottom)
                ? ViewPosition(uv + dy, top) - center : center - ViewPosition(uv - dy, bottom);
            if (min(abs(left - depth), abs(right - depth)) > 0.5 || min(abs(top - depth), abs(bottom - depth)) > 0.5)
                return float3(0, 0, 1);
            float3 normal = normalize(cross(tx, ty));
            return normal.z < 0 ? -normal : normal;
        }
        float3 ReflectionSky(float3 direction)
        {
            float up = saturate(direction.y * 0.5 + 0.5);
            float3 color = lerp(float3(0.09, 0.12, 0.16), float3(0.46, 0.66, 0.86), up);
            float highlight = pow(saturate(dot(direction, normalize(float3(-0.4, 0.8, -0.6)))), 120);
            return color + highlight * float3(4, 3.5, 2.8);
        }
        float4 CompositeFrag(FullscreenVaryings i) : SV_Target
        {
            float2 uv = i.uv;
            float3 background = SAMPLE_TEXTURE2D_LOD(_Background, sampler_LinearClamp, uv, 0).rgb;
            float depth = ReadFluid(uv);
            float thickness = min(4, SAMPLE_TEXTURE2D_LOD(_Thickness, sampler_LinearClamp, uv, 0).r * _FluidSettings.x);
            bool valid = depth < NO_FLUID && depth < SceneEyeDepth(uv) - 0.001 && thickness > 0.00001;
            int mode = (int)_FluidSettings.z;
            if (!valid) return float4(mode == 0 ? background : float3(0.015, 0.015, 0.015), 1);
            float3 normalVS = ReconstructNormal(uv, depth);
            if (mode == 1) return float4((1 - saturate(SAMPLE_TEXTURE2D_LOD(_RawDepth, sampler_PointClamp, uv, 0).r / 15)).xxx, 1);
            if (mode == 2) return float4((1 - saturate(depth / 15)).xxx, 1);
            if (mode == 3) return float4(normalVS * 0.5 + 0.5, 1);
            if (mode == 4) return float4((1 - exp(-thickness * 3)).xxx, 1);

            float3 positionVS = ViewPosition(uv, depth);
            float3 viewVS = unity_OrthoParams.w > 0.5 ? float3(0, 0, 1) : normalize(-positionVS);
            float3 normalWS = normalize(mul((float3x3)UNITY_MATRIX_I_V, normalVS));
            float3 viewWS = normalize(mul((float3x3)UNITY_MATRIX_I_V, viewVS));
            float fresnel = 0.02 + 0.98 * pow(1 - saturate(dot(normalVS, viewVS)), 5);
            float2 offset = normalVS.xy;
            #if UNITY_UV_STARTS_AT_TOP
            offset.y = -offset.y;
            #endif
            float2 refractedUv = clamp(uv + offset * _FluidSettings.y * (1 - exp(-thickness)), 0.001, 0.999);
            if (SceneEyeDepth(refractedUv) < depth) refractedUv = uv;
            float3 refracted = SAMPLE_TEXTURE2D_LOD(_Background, sampler_LinearClamp, refractedUv, 0).rgb;
            float3 transmission = exp(-max(0, _Absorption.xyz) * thickness);
            float3 water = refracted * transmission + _WaterColor.rgb * (1 - transmission);
            float3 reflection = ReflectionSky(reflect(-viewWS, normalWS));
            return float4(lerp(water, reflection, fresnel), 1);
        }
        ENDHLSL
        Pass
        {
            Name "SphereDepth"
            Blend One One
            BlendOp Min
            HLSLPROGRAM
            #pragma vertex ParticleVert
            #pragma fragment DepthFrag
            ENDHLSL
        }
        Pass
        {
            Name "Thickness"
            Blend One One
            HLSLPROGRAM
            #pragma vertex ParticleVert
            #pragma fragment ThicknessFrag
            ENDHLSL
        }
        Pass
        {
            Name "BilateralDepth"
            HLSLPROGRAM
            #pragma vertex FullscreenVert
            #pragma fragment FilterFrag
            ENDHLSL
        }
        Pass
        {
            Name "LiquidComposite"
            HLSLPROGRAM
            #pragma vertex FullscreenVert
            #pragma fragment CompositeFrag
            ENDHLSL
        }
    }
}
