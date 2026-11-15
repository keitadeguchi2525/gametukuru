// ネオン：光る色（影を受けない）
Shader "IBProto/Neon"
{
    Properties { [HDR] _Color ("光の色", Color) = (0.15, 0.95, 1, 1) _Intensity ("強さ", Range(0, 8)) = 1.6 }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "Neon"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial) half4 _Color; half _Intensity; CBUFFER_END
            float4 vert(float4 pos : POSITION) : SV_POSITION { return TransformObjectToHClip(pos.xyz); }
            half4 frag() : SV_Target { return half4(_Color.rgb * _Intensity, 1); }
            ENDHLSL
        }
    }
}
