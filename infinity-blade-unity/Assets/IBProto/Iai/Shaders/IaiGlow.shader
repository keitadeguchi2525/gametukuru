// 光の線・火花・残光：足し算で光る（影を受けない）。頂点の色と掛け合わせる
Shader "IBProto/IaiGlow"
{
    Properties
    {
        [HDR] _Color ("光の色", Color) = (1, 1, 1, 1)
        _Intensity ("強さ", Range(0, 12)) = 2
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("奥行きの比べ方", Float) = 4
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }
        Pass
        {
            Name "Glow"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            ZTest [_ZTest]
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial) half4 _Color; half _Intensity; float _ZTest; CBUFFER_END
            struct A { float4 pos : POSITION; half4 col : COLOR; };
            struct V { float4 pos : SV_POSITION; half4 col : COLOR; };
            V vert(A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.col = i.col; return o; }
            half4 frag(V i) : SV_Target { return half4(_Color.rgb * i.col.rgb * _Intensity, _Color.a * i.col.a); }
            ENDHLSL
        }
    }
}
