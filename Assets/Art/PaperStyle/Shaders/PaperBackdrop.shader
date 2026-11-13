// Cross Section paper style: the far background (paper sky, cut-paper clouds and three layers of
// paper hills). PaperStyle draws it as one full-screen quad at the far plane for each camera, so it
// sits behind everything in the front view, the Q/E side views, the back view and the 3D view.
// It reads the camera from the view matrix: walking pans the hills (parallax) and turning the view
// rolls the panorama, so each side view gets its own skyline.
Shader "Paper/Backdrop"
{
    Properties
    {
        _SkyTop ("Sky top", Color) = (0.737, 0.816, 0.847, 1)
        _SkyBottom ("Sky at the horizon", Color) = (0.886, 0.906, 0.871, 1)
        _Hill1 ("Far hills", Color) = (0.784, 0.839, 0.812, 1)
        _Hill2 ("Middle hills", Color) = (0.706, 0.784, 0.749, 1)
        _Hill3 ("Near hills", Color) = (0.620, 0.718, 0.678, 1)
        _EdgeColor ("Cut-paper edge", Color) = (1, 1, 1, 0.5)
        _CloudColor ("Clouds", Color) = (0.976, 0.976, 0.961, 1)
        _CloudShadow ("Cloud shadow", Color) = (0.62, 0.68, 0.70, 0.45)
        _Horizon ("Horizon height (0..1 of the screen)", Range(0, 1)) = 0.4
        _Parallax ("Parallax", Float) = 0.03
        [NoScaleOffset] _GrainTex ("Grain", 2D) = "gray" {}
        _GrainStrength ("Grain", Range(0, 1)) = 0.07
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Background" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

        Pass
        {
            Name "Backdrop"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _SkyTop;
                half4 _SkyBottom;
                half4 _Hill1;
                half4 _Hill2;
                half4 _Hill3;
                half4 _EdgeColor;
                half4 _CloudColor;
                half4 _CloudShadow;
                float _Horizon;
                float _Parallax;
                half _GrainStrength;
            CBUFFER_END

            TEXTURE2D(_GrainTex);
            SAMPLER(sampler_GrainTex);

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 screenUV : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float2 xy = v.positionOS.xy * 2.0;                  // the quad is -0.5..0.5
                #if UNITY_REVERSED_Z
                    float z = 1e-6;
                #else
                    float z = 0.999999;
                #endif
                o.positionCS = float4(xy.x, xy.y * _ProjectionParams.x, z, 1.0);
                o.screenUV = xy * 0.5 + 0.5;                       // y = 1 at the top of the screen
                return o;
            }

            float Ridge(float x, float freq, float seed)
            {
                return 0.55 * sin(x * freq * 3.1 + seed)
                     + 0.30 * sin(x * freq * 7.3 + seed * 1.7)
                     + 0.15 * sin(x * freq * 13.1 + seed * 2.3);
            }

            half3 Hill(half3 col, float x, float y, float base, float amp, float freq, float seed, half3 hillColor)
            {
                float h = base + amp * Ridge(x, freq, seed);
                float aa = fwidth(y - h) + 1e-5;
                float inside = smoothstep(h + aa, h - aa, y);
                float edge = smoothstep(h - 0.007 - aa, h - 0.007 + aa, y) * inside;
                col = lerp(col, hillColor, inside);
                return lerp(col, _EdgeColor.rgb, edge * _EdgeColor.a);
            }

            float Pill(float2 p, float2 c, float2 halfSize)
            {
                float2 d = abs(p - c);
                d.x = max(d.x - (halfSize.x - halfSize.y), 0.0);
                return length(d) - halfSize.y;
            }

            half3 Cloud(half3 col, float2 p, float cx, float cy, float2 halfSize, float span)
            {
                float x = cx - span * floor(cx / span + 0.5);       // wrap around the panorama
                float2 c = float2(x, cy);
                float shadow = Pill(p, c + float2(0.008, -0.010), halfSize);
                float body = Pill(p, c, halfSize);
                float aa = fwidth(body) + 1e-5;
                col = lerp(col, _CloudShadow.rgb, smoothstep(aa, -aa, shadow) * _CloudShadow.a);
                return lerp(col, _CloudColor.rgb, smoothstep(aa, -aa, body));
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.screenUV;
                float aspect = _ScreenParams.x / _ScreenParams.y;

                // Camera from the view matrix, so every view (2D sides, back, 3D, aim) gets the right backdrop.
                float3 camRight = UNITY_MATRIX_V[0].xyz;
                float3 camForward = -UNITY_MATRIX_V[2].xyz;
                float yaw = atan2(camForward.x, camForward.z);
                float pitch = asin(clamp(camForward.y, -1.0, 1.0));
                float3 cam = _WorldSpaceCameraPos;
                float pan = dot(cam, camRight) * _Parallax + yaw * 1.6;
                float lift = unity_OrthoParams.w > 0.5 ? 0.0 : pitch * 0.9;
                float horizon = _Horizon - lift - cam.y * _Parallax * 0.2;

                float x = (uv.x - 0.5) * aspect;
                half3 col = lerp(_SkyBottom.rgb, _SkyTop.rgb, smoothstep(horizon - 0.05, 1.1, uv.y));

                float2 p = float2(x, uv.y);
                float span = aspect + 1.4;
                col = Cloud(col, p, 0.35 - pan * 0.25, horizon + 0.44, float2(0.15, 0.026), span);
                col = Cloud(col, p, -0.55 - pan * 0.25, horizon + 0.36, float2(0.10, 0.022), span);
                col = Cloud(col, p, 1.15 - pan * 0.25, horizon + 0.50, float2(0.12, 0.024), span);

                col = Hill(col, x + pan * 0.30, uv.y, horizon + 0.10, 0.060, 1.2, 1.0, _Hill1.rgb);
                col = Hill(col, x + pan * 0.55, uv.y, horizon + 0.02, 0.080, 0.9, 2.7, _Hill2.rgb);
                col = Hill(col, x + pan * 0.85, uv.y, horizon - 0.08, 0.070, 0.7, 4.1, _Hill3.rgb);

                half g = SAMPLE_TEXTURE2D(_GrainTex, sampler_GrainTex, float2(x, uv.y) * 2.5).r;
                col *= 1.0h + (g - 0.5h) * 2.0h * _GrainStrength;
                return half4(col, 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
