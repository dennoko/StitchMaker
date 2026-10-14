using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dennokoworks.StitchMaker
{
    /// <summary>
    /// 対象メッシュのメッシュ空間（Transform の位置・回転を除き、スケール適用済み）での複製。生成後は不変。
    /// </summary>
    public sealed class MeshSnapshot : IDisposable
    {
        const int GridResolution = 64;

        public Mesh Mesh { get; }
        public Mesh SourceMesh { get; }
        public readonly Vector3[] Vertices;
        public readonly Vector3[] Normals;
        public readonly Vector4[] Tangents;
        public readonly Vector2[] Uvs;
        public readonly int[] Triangles;
        // 三角形ごとの UV アイランド番号（0 から IslandCount - 1）。
        public readonly int[] TriangleIslands;
        public int IslandCount { get; private set; }

        // 均一グリッド（CSR 形式）。セルごとに AABB が重なる三角形インデックスを持つ。
        Vector3 gridMin, gridMax;
        float cellSize;
        int nx, ny, nz;
        int[] cellStart;
        int[] cellItems;
        int[] stamps;
        int queryId;
        readonly List<int> snapCandidates = new List<int>();

        public MeshSnapshot(Renderer renderer, int materialIndex)
        {
            SourceMesh = GetSourceMesh(renderer);
            if (SourceMesh == null)
                throw new InvalidOperationException("対象の Renderer にメッシュがありません（SkinnedMeshRenderer か、MeshFilter 付きの MeshRenderer を指定してください）。");

            Mesh baked = null;
            try
            {
                var read = SourceMesh;
                var scale = Vector3.one;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    baked = new Mesh();
                    skinned.BakeMesh(baked, true);
                    read = baked;
                }
                else
                {
                    scale = renderer.transform.lossyScale;
                }

                Vertices = read.vertices;
                Normals = read.normals;
                Tangents = read.tangents;
                Uvs = read.uv;
                if (Uvs.Length != Vertices.Length) Uvs = SourceMesh.uv;

                if (Vertices.Length == 0)
                    throw new InvalidOperationException("メッシュの頂点を読み取れません（Read/Write を有効にしてください）。");
                if (Uvs.Length != Vertices.Length)
                    throw new InvalidOperationException("メッシュに UV0 がありません。");

                int subMesh = Mathf.Clamp(materialIndex, 0, read.subMeshCount - 1);
                if (read.GetTopology(subMesh) != MeshTopology.Triangles)
                    throw new InvalidOperationException("三角形以外のトポロジーには対応していません。");
                Triangles = read.GetTriangles(subMesh);

                ApplyScale(scale);
            }
            finally
            {
                if (baked != null) UnityEngine.Object.DestroyImmediate(baked);
            }

            Mesh = new Mesh
            {
                name = "StitchMaker Snapshot",
                hideFlags = HideFlags.HideAndDontSave,
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
            };
            Mesh.SetVertices(Vertices);
            Mesh.SetUVs(0, Uvs);
            Mesh.SetTriangles(Triangles, 0);
            if (Normals.Length == Vertices.Length)
            {
                Mesh.SetNormals(Normals);
            }
            else
            {
                Mesh.RecalculateNormals();
                Normals = Mesh.normals;
            }
            if (Tangents.Length == Vertices.Length)
            {
                Mesh.SetTangents(Tangents);
            }
            else
            {
                Mesh.RecalculateTangents();
                Tangents = Mesh.tangents;
            }
            Mesh.RecalculateBounds();

            BuildGrid();
            TriangleIslands = BuildIslands();
        }

        public static Mesh GetSourceMesh(Renderer renderer)
        {
            if (renderer == null) return null;
            if (renderer is SkinnedMeshRenderer skinned) return skinned.sharedMesh;
            var filter = renderer.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        public void Dispose()
        {
            if (Mesh != null) UnityEngine.Object.DestroyImmediate(Mesh);
        }

        /// <summary>メッシュ空間のレイと表面（表向きの三角形のみ）の最近傍交点を求める。</summary>
        public bool Raycast(Ray meshSpaceRay, out Vector3 position, out Vector3 normal)
        {
            const float Epsilon = 1e-12f;
            var origin = meshSpaceRay.origin;
            var direction = meshSpaceRay.direction;
            var vertices = Vertices;
            var triangles = Triangles;

            float bestT = float.MaxValue;
            float bestU = 0f, bestV = 0f;
            int best = -1;

            for (int i = 0; i < triangles.Length; i += 3)
            {
                var v0 = vertices[triangles[i]];
                var e1 = vertices[triangles[i + 1]] - v0;
                var e2 = vertices[triangles[i + 2]] - v0;
                var p = Vector3.Cross(direction, e2);
                float det = Vector3.Dot(e1, p);
                if (det < Epsilon) continue; // 裏面・平行

                var s = origin - v0;
                float u = Vector3.Dot(s, p);
                if (u < 0f || u > det) continue;
                var q = Vector3.Cross(s, e1);
                float v = Vector3.Dot(direction, q);
                if (v < 0f || u + v > det) continue;
                float t = Vector3.Dot(e2, q) / det;
                if (t <= 0f || t >= bestT) continue;

                bestT = t;
                bestU = u / det;
                bestV = v / det;
                best = i;
            }

            if (best < 0)
            {
                position = default;
                normal = default;
                return false;
            }

            position = origin + direction * bestT;
            normal = (Normals[triangles[best]] * (1f - bestU - bestV)
                      + Normals[triangles[best + 1]] * bestU
                      + Normals[triangles[best + 2]] * bestV).normalized;
            if (normal == Vector3.zero)
            {
                var a = vertices[triangles[best]];
                normal = Vector3.Cross(vertices[triangles[best + 1]] - a, vertices[triangles[best + 2]] - a).normalized;
            }
            return true;
        }

        /// <summary>AABB が bounds と重なりうる三角形のインデックス（Triangles[3 * index] が先頭）を列挙する。</summary>
        public void QueryTriangles(Bounds bounds, List<int> results)
        {
            results.Clear();
            if (cellItems == null) return;

            Vector3 min = bounds.min, max = bounds.max;
            if (max.x < gridMin.x || max.y < gridMin.y || max.z < gridMin.z ||
                min.x > gridMax.x || min.y > gridMax.y || min.z > gridMax.z)
                return;

            queryId++;
            CellRange(min, max, out int x0, out int y0, out int z0, out int x1, out int y1, out int z1);
            for (int z = z0; z <= z1; z++)
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int cell = (z * ny + y) * nx + x;
                for (int k = cellStart[cell]; k < cellStart[cell + 1]; k++)
                {
                    int triangle = cellItems[k];
                    if (stamps[triangle] == queryId) continue;
                    stamps[triangle] = queryId;
                    results.Add(triangle);
                }
            }
        }

        /// <summary>
        /// origin を、normalHint と同じ側を向いた近傍の表面へ吸着させる。
        /// まず法線方向のレイキャスト、外れたら三角形上の最近傍点を使う。
        /// radius 内に該当する三角形が無ければ false を返し、origin / normalHint をそのまま返す。
        /// </summary>
        public bool TrySnapToSurface(Vector3 origin, Vector3 normalHint, float radius,
            out Vector3 snappedPosition, out Vector3 snappedNormal)
        {
            const float FacingThreshold = 0.2f;
            snappedPosition = origin;
            snappedNormal = normalHint;
            if (radius <= 0f || normalHint == Vector3.zero) return false;

            QueryTriangles(new Bounds(origin, Vector3.one * (radius * 2f)), snapCandidates);

            var vertices = Vertices;
            var triangles = Triangles;
            var rayOrigin = origin + normalHint * radius;
            var direction = -normalHint;

            // レイの交点は origin に最も近いもの（t が radius に最も近いもの）を採る。
            float bestRay = float.MaxValue;
            float bestNearest = radius * radius;
            bool hitRay = false, hitNearest = false;
            var rayNormal = normalHint;
            var nearestPosition = origin;
            var nearestNormal = normalHint;

            foreach (int triangle in snapCandidates)
            {
                int i0 = triangles[triangle * 3], i1 = triangles[triangle * 3 + 1], i2 = triangles[triangle * 3 + 2];
                var v0 = vertices[i0];
                var e1 = vertices[i1] - v0;
                var e2 = vertices[i2] - v0;
                var faceNormal = Vector3.Cross(e1, e2).normalized;
                // 裏面や、厚みの反対側の壁には吸着させない。
                if (Vector3.Dot(faceNormal, normalHint) <= FacingThreshold) continue;

                var p = Vector3.Cross(direction, e2);
                float det = Vector3.Dot(e1, p);
                if (det > 1e-12f)
                {
                    var s = rayOrigin - v0;
                    float u = Vector3.Dot(s, p) / det;
                    var q = Vector3.Cross(s, e1);
                    float v = Vector3.Dot(direction, q) / det;
                    float t = Vector3.Dot(e2, q) / det;
                    if (u >= 0f && v >= 0f && u + v <= 1f && t >= 0f && t <= radius * 2f
                        && Mathf.Abs(t - radius) < bestRay)
                    {
                        bestRay = Mathf.Abs(t - radius);
                        hitRay = true;
                        snappedPosition = rayOrigin + direction * t;
                        rayNormal = (Normals[i0] * (1f - u - v) + Normals[i1] * u + Normals[i2] * v).normalized;
                        if (rayNormal == Vector3.zero) rayNormal = faceNormal;
                    }
                }
                if (hitRay) continue;

                var closest = ClosestPointOnTriangle(origin, v0, vertices[i1], vertices[i2]);
                float sqrDistance = (closest - origin).sqrMagnitude;
                if (sqrDistance <= bestNearest)
                {
                    bestNearest = sqrDistance;
                    hitNearest = true;
                    nearestPosition = closest;
                    nearestNormal = faceNormal;
                }
            }

            if (hitRay)
            {
                snappedNormal = rayNormal;
                return true;
            }
            if (hitNearest)
            {
                snappedPosition = nearestPosition;
                snappedNormal = nearestNormal;
                return true;
            }
            return false;
        }

        // Ericson "Real-Time Collision Detection" 5.1.5
        static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            var ab = b - a;
            var ac = c - a;
            var ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;

            var bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));

            var cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
                return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));

            float denominator = va + vb + vc;
            if (Mathf.Abs(denominator) < 1e-20f) return a; // 縮退三角形
            return a + ab * (vb / denominator) + ac * (vc / denominator);
        }

        void ApplyScale(Vector3 scale)
        {
            if (scale == Vector3.one) return;

            // 法線は逆スケールで補正する（非一様スケール対応）。
            var inverse = new Vector3(SafeInverse(scale.x), SafeInverse(scale.y), SafeInverse(scale.z));
            for (int i = 0; i < Vertices.Length; i++)
                Vertices[i] = Vector3.Scale(Vertices[i], scale);
            for (int i = 0; i < Normals.Length; i++)
                Normals[i] = Vector3.Scale(Normals[i], inverse).normalized;
            for (int i = 0; i < Tangents.Length; i++)
            {
                var t = Tangents[i];
                var xyz = Vector3.Scale(new Vector3(t.x, t.y, t.z), scale).normalized;
                Tangents[i] = new Vector4(xyz.x, xyz.y, xyz.z, t.w);
            }
        }

        static float SafeInverse(float value)
        {
            return Mathf.Abs(value) < 1e-8f ? 1f : 1f / value;
        }

        void BuildGrid()
        {
            int triangleCount = Triangles.Length / 3;
            if (triangleCount == 0) return;

            gridMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            gridMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            for (int i = 0; i < Triangles.Length; i++)
            {
                var v = Vertices[Triangles[i]];
                gridMin = Vector3.Min(gridMin, v);
                gridMax = Vector3.Max(gridMax, v);
            }

            var size = gridMax - gridMin;
            float longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            cellSize = Mathf.Max(longest / GridResolution, 1e-6f);
            nx = Mathf.Clamp(Mathf.CeilToInt(size.x / cellSize), 1, GridResolution);
            ny = Mathf.Clamp(Mathf.CeilToInt(size.y / cellSize), 1, GridResolution);
            nz = Mathf.Clamp(Mathf.CeilToInt(size.z / cellSize), 1, GridResolution);

            int cellCount = nx * ny * nz;
            cellStart = new int[cellCount + 1];

            // 1 回目: セルごとの件数を数える。
            for (int t = 0; t < triangleCount; t++)
            {
                TriangleCellRange(t, out int x0, out int y0, out int z0, out int x1, out int y1, out int z1);
                for (int z = z0; z <= z1; z++)
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    cellStart[(z * ny + y) * nx + x + 1]++;
            }
            for (int i = 0; i < cellCount; i++)
                cellStart[i + 1] += cellStart[i];

            // 2 回目: 三角形インデックスを詰める。
            cellItems = new int[cellStart[cellCount]];
            var cursor = new int[cellCount];
            Array.Copy(cellStart, cursor, cellCount);
            for (int t = 0; t < triangleCount; t++)
            {
                TriangleCellRange(t, out int x0, out int y0, out int z0, out int x1, out int y1, out int z1);
                for (int z = z0; z <= z1; z++)
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    cellItems[cursor[(z * ny + y) * nx + x]++] = t;
            }

            stamps = new int[triangleCount];
        }

        /// <summary>
        /// 位置と UV が両方一致する頂点を同一とみなし、頂点を共有する三角形を同じアイランドにまとめる。
        /// ハードエッジで複製された頂点は結合され、UV シーム（位置が同じで UV が違う）は境界になる。
        /// </summary>
        int[] BuildIslands()
        {
            int triangleCount = Triangles.Length / 3;
            var islands = new int[triangleCount];
            if (triangleCount == 0) return islands;

            var parent = new int[Vertices.Length];
            var welded = new Dictionary<(Vector3, Vector2), int>(Vertices.Length);
            for (int i = 0; i < Vertices.Length; i++)
            {
                var key = (Vertices[i], Uvs[i]);
                if (welded.TryGetValue(key, out int first))
                {
                    parent[i] = first;
                }
                else
                {
                    welded.Add(key, i);
                    parent[i] = i;
                }
            }

            for (int t = 0; t < triangleCount; t++)
            {
                int a = Find(parent, Triangles[t * 3]);
                int b = Find(parent, Triangles[t * 3 + 1]);
                int c = Find(parent, Triangles[t * 3 + 2]);
                if (b != a) parent[b] = a;
                if (c != a && c != b) parent[c] = a;
            }

            // 根ごとに、三角形に現れた順で連番を振る。
            var ids = new Dictionary<int, int>();
            for (int t = 0; t < triangleCount; t++)
            {
                int root = Find(parent, Triangles[t * 3]);
                if (!ids.TryGetValue(root, out int id))
                {
                    id = ids.Count;
                    ids.Add(root, id);
                }
                islands[t] = id;
            }
            IslandCount = ids.Count;
            return islands;
        }

        static int Find(int[] parent, int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]]; // 経路半減
                i = parent[i];
            }
            return i;
        }

        void TriangleCellRange(int triangle, out int x0, out int y0, out int z0, out int x1, out int y1, out int z1)
        {
            var a = Vertices[Triangles[triangle * 3]];
            var b = Vertices[Triangles[triangle * 3 + 1]];
            var c = Vertices[Triangles[triangle * 3 + 2]];
            CellRange(Vector3.Min(a, Vector3.Min(b, c)), Vector3.Max(a, Vector3.Max(b, c)),
                out x0, out y0, out z0, out x1, out y1, out z1);
        }

        void CellRange(Vector3 min, Vector3 max, out int x0, out int y0, out int z0, out int x1, out int y1, out int z1)
        {
            x0 = Mathf.Clamp((int)((min.x - gridMin.x) / cellSize), 0, nx - 1);
            y0 = Mathf.Clamp((int)((min.y - gridMin.y) / cellSize), 0, ny - 1);
            z0 = Mathf.Clamp((int)((min.z - gridMin.z) / cellSize), 0, nz - 1);
            x1 = Mathf.Clamp((int)((max.x - gridMin.x) / cellSize), 0, nx - 1);
            y1 = Mathf.Clamp((int)((max.y - gridMin.y) / cellSize), 0, ny - 1);
            z1 = Mathf.Clamp((int)((max.z - gridMin.z) / cellSize), 0, nz - 1);
        }
    }
}
