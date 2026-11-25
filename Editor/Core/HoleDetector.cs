using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dennokoworks.StitchMaker
{
    /// <summary>
    /// マスクから、糸が革に入る点（糸の端・折り返しの頂点）を探す。
    /// 糸の輪郭（被覆率のしきい値の等値線）を辿り、糸の太さ程度の区間で外側へ大きく曲がる所（丸い先端や V 字の頂点）を先端とする。
    /// 横へ通り抜ける糸の脇や、糸同士が交わる所の凹んだ角は先端にならない。結果はマスクごとにキャッシュする。
    /// </summary>
    public static class HoleDetector
    {
        public struct Tip
        {
            public Vector2 position;  // 輪郭上の先端（px。テクスチャの左下が原点）
            public Vector2 normal;    // 糸の外側（先端の先）の向き
            public float radius;      // 先端での糸の半径（px）
        }

        public sealed class Result
        {
            public int width, height;  // 検出に使った解像度（大きなマスクは縮小する）
            public bool tileable;      // 左右の端に糸がある（左右に繋がる）マスク
            public Tip[] tips;
        }

        // 被覆率 saturate(m × 20) が 1 になるマスク値（シェーダーの被覆率と揃える）。
        const float Iso = 0.05f;
        // 糸の太さ程度の区間（半円の弧の長さ）で、輪郭の向きがこれ以上変わる所を先端とする。
        // 丸い端は 180°、V 字の頂点は (180° − 腕の開き) になり、糸の脇のうねりや緩いカーブは届かない。
        const float MinTurn = 100f * Mathf.Deg2Rad;
        const float Step = 0.5f;   // 輪郭の再サンプル間隔（px）

        struct Node
        {
            public Vector2 point;
            public int a, b;
        }

        static readonly Dictionary<int, (uint version, Result result)> cache = new Dictionary<int, (uint, Result)>();

        /// <summary>マスクの先端を返す（キャッシュ付き）。マスクを読み戻すので、描画先を設定する前に呼ぶこと。</summary>
        public static Result Get(Texture2D mask)
        {
            if (mask == null) return null;
            int id = mask.GetInstanceID();
            if (cache.TryGetValue(id, out var cached) && cached.version == mask.updateCount) return cached.result;
            if (cache.Count > 64) cache.Clear();
            var pixels = MaskReadback.Get(mask);
            var result = Detect(pixels.values, pixels.width, pixels.height);
            cache[id] = (mask.updateCount, result);
            return result;
        }

        public static bool HasTips(Texture2D mask)
        {
            var result = Get(mask);
            return result != null && result.tips.Length > 0;
        }

        static Result Detect(float[] mask, int w, int h)
        {
            var result = new Result { width = w, height = h, tips = Array.Empty<Tip>() };
            // 左右の端に糸があるマスク（MaskReadback と同じ判定）は 3 つ横に並べて調べ、真ん中の分だけを使う。
            bool tile = false;
            for (int y = 0; y < h && !tile; y++)
                tile = mask[y * w] > 0.02f || mask[y * w + w - 1] > 0.02f;
            result.tileable = tile;

            int copies = tile ? 3 : 1;
            int gw = w * copies + 2, gh = h + 2;   // 外周を 0 で 1px 囲み、輪郭を必ず閉じる
            var img = new float[gw * gh];
            for (int y = 0; y < h; y++)
                for (int c = 0; c < copies; c++)
                    Array.Copy(mask, y * w, img, (y + 1) * gw + c * w + 1, w);

            var background = new bool[gw * gh];
            for (int i = 0; i < img.Length; i++) background[i] = img[i] <= Iso;
            var dist = DistanceField.Compute(background, gw, gh);
            float threadRadius = TypicalRadius(dist, gw, gh);
            if (threadRadius <= 0f) return result;

            var tips = new List<Tip>();
            foreach (var loop in Contours(img, gw, gh))
                FindTips(loop, img, dist, gw, gh, w, h, tile, threadRadius, tips);
            result.tips = tips.ToArray();
            return result;
        }

        // 糸の典型的な半径 = 背景までの距離の尾根（局所最大）の中央値。
        static float TypicalRadius(float[] dist, int gw, int gh)
        {
            var ridge = new List<float>();
            for (int y = 1; y < gh - 1; y++)
            {
                for (int x = 1; x < gw - 1; x++)
                {
                    float d = dist[y * gw + x];
                    if (d < 1.5f) continue;
                    bool peak = true;
                    for (int dy = -1; dy <= 1 && peak; dy++)
                        for (int dx = -1; dx <= 1 && peak; dx++)
                            peak = dist[(y + dy) * gw + x + dx] <= d;
                    if (peak) ridge.Add(d);
                }
            }
            if (ridge.Count == 0) return 0f;
            ridge.Sort();
            return ridge[ridge.Count / 2];
        }

        // ---- 先端の検出

        static void FindTips(Vector2[] loop, float[] img, float[] dist, int gw, int gh, int w, int h, bool tile,
            float threadRadius, List<Tip> tips)
        {
            var p = Resample(loop, Step, out float ds);
            int n = p.Length;
            // 窓 = 先端の前後 K サンプル（全体で半径 threadRadius の半円の弧の長さ）
            int K = Mathf.Max(Mathf.RoundToInt(0.5f * Mathf.PI * threadRadius / ds), 2);
            if (2 * K + 1 >= n) return;
            Smooth(p, 1f / ds);

            // 前景（糸）が進行方向の左になる向きに揃える。
            float vote = 0f;
            for (int k = 0; k < n; k += Mathf.Max(n / 64, 1))
            {
                var t = (p[(k + 1) % n] - p[k]).normalized;
                var left = new Vector2(-t.y, t.x);
                vote += Sample(img, gw, gh, p[k] + left) - Sample(img, gw, gh, p[k] - left);
            }
            if (vote < 0f) Array.Reverse(p);

            // 各点の前後 K サンプルでの接線の向きの変化（左 = 外へ凸に曲がるとき正）
            var turn = new float[n];
            for (int i = 0; i < n; i++)
            {
                var t0 = p[(i + 1) % n] - p[i];
                var t1 = p[(i + 2) % n] - p[(i + 1) % n];
                turn[i] = Mathf.DeltaAngle(Mathf.Atan2(t0.y, t0.x) * Mathf.Rad2Deg, Mathf.Atan2(t1.y, t1.x) * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            }
            var cumulative = new double[3 * n + 1];
            for (int k = 0; k < 3 * n; k++) cumulative[k + 1] = cumulative[k] + turn[k % n];
            var tau = new float[n];
            var order = new int[n];
            var keys = new float[n];
            for (int i = 0; i < n; i++)
            {
                tau[i] = (float)(cumulative[i + n + K] - cumulative[i + n - K]);
                order[i] = i;
                keys[i] = -tau[i];
            }
            Array.Sort(keys, order);

            var taken = new List<int>();
            foreach (int i in order)
            {
                if (tau[i] < MinTurn) break;
                bool near = false;
                foreach (int j in taken)
                {
                    int gap = Math.Abs(i - j);
                    if (Math.Min(gap, n - gap) < 2 * K) { near = true; break; }
                }
                if (near) continue;
                taken.Add(i);

                // 先端 = 窓の両端を結ぶ弦の外向きの法線方向に最も突き出た点。窓を先端に合わせて数回やり直す。
                int tip = i;
                Vector2 normal = Vector2.zero;
                for (int iteration = 0; iteration < 4; iteration++)
                {
                    var chord = (p[Wrap(tip + K, n)] - p[Wrap(tip - K, n)]).normalized;
                    normal = new Vector2(chord.y, -chord.x);
                    int best = tip;
                    float bestDot = float.MinValue;
                    for (int k = -K; k <= K; k++)
                    {
                        int j = Wrap(tip + k, n);
                        float dot = Vector2.Dot(p[j], normal);
                        if (dot > bestDot) { bestDot = dot; best = j; }
                    }
                    tip = best;
                }
                if (normal == Vector2.zero) continue;

                // タイルしない辺に掛かる先端（画像の端で切れた糸）は除く。
                float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
                for (int k = -K; k <= K; k++)
                {
                    var q = p[Wrap(tip + k, n)];
                    minX = Mathf.Min(minX, q.x - 1f); maxX = Mathf.Max(maxX, q.x - 1f);
                    minY = Mathf.Min(minY, q.y - 1f); maxY = Mathf.Max(maxY, q.y - 1f);
                }
                if (minY < 0.5f || maxY > h - 1.5f) continue;
                if (!tile && (minX < 0.5f || maxX > w - 1.5f)) continue;

                var position = p[tip];
                float x = position.x - 1f - (tile ? w : 0);
                if (tile && (x < 0f || x >= w)) continue;

                // 先端での糸の半径 = 先端から内側へ進んだときの背景までの距離の最大
                float radius = 0f;
                for (float k = 0f; k <= 1.5f * threadRadius; k += 0.5f)
                    radius = Mathf.Max(radius, Sample(dist, gw, gh, position - normal * k));
                if (radius < 0.5f) continue;

                // 格子点 = 画素の中心なので、テクスチャの連続座標へは +0.5。
                tips.Add(new Tip
                {
                    position = new Vector2(x + 0.5f, position.y - 1f + 0.5f),
                    normal = normal,
                    radius = radius,
                });
            }
        }

        static int Wrap(int i, int n) => ((i % n) + n) % n;

        // 閉じた折れ線を弧長で等間隔に取り直す。
        static Vector2[] Resample(Vector2[] loop, float step, out float ds)
        {
            int m = loop.Length;
            var cum = new float[m + 1];
            for (int i = 0; i < m; i++) cum[i + 1] = cum[i] + Vector2.Distance(loop[i], loop[(i + 1) % m]);
            float total = cum[m];
            int n = Mathf.Max(8, (int)(total / step));
            ds = total / n;
            var p = new Vector2[n];
            int seg = 0;
            for (int k = 0; k < n; k++)
            {
                float t = k * ds;
                while (seg < m - 1 && cum[seg + 1] <= t) seg++;
                float len = cum[seg + 1] - cum[seg];
                float f = len > 1e-9f ? (t - cum[seg]) / len : 0f;
                p[k] = Vector2.Lerp(loop[seg], loop[(seg + 1) % m], f);
            }
            return p;
        }

        // 画素の段差を消すため、位置を循環のガウスで平滑化する（sigma はサンプル数）。
        static void Smooth(Vector2[] p, float sigma)
        {
            int n = p.Length;
            int radius = Mathf.CeilToInt(3f * sigma);
            var kernel = new float[2 * radius + 1];
            float sum = 0f;
            for (int k = -radius; k <= radius; k++)
            {
                kernel[k + radius] = Mathf.Exp(-0.5f * k * k / (sigma * sigma));
                sum += kernel[k + radius];
            }
            var source = (Vector2[])p.Clone();
            for (int i = 0; i < n; i++)
            {
                var acc = Vector2.zero;
                for (int k = -radius; k <= radius; k++) acc += source[Wrap(i + k, n)] * kernel[k + radius];
                p[i] = acc / sum;
            }
        }

        static float Sample(float[] field, int gw, int gh, Vector2 point)
        {
            float x = Mathf.Clamp(point.x, 0f, gw - 1.001f);
            float y = Mathf.Clamp(point.y, 0f, gh - 1.001f);
            int x0 = (int)x, y0 = (int)y;
            float fx = x - x0, fy = y - y0;
            int i = y0 * gw + x0;
            return Mathf.Lerp(Mathf.Lerp(field[i], field[i + 1], fx), Mathf.Lerp(field[i + gw], field[i + gw + 1], fx), fy);
        }

        // ---- 輪郭（マーチングスクエア）

        // 格子点 = 画素の中心。辺の ID は 横の辺 (x,y)-(x+1,y) が 2·(y·gw + x)、縦の辺 (x,y)-(x,y+1) が 2·(y·gw + x) + 1。
        static List<Vector2[]> Contours(float[] img, int gw, int gh)
        {
            var nodes = new Dictionary<int, Node>();
            var hit = new int[4];
            for (int y = 0; y < gh - 1; y++)
            {
                for (int x = 0; x < gw - 1; x++)
                {
                    float c0 = img[y * gw + x] - Iso, c1 = img[y * gw + x + 1] - Iso;
                    float c2 = img[(y + 1) * gw + x + 1] - Iso, c3 = img[(y + 1) * gw + x] - Iso;
                    bool i0 = c0 > 0f, i1 = c1 > 0f, i2 = c2 > 0f, i3 = c3 > 0f;
                    if (i0 == i1 && i1 == i2 && i2 == i3) continue;

                    int e0 = 2 * (y * gw + x);             // c0-c1
                    int e1 = 2 * (y * gw + x + 1) + 1;     // c1-c2
                    int e2 = 2 * ((y + 1) * gw + x);       // c3-c2
                    int e3 = 2 * (y * gw + x) + 1;         // c0-c3
                    int count = 0;
                    if (i0 != i1) { AddCrossing(nodes, e0, new Vector2(x, y), new Vector2(x + 1, y), c0, c1); hit[count++] = e0; }
                    if (i1 != i2) { AddCrossing(nodes, e1, new Vector2(x + 1, y), new Vector2(x + 1, y + 1), c1, c2); hit[count++] = e1; }
                    if (i3 != i2) { AddCrossing(nodes, e2, new Vector2(x, y + 1), new Vector2(x + 1, y + 1), c3, c2); hit[count++] = e2; }
                    if (i0 != i3) { AddCrossing(nodes, e3, new Vector2(x, y), new Vector2(x, y + 1), c0, c3); hit[count++] = e3; }

                    if (count == 2)
                    {
                        Link(nodes, hit[0], hit[1]);
                    }
                    else
                    {
                        // 鞍点: 中心の値が c0 と同じ側なら c0-c2 が繋がり、c1・c3 の角を切り離す。
                        float center = 0.25f * (c0 + c1 + c2 + c3);
                        if ((center > 0f) == i0) { Link(nodes, e0, e1); Link(nodes, e2, e3); }
                        else { Link(nodes, e0, e3); Link(nodes, e1, e2); }
                    }
                }
            }

            var loops = new List<Vector2[]>();
            var visited = new HashSet<int>();
            var buffer = new List<Vector2>();
            foreach (int start in nodes.Keys)
            {
                if (visited.Contains(start)) continue;
                buffer.Clear();
                int previous = -1, current = start;
                while (true)
                {
                    visited.Add(current);
                    var node = nodes[current];
                    buffer.Add(node.point);
                    int next = node.a != previous ? node.a : node.b;
                    if (next < 0 || next == start || visited.Contains(next)) break;
                    previous = current;
                    current = next;
                }
                if (buffer.Count >= 8) loops.Add(buffer.ToArray());
            }
            return loops;
        }

        static void AddCrossing(Dictionary<int, Node> nodes, int edge, Vector2 pa, Vector2 pb, float a, float b)
        {
            if (nodes.ContainsKey(edge)) return;
            nodes[edge] = new Node { point = Vector2.Lerp(pa, pb, a / (a - b)), a = -1, b = -1 };
        }

        static void Link(Dictionary<int, Node> nodes, int e0, int e1)
        {
            Connect(nodes, e0, e1);
            Connect(nodes, e1, e0);
        }

        static void Connect(Dictionary<int, Node> nodes, int from, int to)
        {
            var node = nodes[from];
            if (node.a < 0) node.a = to;
            else node.b = to;
            nodes[from] = node;
        }
    }
}
