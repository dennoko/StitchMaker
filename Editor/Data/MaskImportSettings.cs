using UnityEditor;

namespace Dennokoworks.StitchMaker
{
    /// <summary>
    /// 同梱マスク（StitchMaker/Masks/）のインポート設定を固定する。
    /// マスクの濃淡は高さとして読むため、sRGB 変換と圧縮ノイズを避ける。
    /// </summary>
    class MaskImportSettings : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (assetPath.IndexOf("/StitchMaker/Masks/", System.StringComparison.OrdinalIgnoreCase) < 0) return;

            var importer = (TextureImporter)assetImporter;
            importer.sRGBTexture = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = true;
        }
    }
}
