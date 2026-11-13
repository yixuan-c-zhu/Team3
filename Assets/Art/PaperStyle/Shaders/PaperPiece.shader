// Cross Section paper style: the player and every piece cut from it.
// The body is coloured paper with its colour's pattern (blue = stripes, orange = dots) so colour-blind
// players can still match themselves to the glass. Faces made by a cut are drawn as an engineering
// section: light paper with 45 degree ink hatching. ConvexShape.MakeMesh marks those faces with vertex
// colour (1,0,0) and stores face-local coordinates (world units) in UV0 and the face size in UV1.
Shader "Paper/Piece"
{
    Properties
    {
        [MainColor] _BaseColor ("Color (alpha = fade)", Color) = (0.180, 0.384, 0.941, 1)
        [Enum(None,0,Stripes,1,Dots,2)] _Pattern ("Pattern", Float) = 1
        _PatternColor ("Pattern color", Color) = (1, 1, 1, 1)
        _PatternStrength ("Pattern strength", Range(0, 1)) = 0.14
        _PatternScale ("Pattern spacing (world)", Float) = 0.25

        [Header(Cut faces)]
        _CutColor ("Cut face paper", Color) = (0.953, 0.937, 0.894, 1)
        _HatchColor ("Hatch ink", Color) = (0.133, 0.157, 0.227, 1)
        _HatchSpacing ("Hatch spacing (world)", Float) = 0.15
        _HatchWidth ("Hatch width (world)", Float) = 0.026
        _CutRim ("Rim width (world)", Float) = 0.045

        [Header(Paper)]
        [NoScaleOffset] _GrainTex ("Grain", 2D) = "gray" {}
        _GrainScale ("Grain tiling (per world unit)", Float) = 0.4
        _GrainStrength ("Grain strength", Range(0, 1)) = 0.12

        [Header(Lighting)]
        _Ambient ("Ambient", Range(0, 1.5)) = 0.8
        _Diffuse ("Sun", Range(0, 1)) = 0.26
        _ShadowStrength ("Shadow strength", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            float _Pattern;
            half4 _PatternColor;
            half _PatternStrength;
            float _PatternScale;
            half4 _CutColor;
            half4 _HatchColor;
            float _HatchSpacing;
            float _HatchWidth;
            float _CutRim;
            float _GrainScale;
            half _GrainStrength;
            half _Ambient;
            half _Diffuse;
            half _ShadowStrength;
        CBUFFER_END

        TEXTURE2D(_GrainTex);
        SAMPLER(sampler_GrainTex);

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
                half4 color : COLOR;
                float2 faceUV : TEXCOORD0;
                float2 faceSize : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
                float2 faceUV : TEXCOORD3;
                float3 faceSizeCut : TEXCOORD4;   // xy = face size, z = 1 on cut faces
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
                o.faceUV = v.faceUV;
                float cut = (v.color.r > 0.5h && v.color.g < 0.5h) ? 1.0 : 0.0;
                o.faceSizeCut = float3(v.faceSize, cut);
                return o;
            }

            float Ink(float dist, float halfWidth)
            {
                float aa = fwidth(dist) + 1e-5;
                return 1.0 - smoothstep(halfWidth - aa, halfWidth + aa, dist);
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
                float2 uv = i.faceUV;
                float2 size = i.faceSizeCut.xy;
                half3 body = _BaseColor.rgb;
                half3 col;

                // Derivatives first, outside any branch.
                float hatchCoord = (uv.x + uv.y) * 0.70710678;
                float hatchDist = abs(frac(hatchCoord / _HatchSpacing + 0.5) - 0.5) * _HatchSpacing;
                float edgeDist = min(min(uv.x, size.x - uv.x), min(uv.y, size.y - uv.y));
                float stripe = abs(frac(uv.x / _PatternScale) - 0.5);
                float dotDist = length(frac(uv / _PatternScale) - 0.5);
                float hatch = Ink(hatchDist, _HatchWidth * 0.5);
                float rim = Ink(edgeDist, _CutRim);
                float stripeMask = 1.0 - Ink(stripe, 0.25);
                float dotMask = Ink(dotDist, 0.2);

                if (i.faceSizeCut.z > 0.5)
                {
                    // Section lining on cut faces, with a thin rim of the body colour.
                    col = lerp(_CutColor.rgb, body, 0.08h);
                    col = lerp(col, _HatchColor.rgb, hatch);
                    col = lerp(col, body, rim);
                }
                else
                {
                    col = body;
                    half m = _Pattern < 0.5 ? 0.0h : (_Pattern < 1.5 ? stripeMask : dotMask);
                    col = lerp(col, _PatternColor.rgb, m * _PatternStrength);
                }

                half g = SAMPLE_TEXTURE2D(_GrainTex, sampler_GrainTex, uv * _GrainScale).r;
                col *= 1.0h + (g - 0.5h) * 2.0h * _GrainStrength;
                col *= PaperLight(i.positionWS, n, i.screenPos, i.positionCS);
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
