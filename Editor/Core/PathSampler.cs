using System.Collections.Generic;
using UnityEngine;

namespace Dennokoworks.StitchMaker
{
    /// <summary>ステッチ 1 個分の配置。メッシュ空間。tangent は角度オフセット適用済み。</summary>
    public struct StitchSample
    {
        public Vector3 position;
        public Vector3 tangent;
        public Vector3 normal;
    }

    /// <summary>曲線評価と等間隔サンプリング。曲線は 3 次ベジェの 1 経路のみで評価する。</summary>
    public static class PathSampler
    {
        public const int Subdivisions = 16;
        const int MaxSamples = 20000;

        static readonly List<Vector3> points = new List<Vector3>();
        static readonly List<Vector3> normals = new List<Vector3>();
        static readonly List<float> distances = new List<float>();
        // ミラー側のノード列を組み立てる作業用パス（毎回の割り当てを避ける）。
        static readonly StitchPath mirrorScratch = new StitchPath();

        /// <summary>閉じたパスとして扱うか（ノード 3 個未満は開いたパスとみなす）。</summary>
        public static bool IsClosed(StitchPath path)
        {
            return path.closed && path.nodes.Count >= 3;
        }

        public static int SegmentCount(StitchPath path)
        {
            int count = path.nodes.Count;
            if (count < 2) return 0;
            return IsClosed(path) ? count : count - 1;
        }

        /// <summary>隣接ノードから自動ハンドルを求める。使われない側（開いたパスの端）は 0 になる。</summary>
        public static void GetAutoHandles(StitchPath path, int index, out Vector3 handleIn, out Vector3 handleOut)
        {
            var nodes = path.nodes;
            int count = nodes.Count;
            bool closed = IsClosed(path);
            bool hasPrev = closed || index > 0;
            bool hasNext = closed || index < count - 1;

            var current = nodes[index].position;
            var prev = hasPrev ? nodes[(index - 1 + count) % count].position : current;
            var next = hasNext ? nodes[(index + 1) % count].position : current;

            var direction = (next - prev).normalized;
            handleIn = -direction * ((current - prev).magnitude / 3f);
            handleOut = direction * ((next - current).magnitude / 3f);
        }

        /// <summary>ノード index → index+1 の 3 次ベジェ制御点（自動 / 手動ハンドルを吸収）。</summary>
        public static void GetSegment(StitchPath path, int index,
            out Vector3 p0, out Vector3 p1, out Vector3 p2, out Vector3 p3)
        {
            var nodes = path.nodes;
            int nextIndex = (index + 1) % nodes.Count;
            var a = nodes[index];
            var b = nodes[nextIndex];
            p0 = a.position;
            p3 = b.position;
            if (path.manualHandles)
            {
                p1 = p0 + a.handleOut;
                p2 = p3 + b.handleIn;
            }
            else
            {
                GetAutoHandles(path, index, out _, out var handleOut);
                GetAutoHandles(path, nextIndex, out var handleIn, out _);
                p1 = p0 + handleOut;
                p2 = p3 + handleIn;
            }
        }

        /// <summary>
        /// 表示・当たり判定用の折れ線。1 セグメントを Subdivisions 分割するため、
        /// 折れ線の j 番目の辺はセグメント j / Subdivisions に属する。
        /// </summary>
        public static void BuildPolyline(StitchPath path, List<Vector3> results)
        {
            BuildPolyline(path, results, null);
        }

        /// <summary>manualHandles を ON にした時に、現在の自動ハンドルをノードへ書き込む。</summary>
        public static void BakeAutoHandles(StitchPath path)
        {
            int count = path.nodes.Count;
            var handlesIn = new Vector3[count];
            var handlesOut = new Vector3[count];
            for (int i = 0; i < count; i++)
                GetAutoHandles(path, i, out handlesIn[i], out handlesOut[i]);
            for (int i = 0; i < count; i++)
            {
                path.nodes[i].handleIn = handlesIn[i];
                path.nodes[i].handleOut = handlesOut[i];
            }
        }

