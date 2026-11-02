Shader "TA/Volume Source Capture"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        float4x4 _PreviousSourceToWorld;
        float4x4 _SourceCaptureVP, _SourceCaptureView;
        float _CaptureDeltaTime;
        struct Attributes { float3 positionOS:POSITION;float3 normalOS:NORMAL; };
        struct Varyings
        {
            float4 positionCS:SV_POSITION;
            float3 normalWS:TEXCOORD0;
            float3 motionWS:TEXCOORD1;
            float eyeDepth:TEXCOORD2;
        };
        Varyings Vert(Attributes input)
        {
            Varyings o;
            float3 world=TransformObjectToWorld(input.positionOS);
            o.positionCS=mul(_SourceCaptureVP,float4(world,1));
            o.normalWS=TransformObjectToWorldNormal(input.normalOS);
            float3 previous=mul(_PreviousSourceToWorld,float4(input.positionOS,1)).xyz;
            o.motionWS=(world-previous)/max(_CaptureDeltaTime,0.0001);
            o.eyeDepth=-mul(_SourceCaptureView,float4(world,1)).z;
            return o;
        }
        struct FrontOutput { float depth:SV_Target0;float4 normal:SV_Target1;float4 motion:SV_Target2; };
        FrontOutput Front(Varyings input)
        {
            FrontOutput o;
            o.depth=input.positionCS.z;
            o.normal=float4(normalize(input.normalWS),1);
            o.motion=float4(input.motionWS,1);
            return o;
        }
        float Back(Varyings input):SV_Target { return input.eyeDepth; }
        ENDHLSL
        Pass
        {
            Name "FrontDepthNormalMotion"
            Cull Back ZWrite On ZTest LEqual
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Front
            ENDHLSL
        }
        Pass
        {
            Name "FarthestBackDepth"
            Cull Front ZWrite Off ZTest Always
            Blend One One
            BlendOp Max
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Back
            ENDHLSL
        }
    }
}
