Shader "TA/Surface Dust GPU"
{
    Properties
    {
        _DustColor("Dust color", Color) = (0.85, 0.68, 0.4, 0.55)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Name "Dust"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SurfaceDustParticle.hlsl"
            StructuredBuffer<DustParticle> _Particles;
            CBUFFER_START(UnityPerMaterial)
                float4 _DustColor;
            CBUFFER_END
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float alpha : TEXCOORD1;
                float brightness : TEXCOORD2;
            };
            Varyings Vert(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
            {
                const float2 corners[6] = {
                    float2(-1,-1), float2(-1,1), float2(1,1),
                    float2(-1,-1), float2(1,1), float2(1,-1)
                };
                DustParticle p = _Particles[instanceID];
                float progress = saturate(p.age / max(p.lifetime, 0.001));
                float2 corner = corners[vertexID];
                float3 centerVS = TransformWorldToView(p.position);
                float2 velocityVS = mul((float3x3)UNITY_MATRIX_V, p.velocity).xy;
                float speed = length(velocityVS);
                float2 direction = speed > 1e-5 ? velocityVS / speed : float2(0, 1);
                float2 side = float2(direction.y, -direction.x);
                float size = p.size * lerp(0.65, 1.2, progress);
                float stretch = 1 + min(speed * 1.4, 1.5);
                centerVS.xy += (side * corner.x + direction * corner.y * stretch) * size;
                Varyings o;
                o.positionCS = mul(UNITY_MATRIX_P, float4(centerVS, 1));
                o.uv = corner;
                o.alpha = p.age < 0 ? 0 : smoothstep(0, 0.12, progress) * (1 - smoothstep(0.5, 1, progress));
                o.brightness = lerp(0.65, 1.3, (p.seed & 255u) / 255.0);
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float radius2 = dot(i.uv, i.uv);
                clip(1 - radius2);
                float alpha = pow(saturate(1 - radius2), 2.5) * i.alpha * _DustColor.a;
                return half4(_DustColor.rgb * i.brightness * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