        /// <summary>対称軸の成分だけ符号を反転するスケール。</summary>
        public static Vector3 MirrorScale(MirrorAxis axis)
        {
            switch (axis)
            {
                case MirrorAxis.X: return new Vector3(-1f, 1f, 1f);
                case MirrorAxis.Y: return new Vector3(1f, -1f, 1f);
                case MirrorAxis.Z: return new Vector3(1f, 1f, -1f);
                default: return Vector3.one;
            }
        }

        /// <summary>
        /// ミラー側のパスを独立したパスとして作る（制御点を鏡映し、反対側の表面へ吸着済み）。
        /// 設定は複製し、角度オフセットは鏡映に合わせて符号を反転する。
        /// </summary>
        public static StitchPath CreateMirroredPath(StitchPath source, MeshSnapshot snapshot)
        {
            var mirrored = new StitchPath
            {
                name = source.name,
                visible = source.visible,
                mask = source.mask,
                settings = source.settings.Clone(),
                mirrorSnapRadius = source.mirrorSnapRadius,
            };
            mirrored.settings.angleOffset = -source.settings.angleOffset;
            MirrorNodes(source, snapshot, mirrored);
            return mirrored;
        }

        /// <summary>ミラー側の折れ線（シーンビューのゴースト表示用）。ミラー OFF なら空になる。</summary>
        public static void BuildMirroredPolyline(StitchPath path, MeshSnapshot snapshot, List<Vector3> results)
        {
            results.Clear();
            if (path.mirrorAxis == MirrorAxis.None) return;
            MirrorNodes(path, snapshot, mirrorScratch);
            BuildPolyline(mirrorScratch, results, null);
        }

        /// <summary>
        /// ステッチ配置（等間隔・ジッター・角度オフセット適用済み）を求める。
        /// snapshot を渡すと、ミラーが有効なパスではミラー側の配置も続けて追加する。
        /// </summary>
        public static void Sample(StitchPath path, List<StitchSample> results, int seedOffset = 0,
            MeshSnapshot snapshot = null)
        {
            results.Clear();
            AppendSamples(path, path.settings, results, seedOffset, MirrorAxis.None);
            if (path.mirrorAxis == MirrorAxis.None || snapshot == null) return;

            // 単純な座標反転ではなく、吸着後のノード列を改めて等間隔に刻む（反対側の曲面でも間隔が保たれる）。
            MirrorNodes(path, snapshot, mirrorScratch);
            AppendSamples(mirrorScratch, path.settings, results, seedOffset, path.mirrorAxis);
        }

        // source の制御点を鏡映し、反対側の表面へ吸着させて target に書き込む。
        static void MirrorNodes(StitchPath source, MeshSnapshot snapshot, StitchPath target)
        {
            target.closed = source.closed;
            target.manualHandles = source.manualHandles;
            target.mirrorAxis = MirrorAxis.None;

            var scale = MirrorScale(source.mirrorAxis);
            var nodes = target.nodes;
            int count = source.nodes.Count;
            if (nodes.Count > count) nodes.RemoveRange(count, nodes.Count - count);
            while (nodes.Count < count) nodes.Add(new StitchNode());

            for (int i = 0; i < count; i++)
            {
                var from = source.nodes[i];
                var to = nodes[i];
                to.position = Vector3.Scale(from.position, scale);
                to.normal = Vector3.Scale(from.normal, scale).normalized;
                to.handleIn = Vector3.Scale(from.handleIn, scale);
                to.handleOut = Vector3.Scale(from.handleOut, scale);

                if (snapshot == null || !snapshot.TrySnapToSurface(to.position, to.normal,
                        source.mirrorSnapRadius, out var position, out var normal))
                    continue;

                // ハンドルは表面の傾きの変化に追従させる。
                var rotation = Quaternion.FromToRotation(to.normal, normal);
                to.handleIn = rotation * to.handleIn;
                to.handleOut = rotation * to.handleOut;
                to.position = position;
                to.normal = normal;
            }
        }

