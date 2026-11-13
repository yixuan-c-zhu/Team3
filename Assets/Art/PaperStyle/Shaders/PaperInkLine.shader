// Cross Section paper style: dashed "cut along here" lines for LineRenderers (the C cut line, the
// AimZone target outline and the guide on the cutting mat). _BaseColor is the line's main colour;
// AimZone tints it (glass colour while aiming, glow, white flash) through Material.color.
// Dash units: World (LineRenderer Texture Mode = Tile) or Screen Pixels (any mode; assumes the line
// sits at one depth, like the cut line on the near plane).
Shader "Paper/InkLine"
{
    Properties
    {
        [MainColor] _BaseColor ("Line color", Color) = (0.133, 0.157, 0.227, 1)
        _RimColor ("Rim color", Color) = (0.133, 0.157, 0.227, 1)
        _RimWidth ("Rim (fraction of the width)", Range(0, 0.5)) = 0
        _DashLength ("Dash length", Float) = 0.45
        _GapLength ("Gap length", Float) = 0.3
        [Enum(World,0,ScreenPixels,1)] _DashSpace ("Dash units", Float) = 0
        _DashScroll ("Dash scroll speed", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "InkLine"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _RimColor;
                half _RimWidth;
                float _DashLength;
                float _GapLength;
                float _DashSpace;
                float _DashScroll;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float along = i.uv.x;
                float perPixel = length(float2(ddx(i.uv.x), ddy(i.uv.x)));
                if (_DashSpace > 0.5) along = i.uv.x / max(perPixel, 1e-7);
                along += _Time.y * _DashScroll;

                float period = max(_DashLength + _GapLength, 1e-4);
                float f = along - period * floor(along / period);
                float aa = fwidth(along) + 1e-5;
                half dash = (half)(smoothstep(0.0, aa, f) * (1.0 - smoothstep(_DashLength - aa, _DashLength + aa, f)));
                if (_GapLength <= 0.0) dash = 1.0h;

                float across = abs(i.uv.y - 0.5) * 2.0;
                float aaY = fwidth(across) + 1e-5;
                half rim = _RimWidth > 0.001h ? (half)smoothstep(1.0 - _RimWidth * 2.0 - aaY, 1.0 - _RimWidth * 2.0 + aaY, across) : 0.0h;

                half3 col = lerp(_BaseColor.rgb, _RimColor.rgb, rim) * i.color.rgb;
                half a = _BaseColor.a * i.color.a * dash;
                return half4(col, a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
