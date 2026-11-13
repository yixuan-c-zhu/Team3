// Cross Section paper style: coloured glass is cellophane. No ink outline (solid things have ink,
// glass does not), a coloured edge, the colour's pattern (blue = stripes, orange = dots) and one
// highlight streak. Edges come from the renderer's world bounds, so it works on level cubes of any
// size and on the bridge AimZone grows from a slice. Pattern 0 (none) is used for checkpoint gates.
Shader "Paper/Glass"
{
    Properties
    {
        [MainColor] _BaseColor ("Glass color (alpha = fill)", Color) = (0.180, 0.384, 0.941, 0.28)
        [Enum(None,0,Stripes,1,Dots,2)] _Pattern ("Pattern", Float) = 1
        _PatternAlpha ("Pattern opacity", Range(0, 1)) = 0.32
        _PatternScale ("Pattern spacing (world)", Float) = 0.32
        _EdgeColor ("Edge color", Color) = (0.118, 0.310, 0.839, 1)
        _EdgeWidth ("Edge width (world)", Float) = 0.07
        _GlintStrength ("Highlight streak", Range(0, 1)) = 0.55
        _BackFaceAlpha ("Back face opacity", Range(0, 1)) = 0.45
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            float _Pattern;
            half _PatternAlpha;
            float _PatternScale;
            half4 _EdgeColor;
            float _EdgeWidth;
            half _GlintStrength;
            half _BackFaceAlpha;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings vert(Attributes v)
        {
            Varyings o = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_TRANSFER_INSTANCE_ID(v, o);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
            o.positionCS = p.positionCS;
            o.positionWS = p.positionWS;
            o.normalWS = TransformObjectToWorldNormal(v.normalOS);
            return o;
        }

        float Ink(float dist, float halfWidth)
        {
            float aa = fwidth(dist) + 1e-5;
            return 1.0 - smoothstep(halfWidth - aa, halfWidth + aa, dist);
        }

        half4 GlassColor(Varyings i, float isBack)
        {
            float3 p = i.positionWS;
            float3 an = abs(normalize(i.normalWS));
            float3 bmin = unity_RendererBounds_Min.xyz;
            float3 bmax = unity_RendererBounds_Max.xyz;

            // Face-local coordinates: q runs across the face, (lo, hi) are the face's corners.
            float2 q, lo, hi;
            if (an.y > 0.5) { q = p.xz; lo = bmin.xz; hi = bmax.xz; }
            else if (an.x > an.z) { q = p.zy; lo = bmin.zy; hi = bmax.zy; }
            else { q = p.xy; lo = bmin.xy; hi = bmax.xy; }

            float2 d2 = min(q - lo, hi - q);
            float edge = Ink(min(d2.x, d2.y), _EdgeWidth);
            float stripe = Ink(abs(frac(q.x / _PatternScale) - 0.5), 0.14);
            float dots = Ink(length(frac(q / _PatternScale) - 0.5), 0.16);
            float corner = (q.x - lo.x) + (hi.y - q.y);            // distance from the top-left corner
            float glint = Ink(abs(corner - 0.55), 0.12) + Ink(abs(corner - 0.85), 0.04);

            half m = _Pattern < 0.5 ? 0.0h : (_Pattern < 1.5 ? stripe : dots);
            half3 col = lerp(_BaseColor.rgb, _EdgeColor.rgb, m * 0.6h);
            half a = max(_BaseColor.a, m * _PatternAlpha);

            half g = saturate(glint) * _GlintStrength * (1.0h - isBack);
            col = lerp(col, half3(1.0h, 1.0h, 1.0h), g);
            a = max(a, g * 0.85h);

            col = lerp(col, _EdgeColor.rgb, edge);
            a = lerp(a, 0.92h, edge);
            a *= lerp(1.0h, _BackFaceAlpha, isBack);
            return half4(col, a);
        }
        ENDHLSL

        // Back faces first, so the far edges of the box show through the front.
        Pass
        {
            Name "GlassBack"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment fragBack
            #pragma multi_compile_instancing
            half4 fragBack(Varyings i) : SV_Target { UNITY_SETUP_INSTANCE_ID(i); return GlassColor(i, 1.0); }
            ENDHLSL
        }

        Pass
        {
            Name "GlassFront"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment fragFront
            #pragma multi_compile_instancing
            half4 fragFront(Varyings i) : SV_Target { UNITY_SETUP_INSTANCE_ID(i); return GlassColor(i, 0.0); }
            ENDHLSL
        }
    }
    FallBack Off
}
