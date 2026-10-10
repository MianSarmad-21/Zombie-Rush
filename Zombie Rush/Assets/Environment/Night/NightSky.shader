// Unlit skybox that maps an equirectangular (lat-long) panorama onto the sky.
// Same mapping as Unity's Skybox/Panoramic but trimmed to what this game needs
// (one texture, tint, exposure, rotation) so it stays cheap on phones.
Shader "ZombieRush/NightSky"
{
    Properties
    {
        _MainTex ("Sky Panorama (lat-long)", 2D) = "black" {}
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Exposure ("Exposure", Range(0, 4)) = 1
        _Rotation ("Rotation", Range(0, 360)) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            half4 _Tint;
            half _Exposure;
            float _Rotation;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float3 RotateAroundY(float3 v, float degrees)
            {
                float a = degrees * UNITY_PI / 180.0;
                float s, c;
                sincos(a, s, c);
                return float3(c * v.x - s * v.z, v.y, s * v.x + c * v.z);
            }

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(RotateAroundY(v.vertex.xyz, _Rotation));
                o.dir = v.vertex.xyz;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float longitude = atan2(d.z, d.x);
                float latitude = acos(d.y);
                float2 uv = float2(0.5, 1.0) - float2(longitude, latitude) * float2(0.5 / UNITY_PI, 1.0 / UNITY_PI);
                half3 c = tex2D(_MainTex, uv).rgb * _Tint.rgb * _Exposure;
                return half4(c, 1);
            }
            ENDCG
        }
    }

    Fallback Off
}
