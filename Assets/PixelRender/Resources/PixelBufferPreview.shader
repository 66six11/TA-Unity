Shader "Hidden/TA/PixelBufferPreview"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _AlbedoBuffer, _NormalBuffer, _DepthBuffer;
            float _BufferView;
            float4 _DepthRange, _Background, _InformationSize;
            float4 frag(v2f_img i):SV_Target
            {
                float2 uv=(floor(i.uv*_InformationSize.xy+0.0001)+0.5)*_InformationSize.zw;
                float4 albedo=tex2D(_AlbedoBuffer,uv);
                float3 color=_Background.rgb;
                if(albedo.a>0.5)
                {
                    color=albedo.rgb;
                    if(_BufferView>0.5 && _BufferView<1.5) color=normalize(tex2D(_NormalBuffer,uv).xyz)*0.5+0.5;
                    else if(_BufferView>1.5 && _BufferView<2.5)
                        color=saturate((tex2D(_DepthBuffer,uv).r-_DepthRange.x)/max(_DepthRange.y-_DepthRange.x,0.0001)).xxx;
                    else if(_BufferView>2.5) color=1;
                }
                else if(_BufferView>0.5) color=0;
                return float4(color,1);
            }
            ENDHLSL
        }
    }
}
