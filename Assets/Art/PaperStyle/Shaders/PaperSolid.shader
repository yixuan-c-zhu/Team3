// Cross Section paper style: level blocks (floors, walls, steps, ledges, pads, mechanisms).
// Tops are cardstock, sides are corrugated kraft. Everything is in world space, so cubes of any size
// tile seamlessly and no UVs are needed. _BaseColor.a < 1 (set by Fadeable / DepthFader) fades the
// block with a halftone screen instead of real transparency, so it stays opaque and sort-safe.
Shader "Paper/Solid"
{
    Properties
    {
        [MainColor] _BaseColor ("Tint (alpha = fade)", Color) = (1, 1, 1, 1)

        [Header(Surfaces)]
        _TopColor ("Top (cardstock)", Color) = (0.969, 0.965, 0.945, 1)
        _SideColor ("Sides (kraft)", Color) = (0.690, 0.553, 0.376, 1)
        _BottomColor ("Bottom", Color) = (0.561, 0.451, 0.314, 1)
        _LipColor ("Top edge band", Color) = (0.969, 0.965, 0.945, 1)
        _LipWidth ("Top edge band width (world)", Float) = 0.07

        [Header(Corrugation)]
        _FluteColor ("Flute lines", Color) = (0.494, 0.380, 0.255, 1)
        _FluteStrength ("Strength", Range(0, 1)) = 0.8
        _FluteRow ("Row height (world)", Float) = 0.5
        _FluteWave ("Wave length (world)", Float) = 0.42
        _FluteWidth ("Line width (world)", Float) = 0.028

        [Header(Top marks)]
        [Enum(Plain,0,CuttingMat,1,DottedBorder,2,Lookout,3)] _TopStyle ("Top style", Float) = 0
        _MarkColor ("Mark color", Color) = (0.435, 0.690, 0.561, 1)
        _MarkScale ("Mark spacing (world)", Float) = 0.5

        [Header(Paper)]
        [NoScaleOffset] _GrainTex ("Grain", 2D) = "gray" {}
        _GrainScale ("Grain tiling (per world unit)", Float) = 0.4
        _GrainStrength ("Grain strength", Range(0, 1)) = 0.16

        [Header(Lighting)]
        _Ambient ("Ambient", Range(0, 1.5)) = 0.74
        _Diffuse ("Sun", Range(0, 1)) = 0.3
        _ShadowStrength ("Shadow strength", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _TopColor;
            half4 _SideColor;
            half4 _BottomColor;
            half4 _LipColor;
            float _LipWidth;
            half4 _FluteColor;
            half _FluteStrength;
            float _FluteRow;
            float _FluteWave;
            float _FluteWidth;
            float _TopStyle;
            half4 _MarkColor;
            float _MarkScale;
            float _GrainScale;
            half _GrainStrength;
            half _Ambient;
            half _Diffuse;
            half _ShadowStrength;
        CBUFFER_END

        TEXTURE2D(_GrainTex);
        SAMPLER(sampler_GrainTex);

        // Halftone fade: a rotated 6 px dot screen. alpha 1 = solid, 0.2 = faint printed ghost.
        void PaperFadeClip(half alpha, float4 positionCS)
        {
            if (alpha >= 0.995h) return;
            float2 p = positionCS.xy / 6.0;
            p = float2(p.x + p.y, p.y - p.x) * 0.70710678;
            float d = length(frac(p) - 0.5);
            clip(sqrt(saturate(alpha) / 3.14159265) - d);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/AmbientOcclusion.hlsl"

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
                float4 screenPos : TEXCOORD2;
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
                o.screenPos = ComputeScreenPos(p.positionCS);
                return o;
            }

            // Thin anti-aliased line: 1 when dist < halfWidth.
            float Ink(float dist, float halfWidth)
            {
                float aa = fwidth(dist) + 1e-5;
                return 1.0 - smoothstep(halfWidth - aa, halfWidth + aa, dist);
            }

            half3 TopMarks(half3 col, float2 q, float2 lo, float2 hi)
            {
                float2 dEdge2 = min(q - lo, hi - q);
                float dEdge = min(dEdge2.x, dEdge2.y);

                if (_TopStyle < 1.5)
                {
                    // Cutting mat: minor grid, every 4th line stronger, border line inset from the edge.
                    float2 fg = abs(frac(q / _MarkScale + 0.5) - 0.5) * _MarkScale;
                    float minor = Ink(min(fg.x, fg.y), 0.009);
                    float big = _MarkScale * 4.0;
                    float2 fG = abs(frac(q / big + 0.5) - 0.5) * big;
                    float major = Ink(min(fG.x, fG.y), 0.02);
                    float border = Ink(abs(dEdge - 0.1), 0.018);
                    return lerp(col, _MarkColor.rgb, saturate(minor * 0.5 + major + border));
                }
                if (_TopStyle < 2.5)
                {
                    // Dotted border (tracing-paper path).
                    float along = dEdge2.x < dEdge2.y ? q.y : q.x;
                    float spacing = max(_MarkScale * 0.5, 0.05);
                    float2 v = float2(dEdge - 0.16, (frac(along / spacing) - 0.5) * spacing);
                    return lerp(col, _MarkColor.rgb, Ink(length(v), 0.045));
                }
                // Lookout: viewfinder corner brackets and a dashed ring.
                float2 c = (lo + hi) * 0.5;
                float2 h = (hi - lo) * 0.5;
                float2 r = abs(q - c);
                float2 corner = h - 0.22;
                float len = min(h.x, h.y) * 0.4;
                float w = 0.04;
                float bx = Ink(abs(r.y - corner.y), w) * step(corner.x - len, r.x) * step(r.x, corner.x + w);
                float bz = Ink(abs(r.x - corner.x), w) * step(corner.y - len, r.y) * step(r.y, corner.y + w);
                float rad = min(h.x, h.y) * 0.38;
                float ang = atan2(q.y - c.y, q.x - c.x);
                float ring = Ink(abs(length(q - c) - rad), w) * step(0.0, sin(ang * 10.0));
                return lerp(col, _MarkColor.rgb, saturate(bx + bz + ring));
            }

            half PaperLight(float3 positionWS, float3 n, float4 screenPos, float4 positionCS)
            {
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord = screenPos;
                #else
                    float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
                #endif
                Light mainLight = GetMainLight(shadowCoord);
                half ndl = saturate(dot(n, mainLight.direction));
                half shadow = mainLight.shadowAttenuation;
                half ao = 1.0h;
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor aoFactor = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(positionCS));
                    ao = aoFactor.indirectAmbientOcclusion;
                #endif
                half ambient = _Ambient * ao * lerp(1.0h, shadow, _ShadowStrength * 0.6h);
                return ambient + _Diffuse * ndl * lerp(1.0h, shadow, _ShadowStrength);
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                PaperFadeClip(_BaseColor.a, i.positionCS);

                float3 n = normalize(i.normalWS);
                float3 an = abs(n);
                float3 p = i.positionWS;
                float3 bmin = unity_RendererBounds_Min.xyz;
                float3 bmax = unity_RendererBounds_Max.xyz;

                half3 col;
                float2 faceUV;
                if (n.y > 0.5)
                {
                    faceUV = p.xz;
                    col = _TopColor.rgb;
                    if (_TopStyle > 0.5) col = TopMarks(col, p.xz, bmin.xz, bmax.xz);
                }
                else if (n.y < -0.5)
                {
                    faceUV = p.xz;
                    col = _BottomColor.rgb;
                }
                else
                {
                    float u = an.x > an.z ? p.z : p.x;
                    faceUV = float2(u, p.y);
                    col = _SideColor.rgb;
                    float fromTop = bmax.y - p.y;

                    if (_FluteStrength > 0.001)
                    {
                        // Corrugated board seen from its cut edge: rows of sine flutes between liners,
                        // hanging from the top edge so every block starts with a clean liner.
                        float rows = max(fromTop - _LipWidth, 0.0) / _FluteRow;
                        float row = floor(rows);
                        float fr = frac(rows);
                        float k = 6.2831853 / _FluteWave;
                        float phase = u * k + row * 3.14159265;
                        float wave = 0.5 + 0.34 * sin(phase);
                        float slope = _FluteRow * 0.34 * k * cos(phase);
                        float dist = _FluteRow * abs(fr - wave) * rsqrt(1.0 + slope * slope);
                        float flute = Ink(dist, _FluteWidth * 0.5);
                        float liner = Ink(min(fr, 1.0 - fr) * _FluteRow, _FluteWidth * 0.35);
                        float m = saturate(max(flute, liner * 0.7)) * _FluteStrength * step(_LipWidth, fromTop);
                        col = lerp(col, _FluteColor.rgb, m);
                    }

                    // Top edge band: the cardstock top seen edge-on (also visible in the flat 2D view).
                    float aa = fwidth(fromTop) + 1e-5;
                    float lip = 1.0 - smoothstep(_LipWidth - aa, _LipWidth + aa, fromTop);
                    col = lerp(col, _LipColor.rgb, lip);
                }

                half g = SAMPLE_TEXTURE2D(_GrainTex, sampler_GrainTex, faceUV * _GrainScale).r;
                col *= 1.0h + (g - 0.5h) * 2.0h * _GrainStrength;
                col *= PaperLight(p, n, i.screenPos, i.positionCS);
                col *= _BaseColor.rgb;
                return half4(col, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings vertShadow(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
                return o;
            }

            half4 fragShadow(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                clip(_BaseColor.a - 0.99h);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vertDepth(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            half fragDepth(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                // Faded blocks stay out of the depth texture: no ink lines or 2D drop shadow on ghosts.
                clip(_BaseColor.a - 0.99h);
                return i.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vertDN
            #pragma fragment fragDN
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vertDN(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            void fragDN(Varyings i, out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(i);
                clip(_BaseColor.a - 0.99h);
                outNormalWS = half4(NormalizeNormalPerPixel(i.normalWS), 0.0);
                #ifdef _WRITE_RENDERING_LAYERS
                    outRenderingLayers = EncodeMeshRenderingLayer();
                #endif
            }
            ENDHLSL
        }
    }
    FallBack Off
}
