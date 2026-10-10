// Safe-zone boundary for Zombie Rush (URP). Additive energy wall: bright base line,
// hazard stripes near the ground, a faint 2 m grid, and scan bands rising up the wall,
// fading out with height. _IsGround switches to the flat glowing band on the ground.
// _Proximity (0..1) warms it to the danger colour, _Flash pulses the reward colour,
// _Fade fades it in - all set per frame by ZoneBoundaryFX.
Shader "ZombieRush/ZoneWall"
{
    Properties
    {
        [HDR] _Color ("Zone Colour", Color) = (1.0, 0.55, 0.12, 1)
        [HDR] _DangerColor ("Danger Colour (player near)", Color) = (1.0, 0.08, 0.04, 1)
        [HDR] _FlashColor ("Expand Flash Colour", Color) = (1.0, 0.85, 0.35, 1)
        _Intensity ("Intensity", Range(0, 6)) = 2.2
        _WallHeight ("Wall Height (m)", Float) = 6
        _StripeHeight ("Hazard Stripe Height (m)", Float) = 1.1
        _ScrollSpeed ("Scroll Speed", Float) = 0.35
        [Toggle] _IsGround ("Ground Band", Float) = 0
        _Proximity ("Proximity", Range(0, 1)) = 0
        _Flash ("Flash", Range(0, 1)) = 0
        _Fade ("Fade", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ZoneWall"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _DangerColor;
                half4 _FlashColor;
                half _Intensity;
                float _WallHeight;
                float _StripeHeight;
                float _ScrollSpeed;
                half _IsGround;
                half _Proximity;
                half _Flash;
                half _Fade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0; // x = metres around the ring, y = metres up the wall (or -1..1 across the ground band)
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

            // Anti-aliased 0..1 square wave.
            float Band(float x, float width)
            {
                float f = frac(x);
                float aa = max(fwidth(x), 1e-4) * 1.5;
                return smoothstep(0.0, aa, f) * (1.0 - smoothstep(width, width + aa, f));
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                float u = i.uv.x;
                float alpha;

                if (_IsGround > 0.5)
                {
                    float across = abs(i.uv.y);
                    float core = exp(-across * 7.0);                       // hot line under the wall
                    float glow = exp(-across * 2.2) * 0.35;                // soft spill on the ground
                    float dashes = Band(u * 0.5 - t * _ScrollSpeed, 0.5) * step(across, 0.45) * 0.35;
                    alpha = core + glow + dashes;
                    alpha *= smoothstep(1.0, 0.75, across);
                }
                else
                {
                    float y = max(i.uv.y, 0.0);
                    float h = saturate(y / _WallHeight);
                    float fade = pow(1.0 - h, 2.0);

                    float baseLine = exp(-y * 5.0) * 1.4;
                    float stripes = Band((u + y) * 0.5 - t * _ScrollSpeed, 0.45) * (1.0 - smoothstep(_StripeHeight * 0.6, _StripeHeight, y)) * 0.55;
                    float grid = (Band(u * 0.5 + 0.5, 0.03) * 0.5 + Band(y * 0.5, 0.04) * 0.35) * fade;
                    float scan = exp(-pow((h - frac(t * 0.22)) * 14.0, 2.0)) * 0.6 * fade;
                    float sheen = 0.16 * fade;
                    float flicker = 0.92 + 0.08 * sin(t * 21.0 + u * 1.7);

                    alpha = (baseLine + stripes + grid + scan + sheen) * flicker;
                    alpha *= saturate(fade * 1.6 + baseLine);
                }

                half3 tint = lerp(_Color.rgb, _DangerColor.rgb, _Proximity);
                tint = lerp(tint, _FlashColor.rgb, _Flash);
                float pulse = 1.0 + _Proximity * (0.6 + 0.4 * sin(t * 9.0)) + _Flash * 1.5;

                half3 col = tint * _Intensity * pulse * alpha * _Fade;
                col = MixFogColor(col, half3(0, 0, 0), i.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
