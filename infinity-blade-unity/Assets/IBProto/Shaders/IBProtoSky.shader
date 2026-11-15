// 空：イラストっぽいグラデーション＋大きな月と、そのまわりのネオンの輪（スカイボックス用）
Shader "IBProto/Sky"
{
    Properties
    {
        _Top ("天頂の色", Color) = (0.02, 0.02, 0.08, 1)
        _Mid ("中ほどの色", Color) = (0.2, 0.05, 0.32, 1)
        _Horizon ("地平の色", Color) = (0.9, 0.3, 0.5, 1)
        _Bottom ("足元の色", Color) = (0.03, 0.02, 0.08, 1)
        _HorizonWidth ("地平の帯の太さ", Range(0.01, 0.6)) = 0.16
        _SunDir ("月の方向", Vector) = (0, 0.16, 1, 0)
        _SunColor ("月の色", Color) = (1, 0.9, 0.85, 1)
        _SunSize ("月の大きさ", Range(0, 0.4)) = 0.11
        [HDR] _RingColor ("輪の色", Color) = (0.3, 1.6, 1.8, 1)
        _RingSize ("輪の大きさ", Range(0, 0.6)) = 0.16
        _RingWidth ("輪の太さ", Range(0.001, 0.03)) = 0.005
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Top, _Mid, _Horizon, _Bottom, _SunColor, _RingColor;
                float4 _SunDir; half _HorizonWidth, _SunSize, _RingSize, _RingWidth;
            CBUFFER_END
            struct V { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };
            V vert(float4 pos : POSITION) { V o; o.pos = TransformObjectToHClip(pos.xyz); o.dir = pos.xyz; return o; }
            half4 frag(V i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float y = d.y;
                half3 up = lerp(_Mid.rgb, _Top.rgb, saturate((y - _HorizonWidth) / 0.55));
                half3 c = y >= 0 ? lerp(_Horizon.rgb, up, smoothstep(0, _HorizonWidth, y)) : lerp(_Horizon.rgb, _Bottom.rgb, saturate(-y * 6));
                float a = acos(clamp(dot(d, normalize(_SunDir.xyz)), -1, 1));
                c = lerp(c, _SunColor.rgb, step(a, _SunSize));
                c += _RingColor.rgb * step(abs(a - _RingSize), _RingWidth);
                c += _RingColor.rgb * 0.35 * step(abs(a - _RingSize * 1.5), _RingWidth * 0.6);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
