// 遠景のシルエット用：一色塗り。根元ほど地平の色に溶ける＋霧
Shader "IBProto/Flat"
{
    Properties
    {
        _Color ("色", Color) = (0.15, 0.05, 0.25, 1)
        _BaseColor ("根元の色（地平に溶ける）", Color) = (0.9, 0.3, 0.5, 1)
        _FadeBottom ("溶け始める高さ", Float) = -2
        _FadeTop ("溶け終わる高さ", Float) = 6
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color, _BaseColor; float _FadeBottom, _FadeTop;
            CBUFFER_END
            struct V { float4 pos : SV_POSITION; float wy : TEXCOORD0; float fog : TEXCOORD1; };
            V vert(float4 pos : POSITION)
            {
                V o; VertexPositionInputs p = GetVertexPositionInputs(pos.xyz);
                o.pos = p.positionCS; o.wy = p.positionWS.y; o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                half t = saturate((i.wy - _FadeBottom) / max(0.001, _FadeTop - _FadeBottom));
                half3 c = lerp(_BaseColor.rgb, _Color.rgb, t);
                return half4(MixFog(c, i.fog), 1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/DEPTHONLY"
    }
}
