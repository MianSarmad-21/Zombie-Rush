// Fog curtain for the Zombie Rush zone edge (URP). A tall, nearly opaque wall of slowly
// rolling mist just outside the energy wall, so players can't see past the zone.
// Tinted by the scene's fog colour (_UseSceneFog) so it reads as thick haze where the
// world ends. uv: x = metres along the edge, y = metres up. _Fade is set by ZoneBoundaryFX.
Shader "ZombieRush/ZoneFogWall"
{
    Properties
    {
        _Color ("Mist Colour", Color) = (0.32, 0.33, 0.30, 1)
        [Toggle] _UseSceneFog ("Tint With Scene Fog Colour", Float) = 1
        _Opacity ("Opacity", Range(0, 1)) = 1
        _Height ("Wall Height (m)", Float) = 140
        _Scale ("Billow Size", Float) = 0.06
        _Speed ("Roll Speed", Float) = 0.04
        _Contrast ("Billow Contrast", Range(0, 1)) = 0.45
        _GroundDark ("Darker Near Ground", Range(0, 1)) = 0.35
        _Fade ("Fade", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+5" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ZoneFog"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _UseSceneFog;
                half _Opacity;
                float _Height;
                float _Scale;
                float _Speed;
                half _Contrast;
                half _GroundDark;
                half _Fade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float fogFactor : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash(i);
                float b = Hash(i + float2(1, 0));
                float c = Hash(i + float2(0, 1));
                float d = Hash(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float Fbm(float2 p)
            {
                float v = Noise(p) * 0.5;
                v += Noise(p * 2.03 + 17.1) * 0.3;
                v += Noise(p * 4.01 + 41.7) * 0.2;
                return v;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y * _Speed;
                float2 p = i.uv * _Scale;
                // Two layers drifting different ways: slow rolling billows.
                float n = Fbm(p + float2(t, -t * 0.6));
                n = lerp(n, Fbm(p * 2.2 + float2(-t * 1.4, t * 0.5) + n), 0.4);

                half3 mist = lerp(_Color.rgb, unity_FogColor.rgb, _UseSceneFog);
                half shade = 1.0 + (n - 0.5) * 2.0 * _Contrast;
                half ground = lerp(1.0 - _GroundDark, 1.0, saturate(i.uv.y / 10.0));
                half3 col = mist * shade * ground;

                float h = saturate(i.uv.y / max(_Height, 1.0));
                half alpha = _Opacity * saturate(0.9 + n * 0.25);
                alpha *= 1.0 - smoothstep(0.82, 1.0, h);      // melts into the sky at the very top
                alpha *= _Fade;

                col = MixFog(col, i.fogFactor);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
