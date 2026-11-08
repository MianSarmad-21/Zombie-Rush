// Zombie Rush: a glow drawn over a zombie as a second material. It lights up the
// silhouette edges (rim) and adds a faint body glow, so zombies read clearly against the
// dark city. Additive and unlit: no lights or shadows needed, cheap on phones.
// Driven per zombie by ZombieGlow (pulse, frenzy flare, fade on death).
Shader "Zombie Rush/Zombie Rim Glow"
{
    Properties
    {
        [HDR] _RimColor ("Glow Colour", Color) = (0.35, 1.6, 0.2, 1)
        _RimPower ("Edge Sharpness", Range(0.5, 8)) = 2.6
        _Fill ("Body Glow", Range(0, 1)) = 0.1
        _RimIntensity ("Intensity", Range(0, 4)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ZombieRim"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _RimColor;
                half _RimPower;
                half _Fill;
                half _RimIntensity;
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
                float3 normalWS : TEXCOORD0;
                float3 viewWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.viewWS = GetWorldSpaceViewDir(p.positionWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half facing = saturate(dot(normalize(i.normalWS), normalize(i.viewWS)));
                half rim = pow(1.0h - facing, _RimPower);
                half3 c = _RimColor.rgb * (rim + _Fill) * _RimIntensity;
                return half4(c, 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
