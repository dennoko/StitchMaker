using System.Collections.Generic;
using UnityEngine;

namespace Dennokoworks.StitchMaker
{
    /// <summary>
    /// ステッチごとに近傍三角形を集め、UV を描画位置・ステッチ基準座標を頂点属性に持つデカールメッシュを作る。
    /// 各三角形は自分の UV 位置に描かれるため、シームをまたぐステッチも両側のアイランドへ分割される。
    /// </summary>
    public static class DecalMeshBuilder
    {
        static readonly List<int> candidates = new List<int>();
        static readonly List<Vector3> positions = new List<Vector3>();
        static readonly List<Vector3> stitchCoords = new List<Vector3>();
        static readonly List<Vector3> tangentAxes = new List<Vector3>();
        static readonly List<Vector3> bitangentAxes = new List<Vector3>();
        static readonly List<int> indices = new List<int>();
        static readonly Vector3[] local = new Vector3[3];

        /// <summary>1 パス分のデカールメッシュを mesh に書き込む（mesh は呼び出し側が再利用する）。</summary>
        public static void Build(MeshSnapshot snapshot, StitchSettings settings, List<StitchSample> samples, Mesh mesh)
        {
            Build(snapshot, settings.ScaledLength, settings.ScaledWidth, samples, mesh);
        }

        /// <summary>各サンプルを中心に length × width の四角形を貼るデカールメッシュを作る（溝にも使う）。</summary>
        public static void Build(MeshSnapshot snapshot, float length, float width, List<StitchSample> samples, Mesh mesh)
        {
            positions.Clear();
            stitchCoords.Clear();
            tangentAxes.Clear();
            bitangentAxes.Clear();
            indices.Clear();

            length = Mathf.Max(length, 1e-6f);
            width = Mathf.Max(width, 1e-6f);
            float depth = Mathf.Max(length, width); // 深度許容
            var querySize = Vector3.one * (depth * 2f);

            var vertices = snapshot.Vertices;
            var normals = snapshot.Normals;
            var tangents = snapshot.Tangents;
            var uvs = snapshot.Uvs;
            var triangles = snapshot.Triangles;

            for (int s = 0; s < samples.Count; s++)
            {
                var center = samples[s].position;
                var t = samples[s].tangent;
                var n = samples[s].normal;
                var b = Vector3.Cross(n, t);

                snapshot.QueryTriangles(new Bounds(center, querySize), candidates);
                for (int c = 0; c < candidates.Count; c++)
                {
                    int first = candidates[c] * 3;
                    var v0 = vertices[triangles[first]];
                    var v1 = vertices[triangles[first + 1]];
                    var v2 = vertices[triangles[first + 2]];

                    // 裏向き・反対側の面には描かない。
                    if (Vector3.Dot(Vector3.Cross(v1 - v0, v2 - v0), n) <= 0f) continue;

                    local[0] = ToStitchCoord(v0 - center, t, b, n, length, width, depth);
                    local[1] = ToStitchCoord(v1 - center, t, b, n, length, width, depth);
                    local[2] = ToStitchCoord(v2 - center, t, b, n, length, width, depth);
                    if (IsOutside(local[0].x, local[1].x, local[2].x, 0f, 1f)) continue;
                    if (IsOutside(local[0].y, local[1].y, local[2].y, 0f, 1f)) continue;
                    if (IsOutside(local[0].z, local[1].z, local[2].z, -1f, 1f)) continue;

                    for (int k = 0; k < 3; k++)
                    {
                        int vertex = triangles[first + k];
                        var uv = uvs[vertex];
                        var meshNormal = normals[vertex];
                        var tangent4 = tangents[vertex];
                        var meshTangent = new Vector3(tangent4.x, tangent4.y, tangent4.z);
                        var meshBitangent = Vector3.Cross(meshNormal, meshTangent) * tangent4.w;

                        indices.Add(positions.Count);
                        positions.Add(new Vector3(uv.x, uv.y, 0f));
                        stitchCoords.Add(local[k]);
                        tangentAxes.Add(new Vector3(
                            Vector3.Dot(t, meshTangent), Vector3.Dot(t, meshBitangent), Vector3.Dot(t, meshNormal)));
                        bitangentAxes.Add(new Vector3(
                            Vector3.Dot(b, meshTangent), Vector3.Dot(b, meshBitangent), Vector3.Dot(b, meshNormal)));
                    }
                }
            }

            mesh.Clear();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(positions);
            mesh.SetUVs(0, stitchCoords);
            mesh.SetUVs(1, tangentAxes);
            mesh.SetUVs(2, bitangentAxes);
            mesh.SetTriangles(indices, 0, false);
        }

        // (マスク U, マスク V, 正規化深度)。ステッチの内側は U, V が [0,1]、深度が [-1,1]。
        static Vector3 ToStitchCoord(Vector3 offset, Vector3 t, Vector3 b, Vector3 n,
            float length, float width, float depth)
        {
            return new Vector3(
                Vector3.Dot(offset, t) / length + 0.5f,
                Vector3.Dot(offset, b) / width + 0.5f,
                Vector3.Dot(offset, n) / depth);
        }

        static bool IsOutside(float a, float b, float c, float min, float max)
        {
            return (a < min && b < min && c < min) || (a > max && b > max && c > max);
        }
    }
}
