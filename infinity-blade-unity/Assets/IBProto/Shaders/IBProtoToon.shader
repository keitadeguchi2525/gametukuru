// アニメ調の影（2段）＋縁の光＋小さなハイライト。URP 用
Shader "IBProto/Toon"
{
    Properties
    {
        _Lit ("明るい色", Color) = (1, 1, 1, 1)
        _Shade ("影の色", Color) = (0.3, 0.3, 0.4, 1)
        _Threshold ("影の境目", Range(-1, 1)) = 0.05
        _RimColor ("縁の光の色", Color) = (0.35, 0.85, 1, 1)
        _Rim ("縁の光の強さ", Range(0, 1)) = 0.3
        _Spec ("ハイライト", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Lit, _Shade, _RimColor;
                half _Threshold, _Rim, _Spec;
            CBUFFER_END
            struct A { float4 pos : POSITION; float3 n : NORMAL; };
            struct V { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 wp : TEXCOORD1; float fog : TEXCOORD2; };
            V vert(A i)
            {
                V o;
                VertexPositionInputs p = GetVertexPositionInputs(i.pos.xyz);
                o.pos = p.positionCS; o.wp = p.positionWS;
                o.n = TransformObjectToWorldNormal(i.n);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float3 n = normalize(i.n);
                Light L = GetMainLight(TransformWorldToShadowCoord(i.wp));
                half lit = step(_Threshold, dot(n, L.direction)) * step(0.5h, L.shadowAttenuation);
                half3 c = lerp(_Shade.rgb, _Lit.rgb, lit);
                float3 v = normalize(GetWorldSpaceViewDir(i.wp));
                half3 h = normalize(L.direction + v);
                c += _Spec * lit * step(0.93h, dot(n, h));
                half rim = step(0.62h, 1.0h - saturate(dot(n, v)));
                c += _RimColor.rgb * _Rim * rim * 0.6h;
                c = MixFog(c, i.fog);
                return half4(c, 1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/SHADOWCASTER"
        UsePass "Universal Render Pipeline/Lit/DEPTHONLY"
        UsePass "Universal Render Pipeline/Lit/DEPTHNORMALS"
    }
    FallBack "Universal Render Pipeline/Unlit"
}
