// Writes depth only (no colour), before the opaque geometry: anything of the first-person gun and arms
// behind it fails the depth test and is not drawn, so the zoomed world shows through the scope's lens.
// Used by FirstPersonViewmodel on a disc in front of the arms-and-gun camera while looking through a scope.
Shader "ZombieRush/ScopeDepthMask"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry-100" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "DepthMask"
            ColorMask 0
            ZWrite On
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
