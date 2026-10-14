using System.Collections.Generic;
using UnityEngine;

namespace Dennokoworks.StitchMaker
{
    /// <summary>
    /// アイランド限定（StitchPath.islandConstrain）で描いてよい UV アイランドを、パスのノード位置から求める。
    /// 結果は島番号で引く bool 配列で、内部で再利用する（次の呼び出しまでに使い切ること）。
    /// </summary>
    public static class IslandFilter
    {
        // ノードは Raycast で表面上に置かれているので、ごく近くだけを探す。
        const float NodeSearchRadius = 0.001f;
        // 曲線上の点は表面から浮いたり沈んだりするので広めに探す。
        const float SegmentSearchRadius = 0.01f;

        static bool[] allowed = new bool[0];
        static bool[] preferred = new bool[0];
        static readonly List<int> nodeIslands = new List<int>();
        static readonly List<Vector3> polyline = new List<Vector3>();
        static readonly StitchPath mirrorScratch = new StitchPath();

        /// <summary>
        /// 描いてよいアイランド。ノードのアイランドに、シームをまたぐセグメントが通るアイランドとミラー側を加える。
        /// アイランド限定が OFF、またはどのアイランドも見つからなければ null（拘束しない）。
        /// </summary>
        public static bool[] Collect(StitchPath path, MeshSnapshot snapshot)
        {
            if (!path.islandConstrain || snapshot == null || path.nodes.Count == 0) return null;

            Reset(ref allowed, snapshot.IslandCount);
            bool found = Add(path, snapshot, allowed, true);
            if (path.mirrorAxis != MirrorAxis.None)
            {
                PathSampler.MirrorNodes(path, snapshot, mirrorScratch);
                found |= Add(mirrorScratch, snapshot, allowed, true);
            }
            return found ? allowed : null;
        }

        /// <summary>
        /// シーンビューのクリックで優先するアイランド（ノードが乗っているものだけ）。
        /// アイランド限定が OFF、ノードが無い、または見つからなければ null。
        /// </summary>
        public static bool[] CollectNodeIslands(StitchPath path, MeshSnapshot snapshot)
        {
            if (path == null || !path.islandConstrain || snapshot == null || path.nodes.Count == 0) return null;

            Reset(ref preferred, snapshot.IslandCount);
            return Add(path, snapshot, preferred, false) ? preferred : null;
        }

        static void Reset(ref bool[] flags, int count)
        {
            if (flags.Length != count) flags = new bool[count];
            else System.Array.Clear(flags, 0, count);
        }

        static bool Add(StitchPath path, MeshSnapshot snapshot, bool[] flags, bool includeSegments)
        {
            var nodes = path.nodes;
            bool found = false;
            nodeIslands.Clear();
            foreach (var node in nodes)
            {
                int island = FindIsland(snapshot, node.position, node.normal, NodeSearchRadius);
                nodeIslands.Add(island);
                found |= Mark(flags, island);
            }
            if (!includeSegments) return found;

            // 両端のアイランドが違うセグメントだけ、途中の点も調べる（経由するアイランドの取りこぼし防止）。
            // 両端が同じなら調べない。凹面で曲線が浮くと、上に重なったパーツの方が近くなるため。
            const int subdivisions = PathSampler.Subdivisions;
            int segments = PathSampler.SegmentCount(path);
            bool built = false;
            for (int s = 0; s < segments; s++)
            {
                int next = (s + 1) % nodes.Count;
                if (nodeIslands[s] == nodeIslands[next]) continue;
                if (!built)
                {
                    PathSampler.BuildPolyline(path, polyline);
                    built = true;
                }

                for (int k = 1; k < subdivisions; k++)
                {
                    var normal = Vector3.Lerp(nodes[s].normal, nodes[next].normal, (float)k / subdivisions).normalized;
                    int island = FindIsland(snapshot, polyline[s * subdivisions + k], normal, SegmentSearchRadius);
                    found |= Mark(flags, island);
                }
            }
            return found;
        }

        static int FindIsland(MeshSnapshot snapshot, Vector3 position, Vector3 normal, float radius)
        {
            int triangle = snapshot.FindSurfaceTriangle(position, normal, radius);
            return triangle >= 0 ? snapshot.TriangleIslands[triangle] : -1;
        }

        static bool Mark(bool[] flags, int island)
        {
            if (island < 0) return false;
            flags[island] = true;
            return true;
        }
    }
}
