Shader "TA/PixelRender/Toon"
{
    Properties
    {
        _BaseColor("Color", Color) = (1,1,1,1)
        [HDR] _EmissionColor("Emission radiance", Color) = (0,0,0,1)
        _BaseMap("Texture", 2D) = "white" {}
        _Cutoff("Cutoff", Float) = 0.5
        _Steps("Light bands", Range(2,6)) = 3
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Name "PixelSurface"
            Tags { "LightMode"="PixelSurface" }
            ZWrite On ZTest LEqual Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SurfaceVert
            #pragma fragment SurfaceFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _EmissionColor;
            float4 _BaseMap_ST;
            float _Cutoff;
            float _Steps;
            CBUFFER_END
            float4x4 _PixelCaptureVP, _PixelCaptureView;
            float _PixelCaptureLayer;
            struct Input { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct Surface { float4 positionCS:SV_POSITION; float3 normalWS:TEXCOORD0; float eyeDepth:TEXCOORD1; };
            struct Buffers { float4 albedo:SV_Target0; float4 normal:SV_Target1; float4 depth:SV_Target2; float4 radiance:SV_Target3; };
            Surface SurfaceVert(Input v)
            {
                Surface o;
                float3 world=TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS=mul(_PixelCaptureVP,float4(world,1));
                o.normalWS=TransformObjectToWorldNormal(v.normalOS);
                o.eyeDepth=-mul(_PixelCaptureView,float4(world,1)).z;
                return o;
            }
            Buffers SurfaceFrag(Surface i)
            {
                Buffers o;
                o.albedo=float4(_BaseColor.rgb,1);
                o.normal=float4(normalize(i.normalWS),1);
                o.depth=float4(i.eyeDepth,_PixelCaptureLayer,0,1);
                o.radiance=float4(_EmissionColor.rgb,1);
                return o;
            }
            ENDHLSL
        }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _EmissionColor;
            float4 _BaseMap_ST;
            float _Cutoff;
            float _Steps;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS=TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.positionWS);
                o.normalWS=TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                Light sun=GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float diffuse=saturate(dot(normalize(i.normalWS),sun.direction))*sun.shadowAttenuation;
                float band=floor(diffuse*(_Steps-0.001))/(_Steps-1);
                float3 illumination=float3(0.28,0.34,0.48)+band*sun.color*0.72;
                return half4(_BaseColor.rgb*illumination,1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
