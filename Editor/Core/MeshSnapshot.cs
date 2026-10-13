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

        // 均一グリッド（CSR 形式）。セルごとに AABB が重なる三角形インデックスを持つ。
        Vector3 gridMin, gridMax;
        float cellSize;
        int nx, ny, nz;
        int[] cellStart;
        int[] cellItems;
        int[] stamps;
        int queryId;

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
