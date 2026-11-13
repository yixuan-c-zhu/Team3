// Cross Section paper style: full-screen pass run by the "Paper Ink" Full Screen Pass Renderer Feature
// (before transparents, needs Depth + Normal). It draws the ink outline on every solid object
// (depth and normal edges, so cut pieces made at runtime get lines too) and, in the orthographic
// 2D view, a hard 4 px drop shadow so objects look like paper stuck onto the background.
// Glass, eyes and UI are drawn after this pass, so they get no ink (the "glass has no outline" rule).
// It only does work when a PaperStyle component in the scene sets _PaperStyleOn; PaperStyle also
// hands over each camera's near/far/projection, which a full-screen blit can't rely on.
Shader "Hidden/Paper/Composite"
{
    Properties
    {
        _InkColor ("Ink", Color) = (0.133, 0.157, 0.227, 1)
        _InkWidth ("Line width (px at 1080p)", Float) = 3
        _InkOpacity ("Line opacity", Range(0, 1)) = 0.92
        _DepthSensitivity ("Depth edge threshold (relative)", Float) = 0.03
        _NormalThreshold ("Normal edge threshold", Range(0, 2)) = 0.4
        _Shadow2DOffset ("2D drop shadow offset (px at 1080p)", Float) = 4
        _Shadow2DOpacity ("2D drop shadow opacity", Range(0, 1)) = 0.2
        _Shadow2DGap ("2D shadow depth gap (world)", Float) = 0.75
        _Vignette ("Vignette", Range(0, 1)) = 0.1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend Off

        Pass
        {
            Name "PaperComposite"

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _InkColor;
                float _InkWidth;
                half _InkOpacity;
                float _DepthSensitivity;
                float _NormalThreshold;
                float _Shadow2DOffset;
                half _Shadow2DOpacity;
                float _Shadow2DGap;
                half _Vignette;
            CBUFFER_END

            // Globals set by the PaperStyle component.
            float _PaperStyleOn;            // 0 = pass through
            float4 _PaperCameraParams;      // near, far, 1 if orthographic
            float4 _PaperCameraForward;     // camera forward (world)

            float EyeDepth(float raw)
            {
                float n = _PaperCameraParams.x;
                float f = _PaperCameraParams.y;
                if (_PaperCameraParams.z > 0.5)
                {
                    #if UNITY_REVERSED_Z
                        raw = 1.0 - raw;
                    #endif
                    return lerp(n, f, raw);
                }
                #if UNITY_REVERSED_Z
                    return n * f / (n + raw * (f - n));
                #else
                    return n * f / (f - raw * (f - n));
                #endif
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                if (_PaperStyleOn <= 0.0 || _PaperCameraParams.y <= 0.0) return col;

                float px = max(_ScreenParams.y / 1080.0, 0.5);
                float2 texel = _CameraDepthTexture_TexelSize.xy;
                float2 o = texel * max(_InkWidth * px * 0.5, 0.5);
                float far = _PaperCameraParams.y * 0.98;

                float d0 = EyeDepth(SampleSceneDepth(uv));
                float dA = EyeDepth(SampleSceneDepth(uv + float2(o.x, o.y)));
                float dB = EyeDepth(SampleSceneDepth(uv - float2(o.x, o.y)));
                float dC = EyeDepth(SampleSceneDepth(uv + float2(o.x, -o.y)));
                float dD = EyeDepth(SampleSceneDepth(uv + float2(-o.x, o.y)));

                float3 n0 = SampleSceneNormals(uv);
                float3 nA = SampleSceneNormals(uv + float2(o.x, o.y));
                float3 nB = SampleSceneNormals(uv - float2(o.x, o.y));
                float3 nC = SampleSceneNormals(uv + float2(o.x, -o.y));
                float3 nD = SampleSceneNormals(uv + float2(-o.x, o.y));

                // Depth edges (silhouettes), with a higher threshold on surfaces seen at a grazing angle.
                float nearest = max(min(min(dA, dB), min(dC, dD)), 1e-3);
                float facing = saturate(abs(dot(n0, _PaperCameraForward.xyz)) * 2.0);
                float threshold = _DepthSensitivity * lerp(4.0, 1.0, facing);
                float dd = (abs(dA - dB) + abs(dC - dD)) / nearest;
                float depthEdge = smoothstep(threshold, threshold * 1.6, dd);

                // Normal edges (creases between faces), only where every sample is an object.
                float allSolid = step(max(max(dA, dB), max(dC, dD)), far);
                float nd = (1.0 - dot(nA, nB)) + (1.0 - dot(nC, nD));
                float normalEdge = smoothstep(_NormalThreshold, _NormalThreshold + 0.25, nd) * allSolid;

                float strength = saturate(_PaperStyleOn);
                float edge = saturate(max(depthEdge, normalEdge)) * _InkOpacity * strength;
                col.rgb = lerp(col.rgb, _InkColor.rgb, edge);

                // 2D view: hard drop shadow down and to the right, like paper stuck onto the page behind.
                if (_PaperCameraParams.z > 0.5)
                {
                    #if UNITY_UV_STARTS_AT_TOP
                        float up = -1.0;
                    #else
                        float up = 1.0;
                    #endif
                    float2 so = float2(-1.0, up) * texel * _Shadow2DOffset * px;
                    float dS = EyeDepth(SampleSceneDepth(uv + so));
                    float shadow = step(_Shadow2DGap, d0 - dS);
                    col.rgb *= 1.0 - shadow * _Shadow2DOpacity * strength;
                }

                float2 v = uv - 0.5;
                v.x *= _ScreenParams.x / _ScreenParams.y;
                col.rgb *= 1.0 - _Vignette * smoothstep(0.35, 1.0, length(v)) * strength;
                return col;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
