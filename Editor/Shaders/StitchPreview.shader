Shader "Hidden/dennokoworks/StitchMaker/Preview"
{
    Properties
    {
        _ColorTex ("Color", 2D) = "black" {}
        _NormalTex ("Normal", 2D) = "bump" {}
        _HeightTex ("Height", 2D) = "black" {}
        _Mode ("Mode", Float) = 0
        _CameraPosition ("Camera Position", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        // 対象メッシュの上に重ね描きする。
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZTest LEqual
            ZWrite Off
            Cull Back
            Offset -1, -1

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _ColorTex;
            sampler2D _NormalTex;
            sampler2D _HeightTex;
            float _Mode; // 0: Combined, 1: Normal, 2: Height
            float4 _CameraPosition;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
                float3 worldTangent : TEXCOORD3;
                float3 worldBitangent : TEXCOORD4;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.worldPos = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0)).xyz;
                // meshToWorld は回転と平行移動のみなので、法線もそのまま回せる。
                o.worldNormal = mul((float3x3)unity_ObjectToWorld, v.normal);
                o.worldTangent = mul((float3x3)unity_ObjectToWorld, v.tangent.xyz);
                o.worldBitangent = cross(o.worldNormal, o.worldTangent) * v.tangent.w;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 color = tex2D(_ColorTex, i.uv);
                float3 n = tex2D(_NormalTex, i.uv).xyz * 2.0 - 1.0;
                float height = tex2D(_HeightTex, i.uv).r;

                float3 worldNormal = normalize(
                    i.worldTangent * n.x + i.worldBitangent * n.y + i.worldNormal * n.z);
                float3 toCamera = normalize(_CameraPosition.xyz - i.worldPos);
                float lit = 0.35 + 0.65 * saturate(dot(worldNormal, toCamera));

                if (_Mode < 0.5)
                {
                    // 糸の無い溝は、平らな面より暗くなる分を黒い陰として重ねる。
                    float flatLit = 0.35 + 0.65 * saturate(dot(normalize(i.worldNormal), toCamera));
                    float shadow = saturate((1.0 - lit / flatLit) * 2.0);
                    float alpha = color.a + shadow * (1.0 - color.a);
                    return float4(color.rgb * lit * color.a / max(alpha, 1e-4), alpha);
                }
                if (_Mode < 1.5)
                    return float4(lit * 0.7, lit * 0.7, lit * 0.7, 1.0);
                return float4(height, height, height, 1.0);
            }
            ENDCG
        }
    }
}
