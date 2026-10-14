Shader "Hidden/dennokoworks/StitchMaker/Bake"
{
    Properties
    {
        _MainTex ("Source", 2D) = "black" {}
        _MaskTex ("Mask", 2D) = "black" {}
        _ValidTex ("Valid", 2D) = "black" {}
        _Color ("Thread Color", Color) = (1, 1, 1, 1)
        _HeightScale ("Height Scale", Float) = 1
        _NormalScale ("Normal Scale", Vector) = (1, 1, 0, 0)
        _GrooveDepth ("Groove Depth", Float) = 0.5
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    // UV (0-1) をそのままレンダーターゲット上の位置にする。
    float4 UvToClip(float2 uv)
    {
        float2 p = uv * 2.0 - 1.0;
    #if UNITY_UV_STARTS_AT_TOP
        p.y = -p.y;
    #endif
        return float4(p, 0.5, 1.0);
    }
    ENDCG

    SubShader
    {
        Cull Off

        // Pass 0: Decal（Color / Normal / Height の MRT。マスク値の高いステッチが勝つ）
        Pass
        {
            ZTest LEqual
            ZWrite On
            Blend Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            sampler2D _MaskTex;
            float4 _MaskTex_TexelSize;
            float4 _Color;
            float _HeightScale;
            float4 _NormalScale; // x: 長さ方向, y: 幅方向

            struct appdata
            {
                float4 vertex : POSITION;      // (uv.x, uv.y, 0)
                float3 stitch : TEXCOORD0;     // (マスク U, マスク V, 正規化深度)
                float3 tangentAxis : TEXCOORD1;   // ステッチ接線（メッシュ接空間）
                float3 bitangentAxis : TEXCOORD2; // ステッチ従法線（メッシュ接空間）
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 stitch : TEXCOORD0;
                float3 tangentAxis : TEXCOORD1;
                float3 bitangentAxis : TEXCOORD2;
            };

            struct fragOutput
            {
                float4 color : SV_Target0;
                float4 normal : SV_Target1;
                float4 height : SV_Target2;
                float depth : SV_Depth;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UvToClip(v.vertex.xy);
                o.stitch = v.stitch;
                o.tangentAxis = v.tangentAxis;
                o.bitangentAxis = v.bitangentAxis;
                return o;
            }

            fragOutput frag(v2f i)
            {
                float3 st = i.stitch;
                if (st.x < 0.0 || st.x > 1.0 || st.y < 0.0 || st.y > 1.0 || abs(st.z) > 1.0)
                    discard;

                float2 e = _MaskTex_TexelSize.xy;
                float m = tex2D(_MaskTex, st.xy).r;
                float ms = (tex2D(_MaskTex, st.xy + float2(e.x, 0)).r - tex2D(_MaskTex, st.xy - float2(e.x, 0)).r) / (2.0 * e.x);
                float mt = (tex2D(_MaskTex, st.xy + float2(0, e.y)).r - tex2D(_MaskTex, st.xy - float2(0, e.y)).r) / (2.0 * e.y);

                float3 n = float3(0, 0, 1)
                    - _NormalScale.x * ms * i.tangentAxis
                    - _NormalScale.y * mt * i.bitangentAxis;
                n = normalize(n);

                fragOutput o;
                o.color = float4(_Color.rgb, saturate(m * 20.0));
                o.normal = float4(n * 0.5 + 0.5, 1.0);
                o.height = float4(m * _HeightScale, 0, 0, 1);
            #if defined(UNITY_REVERSED_Z)
                o.depth = m;
            #else
                o.depth = 1.0 - m;
            #endif
                return o;
            }
            ENDCG
        }

        // Pass 1: IslandMask（UV アイランド内を 1 にする）
        Pass
        {
            ZTest Always
            ZWrite Off
            Blend Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct appdata
            {
                float2 uv : TEXCOORD0;
            };

            float4 vert(appdata v) : SV_POSITION
            {
                return UvToClip(v.uv);
            }

            float4 frag() : SV_Target
            {
                return float4(1, 1, 1, 1);
            }
            ENDCG
        }

        // Pass 2: Dilate（無効画素を有効な 8 近傍の平均で埋める。1 回で 1px 拡張）
        Pass
        {
            ZTest Always
            ZWrite Off
            Blend Off

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            sampler2D _ValidTex;

            float4 frag(v2f_img i) : SV_Target
            {
                float4 center = tex2Dlod(_MainTex, float4(i.uv, 0, 0));
                if (tex2Dlod(_ValidTex, float4(i.uv, 0, 0)).r > 0.5)
                    return center;

                // 中心は無効画素なので、3x3 をそのまま走査しても寄与しない。
                float4 sum = 0;
                float count = 0;
                for (int y = -1; y <= 1; y++)
                {
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 uv = i.uv + float2(x, y) * _MainTex_TexelSize.xy;
                        float valid = step(0.5, tex2Dlod(_ValidTex, float4(uv, 0, 0)).r);
                        sum += tex2Dlod(_MainTex, float4(uv, 0, 0)) * valid;
                        count += valid;
                    }
                }
                return count > 0.0 ? sum / count : center;
            }
            ENDCG
        }

        // Pass 3: Thumbnail（プリセット一覧用。布地色の上に Color を重ね、Normal で簡易ライティングする）
        Pass
        {
            ZTest Always
            ZWrite Off
            Blend Off

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            sampler2D _MainTex;   // Color
            sampler2D _NormalTex;

            float4 frag(v2f_img i) : SV_Target
            {
                float4 c = tex2D(_MainTex, i.uv);
                float3 n = normalize(tex2D(_NormalTex, i.uv).xyz * 2.0 - 1.0);

                // 左上からの平行光。平らな面で 1 になるよう L.z で割る。
                float3 l = normalize(float3(-0.45, 0.55, 0.7));
                float diffuse = saturate(dot(n, l)) / l.z;
                float specular = pow(saturate(dot(reflect(-l, n), float3(0, 0, 1))), 24.0) * 0.3;
                float shade = 0.35 + 0.65 * diffuse;
                float3 thread = c.rgb * shade + specular;

                // 白い糸も黒い糸も見える中間グレーの布地。溝が見えるよう布地にも陰を付ける。
                float3 fabric = 0.44;
            #if !defined(UNITY_COLORSPACE_GAMMA)
                fabric = GammaToLinearSpace(fabric);
            #endif
                return float4(lerp(fabric * shade, thread, c.a), 1.0);
            }
            ENDCG
        }

        // Pass 4: Groove（パスに沿った溝。Normal だけを凹ませ、Color の A と Height は布地のまま）
        // 頂点形式は Pass 0 と同じ。V（幅方向）の中央 Floor の範囲が平らな底で、その外側が両側の壁になる。
        // 深度はマスク値 GrooveLevel 相当。ステッチの余白（マスク値 0）には上書きされず、糸は上に乗る。
        Pass
        {
            ZTest LEqual
            ZWrite On
            Blend Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float4 _Color;
            float _GrooveDepth;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 stitch : TEXCOORD0;
                float3 tangentAxis : TEXCOORD1;
                float3 bitangentAxis : TEXCOORD2;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 stitch : TEXCOORD0;
                float3 bitangentAxis : TEXCOORD2;
            };

            struct fragOutput
            {
                float4 color : SV_Target0;
                float4 normal : SV_Target1;
                float4 height : SV_Target2;
                float depth : SV_Depth;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UvToClip(v.vertex.xy);
                o.stitch = v.stitch;
                o.bitangentAxis = v.bitangentAxis;
                return o;
            }

            fragOutput frag(v2f i)
            {
                float3 st = i.stitch;
                if (st.x < 0.0 || st.x > 1.0 || st.y < 0.0 || st.y > 1.0 || abs(st.z) > 1.0)
                    discard;

                const float Floor = 0.4;
                // 中心からの距離（0 = 中心、1 = 溝の縁）。壁の高さは smoothstep で縁へ向かって上がる。
                float y = abs(st.y * 2.0 - 1.0);
                float s = saturate((y - Floor) / (1.0 - Floor));
                float slope = _GrooveDepth * 6.0 * s * (1.0 - s) / (1.0 - Floor) * sign(st.y - 0.5);
                float3 n = normalize(float3(0, 0, 1) - slope * i.bitangentAxis);

                const float GrooveLevel = 0.001;
                fragOutput o;
                // RGB は Decal パスの余白と同じく糸色で埋める（バイリニア補間で黒フチを出さない）。
                o.color = float4(_Color.rgb, 0);
                o.normal = float4(n * 0.5 + 0.5, 1.0);
                o.height = float4(0, 0, 0, 1);
            #if defined(UNITY_REVERSED_Z)
                o.depth = GrooveLevel;
            #else
                o.depth = 1.0 - GrooveLevel;
            #endif
                return o;
            }
            ENDCG
        }
    }
}
