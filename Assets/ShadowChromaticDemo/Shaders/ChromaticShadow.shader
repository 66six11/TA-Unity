Shader "TA/Chromatic Shadow Demo"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.8,0.8,0.8,1)
        _SunIrradiance ("Sun Irradiance (Linear RGB)", Vector) = (4,2.6,1.47,1)
        _SkyRadiance ("Upper Hemisphere Radiance", Vector) = (0.12,0.24,0.5,1)
        _GroundRadiance ("Lower Hemisphere Radiance", Vector) = (0.035,0.025,0.02,1)
        _SkyIntensity ("Environment Intensity", Range(0,3)) = 0.4
        _Softness ("Penumbra Filter Radius (World Units)", Range(0.01,0.5)) = 0.22
        [Enum(Combined,0,SunOnly,1,SkyOnly,2,Visibility,3)] _ViewMode ("Lighting View", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _SunIrradiance;
            float4 _SkyRadiance;
            float4 _GroundRadiance;
            float _SkyIntensity;
            float _Softness;
            float _ViewMode;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ChromaticForward"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half fog : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = p.positionCS;
                output.positionWS = p.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fog = ComputeFogFactor(p.positionCS.z);
                return output;
            }

            // Average comparison results, never average depth values.
            half VisibilityPCF(float3 coord, float2 radius)
            {
                half v = 0;
                float weightSum = 0;
                // A disk avoids the repeated parallel bands of a sparse square grid.
                [unroll] for (int i = 0; i < 64; ++i)
                {
                    float diskRadius = sqrt((i + 0.5) / 64.0);
                    float angle = i * 2.39996323;
                    float2 disk = float2(cos(angle), sin(angle)) * diskRadius;
                    float weight = 1 - 0.75 * diskRadius;
                    v += SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture,
                        sampler_LinearClampCompare, float3(coord.xy + disk * radius, coord.z)) * weight;
                    weightSum += weight;
                }
                return v / weightSum;
            }

            // Return raw geometric visibility, before light strength and distance fade.
            half ShadowVisibility(float3 positionWS)
            {
                #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                    int cascade = 0;
                    #if defined(_MAIN_LIGHT_SHADOWS_CASCADE) || defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                        cascade = (int)ComputeCascadeIndex(positionWS);
                        if (cascade >= MAX_SHADOW_CASCADES) return 1;
                    #endif
                    // Explicit light-space transform: TransformWorldToShadowCoord can
                    // return screen UV when URP Screen Space Shadows is enabled.
                    float4x4 worldToShadow = _MainLightWorldToShadow[cascade];
                    float3 coord = mul(worldToShadow, float4(positionWS,1)).xyz;
                    if (BEYOND_SHADOW_FAR(coord)) return 1;
                    // Matrix row lengths convert world units to atlas UV per cascade.
                    float2 uvPerWorldUnit = float2(length(worldToShadow[0].xyz), length(worldToShadow[1].xyz));
                    return VisibilityPCF(coord, _Softness * uvPerWorldUnit);
                #else
                    return 1;
                #endif
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half rawVisibility = ShadowVisibility(input.positionWS);
                half shadowAmount = GetMainLightShadowParams().x * (1 - GetMainLightShadowFade(input.positionWS));
                half visibility = lerp(1, rawVisibility, shadowAmount);
                if (_ViewMode > 2.5) return half4(visibility,visibility,visibility,1);
                Light light = GetMainLight();
                half3 normalWS = normalize(input.normalWS);
                half ndotl = saturate(dot(normalWS, light.direction));
                // Treat the URP main light as incident irradiance. Lambert BRDF = albedo / PI.
                half3 sun = _SunIrradiance.rgb * ndotl * visibility / PI;
                // Exact unoccluded Lambert integral for two constant-radiance hemispheres:
                // E = PI * [Lupper * (1+Ny)/2 + Llower * (1-Ny)/2].
                half upperWeight = saturate(normalWS.y * 0.5h + 0.5h);
                half3 sky = lerp(_GroundRadiance.rgb, _SkyRadiance.rgb, upperWeight) * _SkyIntensity;
                if (_ViewMode > 0.5 && _ViewMode < 1.5) sky = 0;
                if (_ViewMode > 1.5) sun = 0;
                half3 color = _BaseColor.rgb * (sky + sun);
                return half4(MixFog(color, input.fog), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            struct A { float4 positionOS:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS:SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            V DepthVert(A input)
            {
                V output = (V)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }
            half DepthFrag(V input):SV_Target { return input.positionCS.z; }
            ENDHLSL
        }
    }
    Fallback Off
}
