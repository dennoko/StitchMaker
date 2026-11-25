Shader "Hidden/dennokoworks/StitchMaker/Bake"
{
    Properties
    {
        _MainTex ("Source", 2D) = "black" {}
        _Factor ("Downsample Factor", Float) = 2
        _MaskTex ("Mask", 2D) = "black" {}
        _ValidTex ("Valid", 2D) = "black" {}
        _Color ("Thread Color", Color) = (1, 1, 1, 1)
        _HeightScale ("Height Scale", Float) = 1
        _NormalScale ("Normal Scale", Vector) = (1, 1, 0, 0)
        _GrooveDepth ("Groove Depth", Float) = 0.5
        _HoleTex ("Hole Field", 2D) = "black" {}
        _SinkTex ("Sink Field", 2D) = "black" {}
        _CoverageTex ("Coverage", 2D) = "black" {}
        _AOTex ("AO", 2D) = "white" {}
        _ReliefRect ("Relief Rect", Vector) = (1, 1, 0, 0)
        _SinkRect ("Sink Rect", Vector) = (1, 1, 0, 0)
        _HoleRect ("Hole Rect", Vector) = (1, 1, 0, 0)
        _HoleDepth ("Hole Depth", Float) = 0
        _SinkDepth ("Sink Depth", Float) = 0
        _HoleScale ("Hole Normal Scale", Vector) = (1, 1, 0, 0)
        _SinkScale ("Sink Normal Scale", Vector) = (1, 1, 0, 0)
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

    // Blit 系のパス（Dilate / Thumbnail / Downsample）の入力。
    sampler2D _MainTex;
    float4 _MainTex_TexelSize;

    // Downsample（Pass 5〜7）用。出力 1 画素に対応する _Factor × _Factor 個の作業画素を、テクセル中心で読む。
    float _Factor;

    float4 DownsampleTap(float2 uv, int x, int y)
    {
        float2 offset = float2(x, y) - (_Factor - 1.0) * 0.5;
        return tex2Dlod(_MainTex, float4(uv + offset * _MainTex_TexelSize.xy, 0, 0));
    }

    // 糸の余白（マスク値 0）より手前、糸（マスク値 > 0）より奥に置く凹み（溝・穴・沈み込み）の深度。
    // 傾きが大きいほど手前にし、凹み同士が重なる所では傾きの大きい方が勝つ。
    float ReliefLevel(float slope)
    {
        return 0.0005 + 0.001 * saturate(slope);
    }

    // 溝の底での AO の濃さ（× 溝の深さ）。
    static const float GrooveOcclusion = 0.5;

    // ---- 穴・沈み込み（Pass 8 / 9）
    // ステッチ座標（余白付きの四角形の 0-1）→ マスク座標 = uv * _ReliefRect.xy - _ReliefRect.zw。
    float4 _ReliefRect;
    // マスク座標 → 沈み込みフィールド / 穴のテクスチャの座標 = m * xy + zw（どちらもマスクの外に余白を持つ）。
    float4 _SinkRect;
    float4 _HoleRect;
    sampler2D _HoleTex;
    float4 _HoleTex_TexelSize;
    sampler2D _SinkTex;
    float4 _SinkTex_TexelSize;
    float _HoleDepth;
    float _SinkDepth;
    float4 _HoleScale;  // 穴の勾配（穴のテクスチャ座標あたり）→ 法線の傾き。x: 長さ方向, y: 幅方向
    float4 _SinkScale;  // 沈み込みの勾配（沈み込みフィールド座標あたり）→ 法線の傾き

    // 凹みの深さ（0 = 布地、1 = 「深さ」スライダー 1 相当）と、法線 = normalize((0,0,1) + grad.x·T + grad.y·B) の grad。
    struct Relief
    {
        float depth;
        float2 grad;
    };

    float Inside01(float2 t)
    {
        return step(0.0, t.x) * step(t.x, 1.0) * step(0.0, t.y) * step(t.y, 1.0);
    }

    // 沈み込みフィールド（糸の縁で 1、沈み込みの幅だけ離れると 0。ReliefRenderer が CPU で作る）
    float SinkField(float2 t)
    {
        return tex2Dlod(_SinkTex, float4(t, 0, 0)).r * Inside01(t);
    }

    float HoleValue(float2 t)
    {
        return tex2Dlod(_HoleTex, float4(t, 0, 0)).r * Inside01(t);
    }

    Relief EvalRelief(float2 uv)
    {
        float2 m = uv * _ReliefRect.xy - _ReliefRect.zw;

        float2 su = m * _SinkRect.xy + _SinkRect.zw;
        float2 e = _SinkTex_TexelSize.xy;
        float sinkDepth = _SinkDepth * SinkField(su);
        float fu = (SinkField(su + float2(e.x, 0)) - SinkField(su - float2(e.x, 0))) / (2.0 * e.x);
        float fv = (SinkField(su + float2(0, e.y)) - SinkField(su - float2(0, e.y))) / (2.0 * e.y);

        float2 hu = m * _HoleRect.xy + _HoleRect.zw;
        float2 h = _HoleTex_TexelSize.xy;
        float holeDepth = _HoleDepth * HoleValue(hu);
        float hs = (HoleValue(hu + float2(h.x, 0)) - HoleValue(hu - float2(h.x, 0))) / (2.0 * h.x);
        float ht = (HoleValue(hu + float2(0, h.y)) - HoleValue(hu - float2(0, h.y))) / (2.0 * h.y);

        // 深い方の凹みの勾配を使う（2 つの凹みは足し合わせない）。
        Relief r;
        if (sinkDepth >= holeDepth)
        {
            r.depth = sinkDepth;
            r.grad = _SinkDepth * float2(fu * _SinkScale.x, fv * _SinkScale.y);
        }
        else
        {
            r.depth = holeDepth;
            r.grad = _HoleDepth * float2(hs * _HoleScale.x, ht * _HoleScale.y);
        }
        return r;
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

            // _MainTex = Color
            sampler2D _NormalTex;
            sampler2D _AOTex;

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
                // AO は糸の部分で 1 なので、布地の陰影にだけ掛かる（穴・沈み込み・溝の影）。
                float ao = tex2D(_AOTex, i.uv).r;
                return float4(lerp(fabric * shade * ao, thread, c.a), 1.0);
            }
            ENDCG
        }

        // Pass 4: Groove（パスに沿った溝。Normal だけを凹ませ、Color の A と Height は布地のまま）
        // 頂点形式は Pass 0 と同じ。V（幅方向）の中央 Floor の範囲が平らな底で、その外側が両側の壁になる。
        // 深度は傾きに応じた ReliefLevel。ステッチの余白（マスク値 0）には上書きされず、糸は上に乗る。
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

                float level = ReliefLevel(abs(slope));
                fragOutput o;
                // RGB は Decal パスの余白と同じく糸色で埋める（バイリニア補間で黒フチを出さない）。
                o.color = float4(_Color.rgb, 0);
                o.normal = float4(n * 0.5 + 0.5, 1.0);
                o.height = float4(0, 0, 0, 1);
            #if defined(UNITY_REVERSED_Z)
                o.depth = level;
            #else
                o.depth = 1.0 - level;
            #endif
                return o;
            }
            ENDCG
        }

        // Pass 5: DownsampleColor（被覆率で重み付けした平均。被覆の無いブロックは糸色の余白を保つよう単純平均）
        Pass
        {
            ZTest Always
            ZWrite Off
            Blend Off

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            float4 frag(v2f_img i) : SV_Target
            {
                int n = (int)_Factor;
                float3 weighted = 0;
                float4 sum = 0;
                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        float4 c = DownsampleTap(i.uv, x, y);
                        weighted += c.rgb * c.a;
                        sum += c;
                    }
                }
                float3 rgb = sum.a > 1e-4 ? weighted / sum.a : sum.rgb / (n * n);
                return float4(rgb, sum.a / (n * n));
            }
            ENDCG
        }

        // Pass 6: DownsampleNormal（アンパックして平均し、再正規化する）
        Pass
        {
            ZTest Always
            ZWrite Off
            Blend Off

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            float4 frag(v2f_img i) : SV_Target
            {
                int n = (int)_Factor;
                float3 sum = 0;
                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < n; x++)
                        sum += DownsampleTap(i.uv, x, y).xyz * 2.0 - 1.0;
                }
                float3 normal = dot(sum, sum) > 1e-8 ? normalize(sum) : float3(0, 0, 1);
                return float4(normal * 0.5 + 0.5, 1.0);
            }
            ENDCG
        }

        // Pass 7: DownsampleHeight（算術平均）
        Pass
        {
            ZTest Always
            ZWrite Off
            Blend Off

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            float4 frag(v2f_img i) : SV_Target
            {
                int n = (int)_Factor;
                float sum = 0;
                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < n; x++)
                        sum += DownsampleTap(i.uv, x, y).r;
                }
                return float4(sum / (n * n), 0, 0, 1);
            }
            ENDCG
        }

        // Pass 8: Relief（穴・沈み込みの Normal。頂点形式は Pass 0 と同じで、ステッチ座標は余白付きの四角形を表す）
        // 深度は溝と同じ帯（ReliefLevel）に置き、傾きの大きい方が勝つ。糸（マスク値 > 0）は常に上に乗る。
        Pass
        {
            ZTest LEqual
            ZWrite On
            Blend Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            float4 _Color;

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

                Relief r = EvalRelief(st.xy);
                // 凹みの無い画素は書かない（溝や他の目の凹みを平らな法線で上書きしない）。
                if (r.depth <= 0.0)
                    discard;

                float3 n = normalize(float3(0, 0, 1) + r.grad.x * i.tangentAxis + r.grad.y * i.bitangentAxis);
                float level = ReliefLevel(length(r.grad));

                fragOutput o;
                // 溝と同じく、RGB は糸色・被覆率 0・高さ 0（布地）。
                o.color = float4(_Color.rgb, 0);
                o.normal = float4(n * 0.5 + 0.5, 1.0);
                o.height = float4(0, 0, 0, 1);
            #if defined(UNITY_REVERSED_Z)
                o.depth = level;
            #else
                o.depth = 1.0 - level;
            #endif
                return o;
            }
            ENDCG
        }

        // Pass 9: ReliefAO（穴・沈み込みの AO。暗い方が勝つよう最小値でブレンドし、描く順番に依存しない）
        Pass
        {
            ZTest Always
            ZWrite Off
            BlendOp Min
            Blend One One

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            struct appdata
            {
                float4 vertex : POSITION;
                float3 stitch : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 stitch : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UvToClip(v.vertex.xy);
                o.stitch = v.stitch;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 st = i.stitch;
                if (st.x < 0.0 || st.x > 1.0 || st.y < 0.0 || st.y > 1.0 || abs(st.z) > 1.0)
                    discard;
                Relief r = EvalRelief(st.xy);
                return float4(1.0 - saturate(r.depth), 0, 0, 1);
            }
            ENDCG
        }

        // Pass 10: GrooveAO（溝の AO。底を最も暗くし、壁で布地の明るさへ戻す。ブレンドは Pass 9 と同じ）
        Pass
        {
            ZTest Always
            ZWrite Off
            BlendOp Min
            Blend One One

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float _GrooveDepth;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 stitch : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 stitch : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UvToClip(v.vertex.xy);
                o.stitch = v.stitch;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 st = i.stitch;
                if (st.x < 0.0 || st.x > 1.0 || st.y < 0.0 || st.y > 1.0 || abs(st.z) > 1.0)
                    discard;

                const float Floor = 0.4;
                float y = abs(st.y * 2.0 - 1.0);
                float wall = smoothstep(0.0, 1.0, saturate((y - Floor) / (1.0 - Floor)));
                float ao = 1.0 - GrooveOcclusion * _GrooveDepth * (1.0 - wall);
                return float4(saturate(ao), 0, 0, 1);
            }
            ENDCG
        }

        // Pass 11: AOComposite（糸の部分は AO を掛けない。_MainTex = AO、_CoverageTex = Color（A = 被覆率））
        Pass
        {
            ZTest Always
            ZWrite Off
            Blend Off

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            sampler2D _CoverageTex;

            float4 frag(v2f_img i) : SV_Target
            {
                float ao = tex2Dlod(_MainTex, float4(i.uv, 0, 0)).r;
                float coverage = tex2Dlod(_CoverageTex, float4(i.uv, 0, 0)).a;
                return float4(lerp(ao, 1.0, coverage), 0, 0, 1);
            }
            ENDCG
        }
    }
}
