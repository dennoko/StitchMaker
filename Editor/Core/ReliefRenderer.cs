using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dennokoworks.StitchMaker
{
    /// <summary>
    /// 穴・沈み込みの描画準備（TextureBaker とサムネイルで共用）。
    /// 沈み込みフィールド（糸の縁からの距離で決まる深さ）と穴のテクスチャ（HoleDetector が見つけた先端に穴を描いたもの）を CPU で作ってキャッシュし、
    /// Bake シェーダーの Relief 系パスのプロパティを設定する。
    /// 穴・沈み込みは、ステッチの四角形を沈み込みの幅（または穴のはみ出し）だけ広げた「余白付きの四角形」のデカールで描く。
    /// </summary>
    public sealed class ReliefRenderer : IDisposable
    {
        public const int ReliefPass = 8, ReliefAOPass = 9, GrooveAOPass = 10, AOCompositePass = 11;
        // 沈み込みフィールドの最大辺（px）。深さはなだらかに変わるので粗くてよい。
        const int MaxFieldSize = 512;
        // 糸とみなすマスク値（被覆率 saturate(m × 20) が 1 になる値）。
        const float ThreadIso = 0.05f;
        // 穴のテクスチャの最大辺（px）と、穴の半径の最小 px（小さな穴は拡大して描く）。
        const int MaxHoleSize = 2048;
        const float MinHolePixels = 8f;
        // 穴の形（先端での糸の半径 r × 穴の大きさ s）: 半径 1.0·r·s（糸と同じ太さ）の円を、輪郭上の先端から外側へ 0.25·r·s の所に置く。
        // 糸の先端は穴の内側の縁に少し掛かるだけで、穴の大部分（先端から外へ 1.25·r·s）が影として見える。
        const float HoleRadius = 1.0f, HoleOffset = 0.25f;
        // 穴の深さの形: 中心から半径の 45% までは平らな底、そこから縁へなめらかに浅くなる。
        const float HoleFloor = 0.45f;

        static readonly int HoleTexId = Shader.PropertyToID("_HoleTex");
        static readonly int SinkTexId = Shader.PropertyToID("_SinkTex");
        static readonly int ReliefRectId = Shader.PropertyToID("_ReliefRect");
        static readonly int SinkRectId = Shader.PropertyToID("_SinkRect");
        static readonly int HoleRectId = Shader.PropertyToID("_HoleRect");
        static readonly int HoleDepthId = Shader.PropertyToID("_HoleDepth");
        static readonly int SinkDepthId = Shader.PropertyToID("_SinkDepth");
        static readonly int HoleScaleId = Shader.PropertyToID("_HoleScale");
        static readonly int SinkScaleId = Shader.PropertyToID("_SinkScale");

        struct Key : IEquatable<Key>
        {
            public int mask, maskVersion;
            public float length, width, sinkWidth;

            public bool Equals(Key other)
            {
                return mask == other.mask && maskVersion == other.maskVersion && length == other.length
                    && width == other.width && sinkWidth == other.sinkWidth;
            }

            public override bool Equals(object obj) => obj is Key other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = mask;
                    hash = hash * 31 + maskVersion;
                    hash = hash * 31 + length.GetHashCode();
                    hash = hash * 31 + width.GetHashCode();
                    return hash * 31 + sinkWidth.GetHashCode();
                }
            }
        }

        // 穴のテクスチャ。マスクの外へはみ出す穴のために余白を持つ。
        sealed class HoleField
        {
            public Texture2D texture;
            public Vector4 rect;    // マスク座標 → テクスチャ座標 = m * xy + zw
            public Vector2 margin;  // 余白（マスク座標。1 = マスクの幅・高さ）
        }

        readonly Material material;
        readonly Dictionary<Key, Texture2D> fields = new Dictionary<Key, Texture2D>();
        readonly HashSet<Key> used = new HashSet<Key>();
        readonly List<Key> unused = new List<Key>();
        // 穴のテクスチャ（マスク、マスクの版、穴の大きさ）。先端の無いマスクは null を入れておく。
        readonly Dictionary<(int, uint, float), HoleField> holes = new Dictionary<(int, uint, float), HoleField>();
        readonly HashSet<(int, uint, float)> usedHoles = new HashSet<(int, uint, float)>();
        readonly List<(int, uint, float)> unusedHoles = new List<(int, uint, float)>();

        /// <param name="material">Bake シェーダーのマテリアル（呼び出し側が所有する）。</param>
        public ReliefRenderer(Material material)
        {
            this.material = material;
        }

        public void Dispose()
        {
            foreach (var field in fields.Values) Release(field);
            fields.Clear();
            used.Clear();
            foreach (var hole in holes.Values) Release(hole);
            holes.Clear();
            usedHoles.Clear();
        }

        /// <summary>1 回の描画の開始。EndFrame までに使われなかった沈み込みフィールド・穴のテクスチャは EndFrame で解放する。</summary>
        public void BeginFrame()
        {
            used.Clear();
            usedHoles.Clear();
        }

        public void EndFrame()
        {
            unused.Clear();
            foreach (var key in fields.Keys)
            {
                if (!used.Contains(key)) unused.Add(key);
            }
            foreach (var key in unused)
            {
                Release(fields[key]);
                fields.Remove(key);
            }

            unusedHoles.Clear();
            foreach (var key in holes.Keys)
            {
                if (!usedHoles.Contains(key)) unusedHoles.Add(key);
            }
            foreach (var key in unusedHoles)
            {
                Release(holes[key]);
                holes.Remove(key);
            }
        }

        /// <summary>
        /// 穴か沈み込みがあれば Relief 系パスのプロパティを設定し、余白付きの四角形の長さ・幅（メートル）を size に返す。
        /// 穴の位置は HoleDetector がマスクから決める（先端が見つからないマスクでは穴なし）。
        /// マスクの読み戻しや沈み込みフィールドの生成で RenderTarget を変えるので、描画先を設定する前に呼ぶこと。
        /// </summary>
        public bool Prepare(StitchSettings settings, Texture2D mask, out Vector2 size)
        {
            size = Vector2.zero;
            if (mask == null) return false;
            var hole = settings.HasHole ? GetHoles(mask, settings.holeSize) : null;
            bool sink = settings.HasSink;
            if (hole == null && !sink) return false;

            float length = Mathf.Max(settings.ScaledLength, 1e-6f);
            float width = Mathf.Max(settings.ScaledWidth, 1e-6f);
            float sinkMargin = sink ? settings.ScaledSinkWidth : 0f;
            // 四角形の余白は、沈み込みの幅と穴のはみ出しの大きい方（縦横共通）。
            float margin = sinkMargin;
            if (hole != null) margin = Mathf.Max(margin, Mathf.Max(hole.margin.x * length, hole.margin.y * width));
            float paddedLength = length + 2f * margin;
            float paddedWidth = width + 2f * margin;
            size = new Vector2(paddedLength, paddedWidth);

            Texture sinkField = sink ? GetField(mask, length, width, sinkMargin) : (Texture)Texture2D.blackTexture;
            float sinkLength = length + 2f * sinkMargin;
            float sinkWidth = width + 2f * sinkMargin;

            // 凹みの高さの単位は糸と同じ「normalStrength × 幅」（深さ 1 = 糸の盛り上がりと同じ深さ）。
            // 勾配はそれぞれのテクスチャの座標あたりなので、テクスチャ 1 枚分の実寸で割る。
            float unit = settings.normalStrength * width;
            material.SetVector(ReliefRectId, ReliefRect(length, width, margin));
            material.SetTexture(HoleTexId, hole != null ? hole.texture : Texture2D.blackTexture);
            material.SetVector(HoleRectId, hole != null ? hole.rect : new Vector4(1f, 1f, 0f, 0f));
            material.SetFloat(HoleDepthId, hole != null ? settings.holeDepth : 0f);
            material.SetVector(HoleScaleId, hole != null
                ? new Vector4(unit * hole.rect.x / length, unit * hole.rect.y / width, 0f, 0f)
                : Vector4.zero);
            material.SetTexture(SinkTexId, sinkField);
            material.SetVector(SinkRectId, new Vector4(length / sinkLength, width / sinkWidth, sinkMargin / sinkLength, sinkMargin / sinkWidth));
            material.SetFloat(SinkDepthId, sink ? settings.sinkDepth : 0f);
            material.SetVector(SinkScaleId, new Vector4(unit / sinkLength, unit / sinkWidth, 0f, 0f));
            return true;
        }

        // 余白付きの四角形の座標（0-1）→ マスク座標 = uv * xy - zw。
        static Vector4 ReliefRect(float length, float width, float margin)
        {
            return new Vector4((length + 2f * margin) / length, (width + 2f * margin) / width, margin / length, margin / width);
        }

        // ---- 穴

        // マスクの先端に穴を描いたテクスチャ。先端が見つからなければ null。
        HoleField GetHoles(Texture2D mask, float size)
        {
            var key = (mask.GetInstanceID(), mask.updateCount, size);
            usedHoles.Add(key);
            if (holes.TryGetValue(key, out var cached) && (cached == null || cached.texture != null)) return cached;

            var detected = HoleDetector.Get(mask);
            var field = detected != null && detected.tips.Length > 0 ? BuildHoles(detected, size) : null;
            Release(cached);
            holes[key] = field;
            return field;
        }

        // 深さ 0-1 の穴を描く。中心付近は平らな底（1）で、縁へなめらかに 0 になる。重なりは深い方。
        static HoleField BuildHoles(HoleDetector.Result detected, float size)
        {
            int w = detected.width, h = detected.height;
            int count = detected.tips.Length;
            var centers = new Vector2[count];
            var radii = new float[count];
            float minRadius = float.MaxValue, overflowX = 0f, overflowY = 0f;
            for (int i = 0; i < count; i++)
            {
                var tip = detected.tips[i];
                radii[i] = HoleRadius * tip.radius * size;
                centers[i] = tip.position + tip.normal * (HoleOffset * tip.radius * size);
                minRadius = Mathf.Min(minRadius, radii[i]);
                overflowX = Mathf.Max(overflowX, Mathf.Max(radii[i] - centers[i].x, centers[i].x + radii[i] - w));
                overflowY = Mathf.Max(overflowY, Mathf.Max(radii[i] - centers[i].y, centers[i].y + radii[i] - h));
            }
            // 左右に繋がるマスクは、はみ出した穴を反対側に回り込ませて描くので横の余白は要らない。
            float padX = detected.tileable ? 0f : Mathf.Min(Mathf.Ceil(overflowX), Mathf.Max(w, h));
            float padY = Mathf.Min(Mathf.Ceil(overflowY), Mathf.Max(w, h));
            float spanX = w + 2f * padX, spanY = h + 2f * padY;

            // テクスチャの 1 px = 検出の 1 / scale px。
            float scale = Mathf.Clamp(MinHolePixels / Mathf.Max(minRadius, 1e-3f), 1f, 4f);
            scale = Mathf.Min(scale, MaxHoleSize / Mathf.Max(spanX, spanY));
            int tw = Mathf.Clamp(Mathf.CeilToInt(spanX * scale), 4, MaxHoleSize);
            int th = Mathf.Clamp(Mathf.CeilToInt(spanY * scale), 4, MaxHoleSize);

            var pixels = new Color[tw * th];
            float[] shifts = detected.tileable ? new[] { -w, 0f, w } : new[] { 0f };
            for (int i = 0; i < count; i++)
            {
                float radius = radii[i] * scale;
                foreach (float shift in shifts)
                {
                    float cx = (centers[i].x + shift + padX) * scale;
                    float cy = (centers[i].y + padY) * scale;
                    int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - radius)), x1 = Mathf.Min(tw - 1, Mathf.CeilToInt(cx + radius));
                    int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - radius)), y1 = Mathf.Min(th - 1, Mathf.CeilToInt(cy + radius));
                    for (int y = y0; y <= y1; y++)
                    {
                        for (int x = x0; x <= x1; x++)
                        {
                            float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                            float d = Mathf.Sqrt(dx * dx + dy * dy) / radius;
                            if (d >= 1f) continue;
                            float value = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(HoleFloor, 1f, d));
                            int index = y * tw + x;
                            if (value > pixels[index].r) pixels[index] = new Color(value, value, value, 1f);
                        }
                    }
                }
            }

            var format = SystemInfo.SupportsTextureFormat(TextureFormat.RHalf) ? TextureFormat.RHalf : TextureFormat.RGBA32;
            var texture = new Texture2D(tw, th, format, false, true)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixels(pixels);
            texture.Apply(false, true);

            // マスク座標 m（0-1）→ 検出 px = m·w → テクスチャ座標 = (m·w + padX)·scale / tw。
            return new HoleField
            {
                texture = texture,
                rect = new Vector4(w * scale / tw, h * scale / th, padX * scale / tw, padY * scale / th),
                margin = new Vector2(padX / w, padY / h),
            };
        }

        // ---- 沈み込み

        // 余白付きのキャンバス（ステッチ座標で (length + 2·margin) × (width + 2·margin)）に、糸の縁からの実寸の距離 d を求め、
        // 深さ = 0.5 + 0.5·cos(π·d / margin)（糸の縁で 1、margin 離れると 0。両端で傾きが 0 になる、なだらかな形）にする。
        // 糸の太さや沈み込みの幅に関係なく、糸の縁では常に最大の深さになる。
        Texture2D GetField(Texture2D mask, float length, float width, float margin)
        {
            var key = new Key
            {
                mask = mask.GetInstanceID(),
                maskVersion = (int)mask.updateCount,
                length = length,
                width = width,
                sinkWidth = margin,
            };
            used.Add(key);
            if (fields.TryGetValue(key, out var cached) && cached != null) return cached;

            var pixels = MaskReadback.Get(mask);
            float paddedLength = length + 2f * margin;
            float paddedWidth = width + 2f * margin;
            // 1 px の大きさ（メートル）: マスクの画素より細かくせず、最大辺は MaxFieldSize まで。
            float cell = Mathf.Max(Mathf.Max(paddedLength, paddedWidth) / MaxFieldSize,
                Mathf.Min(length / pixels.width, width / pixels.height));
            int w = Mathf.Clamp(Mathf.CeilToInt(paddedLength / cell), 4, MaxFieldSize);
            int h = Mathf.Clamp(Mathf.CeilToInt(paddedWidth / cell), 4, MaxFieldSize);
            float sx = paddedLength / w, sy = paddedWidth / h;

            // 糸の画素。左右に繋がるマスクは、余白に隣の目の糸を繰り返して置く（継ぎ目で沈み込みが途切れないように）。
            var thread = new bool[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = ((y + 0.5f) * sy - margin) / width;
                if (v < 0f || v > 1f) continue;
                for (int x = 0; x < w; x++)
                {
                    float u = ((x + 0.5f) * sx - margin) / length;
                    if (pixels.tileable) u -= Mathf.Floor(u);
                    else if (u < 0f || u > 1f) continue;
                    thread[y * w + x] = pixels.Sample(u, v) > ThreadIso;
                }
            }

            var distance = DistanceField.Compute(thread, w, h, sx, sy);
            var colors = new Color[w * h];
            for (int i = 0; i < colors.Length; i++)
            {
                float t = distance[i] / margin;
                float depth = t >= 1f ? 0f : 0.5f + 0.5f * Mathf.Cos(Mathf.PI * t);
                colors[i] = new Color(depth, depth, depth, 1f);
            }

            var format = SystemInfo.SupportsTextureFormat(TextureFormat.RHalf) ? TextureFormat.RHalf : TextureFormat.RGBA32;
            var field = new Texture2D(w, h, format, false, true)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            field.SetPixels(colors);
            field.Apply(false, true);

            if (cached != null) UnityEngine.Object.DestroyImmediate(cached);
            fields[key] = field;
            return field;
        }

        static void Release(Texture2D texture)
        {
            if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
        }

        static void Release(HoleField field)
        {
            if (field != null && field.texture != null) UnityEngine.Object.DestroyImmediate(field.texture);
        }
    }
}