        // mirrorAxis が None 以外なら path はミラー側のノード列で、元パスと鏡像になるよう符号を反転して刻む。
        static void AppendSamples(StitchPath path, StitchSettings settings, List<StitchSample> results,
            int seedOffset, MirrorAxis mirrorAxis)
        {
            if (settings.spacing < 1e-5f) return;
            bool mirrored = mirrorAxis != MirrorAxis.None;
            // 鏡映すると bitangent (= normal × tangent) と回転の向きが逆になる。
            float sign = mirrored ? -1f : 1f;
            var axis = Vector3.one - MirrorScale(mirrorAxis); // 対称軸の成分だけ 2、他は 0
            float overlapLimit = settings.width * 0.25f;

            BuildPolyline(path, points, normals);
            if (points.Count < 2) return;

            distances.Clear();
            distances.Add(0f);
            for (int i = 1; i < points.Count; i++)
                distances.Add(distances[i - 1] + (points[i] - points[i - 1]).magnitude);
            float total = distances[distances.Count - 1];
            if (total < 1e-6f) return;

            int count;
            float step, first;
            if (IsClosed(path))
            {
                // 継ぎ目が出ないよう、周長を割り切れる間隔に微調整する。
                count = Mathf.Max(1, Mathf.RoundToInt(total / settings.spacing));
                step = total / count;
                first = step * 0.5f;
            }
            else
            {
                count = Mathf.Max(1, Mathf.FloorToInt(total / settings.spacing));
                step = settings.spacing;
                first = (total - (count - 1) * step) * 0.5f;
            }
            count = Mathf.Min(count, MaxSamples);

            var random = new System.Random(unchecked(settings.seed * 7919 + seedOffset));
            int edge = 0;
            for (int i = 0; i < count; i++)
            {
                float distance = first + i * step;
                while (edge < points.Count - 2 && distances[edge + 1] < distance) edge++;

                float edgeLength = distances[edge + 1] - distances[edge];
                float f = edgeLength > 1e-9f ? Mathf.Clamp01((distance - distances[edge]) / edgeLength) : 0f;

                // 乱数は設定に関わらず毎回 3 個引く（ジッター量を変えても他のステッチの乱数列がずれない）。
                float randomT = (float)(random.NextDouble() * 2.0 - 1.0);
                float randomB = (float)(random.NextDouble() * 2.0 - 1.0);
                float randomAngle = (float)(random.NextDouble() * 2.0 - 1.0);

                var tangent = (points[edge + 1] - points[edge]).normalized;
                var normal = Vector3.Lerp(normals[edge], normals[edge + 1], f);
                normal = (normal - tangent * Vector3.Dot(normal, tangent)).normalized;
                if (tangent == Vector3.zero || normal == Vector3.zero) continue;

                var onCurve = Vector3.Lerp(points[edge], points[edge + 1], f);
                // 対称面上では元パスのステッチと重なるので、ミラー側を省いて二重焼きを防ぐ。
                if (mirrored && Mathf.Abs(Vector3.Dot(onCurve, axis)) * 0.5f < overlapLimit) continue;

                var bitangent = Vector3.Cross(normal, tangent);
                var position = onCurve
                               + (tangent * randomT + bitangent * (randomB * sign)) * settings.jitterPosition;
                float angle = (settings.angleOffset + randomAngle * settings.jitterAngle) * sign;

                results.Add(new StitchSample
                {
                    position = position,
                    tangent = Quaternion.AngleAxis(angle, normal) * tangent,
                    normal = normal,
                });
            }
        }

        static void BuildPolyline(StitchPath path, List<Vector3> pointResults, List<Vector3> normalResults)
        {
            pointResults.Clear();
            normalResults?.Clear();

            int segments = SegmentCount(path);
            var nodes = path.nodes;
            for (int s = 0; s < segments; s++)
            {
                GetSegment(path, s, out var p0, out var p1, out var p2, out var p3);
                var n0 = nodes[s].normal;
                var n1 = nodes[(s + 1) % nodes.Count].normal;
                bool last = s == segments - 1;
                int steps = last ? Subdivisions + 1 : Subdivisions;
                for (int k = 0; k < steps; k++)
                {
                    float t = (float)k / Subdivisions;
                    pointResults.Add(Evaluate(p0, p1, p2, p3, t));
                    normalResults?.Add(Vector3.Lerp(n0, n1, t).normalized);
                }
            }
        }

        static Vector3 Evaluate(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float u = 1f - t;
            return p0 * (u * u * u) + p1 * (3f * u * u * t) + p2 * (3f * u * t * t) + p3 * (t * t * t);
        }
    }
}
