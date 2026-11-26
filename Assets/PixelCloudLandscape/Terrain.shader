Shader "TA/Pixel Cloud Landscape/Terrain"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "LandscapeLight.hlsl"
            struct In { float3 pos:POSITION; float3 normal:NORMAL; float4 color:COLOR; };
            struct Out { float4 pos:SV_POSITION; float3 normal:TEXCOORD0; float3 albedo:TEXCOORD1; };
            Out vert(In v)
            {
                Out o;
                o.pos = TransformObjectToHClip(v.pos);
                o.normal = TransformObjectToWorldNormal(v.normal);
                o.albedo = v.color.rgb;
                return o;
            }
            half4 frag(Out i):SV_Target
            {
                float ndl=dot(normalize(i.normal), GetMainLight().direction);
                return half4(toTarget(i.albedo*landscapeLight(0.36+0.65*ndl,0)),1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Unlit/DepthNormalsOnly"
        UsePass "Universal Render Pipeline/Unlit/DepthOnly"
    }
}

