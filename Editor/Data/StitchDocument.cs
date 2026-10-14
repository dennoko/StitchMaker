using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Dennokoworks.StitchMaker
{
    [Serializable]
    public class ExportSettings
    {
        public const string FallbackFolder = "Assets/dennokoworks/StitchMakerData/Textures";

        public int resolution = 2048;
        public int padding = 4;
        // 出力時のアンチエイリアス（SSAA）の倍率。1 = OFF、2 = 2x（推奨）、4 = 4x。プレビューには掛けない。
        public int ssaa = 2;
        public bool color = true;
        public bool normal = true;
        public bool height = true;
        public bool mask = true;
        public bool overwrite = false;
        // Normal / Height もステッチ以外を透過にして書き出す（Color は常に透過）。
        public bool transparent = false;
        public string folder = FallbackFolder;
    }

    /// <summary>編集対象のすべて（パス一覧 + 出力設定）。</summary>
    public class StitchDocument : ScriptableObject
    {
        const string DefaultMaskSuffix = "StitchMaker/Masks/Straight.png";

        public List<StitchPath> paths = new List<StitchPath>();
        public ExportSettings export = new ExportSettings();

        /// <summary>
        /// 新規パスを末尾に追加する。template を渡すとステッチ設定とマスクを引き継ぎ（同じステッチを複数箇所に置く用途）、
        /// 無ければ同梱マスクを割り当てる。形状とミラー設定は引き継がない。
        /// </summary>
        public StitchPath AddPath(StitchPath template = null)
        {
            var path = new StitchPath
            {
                name = "Path " + (paths.Count + 1),
                mask = template != null ? template.mask : FindDefaultMask(),
            };
            if (template != null) path.settings = template.settings.Clone();
            paths.Add(path);
            return path;
        }

        /// <summary>パスを形状・設定ごと複製し、元のパスの直後に挿入する。</summary>
        public StitchPath DuplicatePath(int index)
        {
            var source = paths[index];
            var copy = new StitchPath
            {
                name = source.name + " (Copy)",
                visible = source.visible,
                closed = source.closed,
                manualHandles = source.manualHandles,
                mirrorAxis = source.mirrorAxis,
                mirrorSnapRadius = source.mirrorSnapRadius,
                mask = source.mask,
                settings = source.settings.Clone(),
            };
            foreach (var node in source.nodes)
            {
                copy.nodes.Add(new StitchNode
                {
                    position = node.position,
                    normal = node.normal,
                    handleIn = node.handleIn,
                    handleOut = node.handleOut,
                });
            }
            paths.Insert(index + 1, copy);
            return copy;
        }

        static Texture2D FindDefaultMask()
        {
            foreach (var guid in AssetDatabase.FindAssets("Straight t:Texture2D"))
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (assetPath.EndsWith(DefaultMaskSuffix, StringComparison.OrdinalIgnoreCase))
                    return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            }
            return null;
        }
    }
}
