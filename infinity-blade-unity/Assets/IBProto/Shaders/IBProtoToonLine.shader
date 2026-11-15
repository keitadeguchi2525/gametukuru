// 輪郭線：モデルに焼き込んだ「裏返しの殻」を塗るだけの色
Shader "IBProto/ToonLine"
{
    Properties { _Color ("線の色", Color) = (0.02, 0.02, 0.04, 1) }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "Line"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial) half4 _Color; CBUFFER_END
            float4 vert(float4 pos : POSITION) : SV_POSITION { return TransformObjectToHClip(pos.xyz); }
            half4 frag() : SV_Target { return _Color; }
            ENDHLSL
        }
    }
}
