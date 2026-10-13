using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Dennokoworks.StitchMaker
{
    /// <summary>
    /// プリセットのファイル入出力。1 プリセット = {name}.json + {name}.png で、ファイル名がプリセット名。
    /// </summary>
    public static class PresetStore
    {
        const string MaskFolder = "Assets/dennokoworks/StitchMakerData/Masks";

        public static string Directory
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "dennokoworks", "StitchMaker", "Presets");
                System.IO.Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string[] GetNames()
        {
            return System.IO.Directory.GetFiles(Directory, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static bool Exists(string name)
        {
            return File.Exists(JsonPath(name));
        }

        /// <summary>ファイル名に使えない文字を取り除く。</summary>
        public static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var invalid = Path.GetInvalidFileNameChars();
            return new string(name.Where(c => Array.IndexOf(invalid, c) < 0).ToArray()).Trim();
        }

        public static void Save(string name, StitchSettings settings, Texture2D mask)
        {
            File.WriteAllText(JsonPath(name), JsonUtility.ToJson(settings, true));
            if (mask != null)
                File.WriteAllBytes(PngPath(name), EncodeMask(mask));
            else if (File.Exists(PngPath(name)))
                File.Delete(PngPath(name));
        }

        /// <summary>プリセットの値をパスへコピーする。以後プリセットは参照しない。</summary>
        public static void Apply(string name, StitchPath path)
        {
            if (!Exists(name)) return;
            JsonUtility.FromJsonOverwrite(File.ReadAllText(JsonPath(name)), path.settings);
            if (File.Exists(PngPath(name)))
                path.mask = ImportMask(name, File.ReadAllBytes(PngPath(name)));
        }

        public static void Delete(string name)
        {
            if (File.Exists(JsonPath(name))) File.Delete(JsonPath(name));
            if (File.Exists(PngPath(name))) File.Delete(PngPath(name));
        }

        // ---------------------------------------------------------------- 同梱プリセット
        // ツール本体の Presets/{name}.json（StitchSettings + 下記の追加項目）と Masks/{mask}.png の組。

        [Serializable]
        class BuiltInInfo
        {
            public string category = "";
            public int order = 0;
            public string mask = "";
        }

        public struct BuiltInPreset
        {
            public string label;     // メニュー表示用（"カテゴリ/名前"）
            public string assetPath;
        }

        /// <summary>同梱プリセットを表示順に列挙する。</summary>
        public static BuiltInPreset[] GetBuiltIn()
        {
            string root = FindToolRoot();
            if (root == null || !System.IO.Directory.Exists(root + "/Presets")) return new BuiltInPreset[0];

            return System.IO.Directory.GetFiles(root + "/Presets", "*.json")
                .Select(file =>
                {
                    var info = JsonUtility.FromJson<BuiltInInfo>(File.ReadAllText(file)) ?? new BuiltInInfo();
                    string name = Path.GetFileNameWithoutExtension(file);
                    return new
                    {
                        info.order,
                        preset = new BuiltInPreset
                        {
                            label = string.IsNullOrEmpty(info.category) ? name : info.category + "/" + name,
                            assetPath = file.Replace('\\', '/'),
                        },
                    };
                })
                .OrderBy(x => x.order)
                .Select(x => x.preset)
                .ToArray();
        }

        /// <summary>同梱プリセットの値と同梱マスクをパスへコピーする。</summary>
        public static void ApplyBuiltIn(string assetPath, StitchPath path)
        {
            if (!File.Exists(assetPath)) return;
            string json = File.ReadAllText(assetPath);
            JsonUtility.FromJsonOverwrite(json, path.settings);

            var info = JsonUtility.FromJson<BuiltInInfo>(json);
            string root = FindToolRoot();
            if (info == null || string.IsNullOrEmpty(info.mask) || root == null) return;
            var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(root + "/Masks/" + info.mask + ".png");
            if (mask != null) path.mask = mask;
        }

        // このスクリプトの位置からツールのルートフォルダを求める（フォルダを移動されても動く）。
        static string FindToolRoot()
        {
            const string suffix = "/Editor/Data/PresetStore.cs";
            foreach (var guid in AssetDatabase.FindAssets("PresetStore t:MonoScript"))
            {
                var scriptPath = AssetDatabase.GUIDToAssetPath(guid);
                if (scriptPath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return scriptPath.Substring(0, scriptPath.Length - suffix.Length);
            }
            return null;
        }

        static string JsonPath(string name)
        {
            return Path.Combine(Directory, name + ".json");
        }

        static string PngPath(string name)
        {
            return Path.Combine(Directory, name + ".png");
        }

        // 非 Readable・圧縮テクスチャでも扱えるよう RenderTexture 経由で PNG 化する。
        static byte[] EncodeMask(Texture2D mask)
        {
            var readWrite = GraphicsFormatUtility.IsSRGBFormat(mask.graphicsFormat)
                ? RenderTextureReadWrite.sRGB
                : RenderTextureReadWrite.Linear;
            var rt = RenderTexture.GetTemporary(mask.width, mask.height, 0, RenderTextureFormat.ARGB32, readWrite);
            var previous = RenderTexture.active;
            Graphics.Blit(mask, rt);
            RenderTexture.active = rt;
            var copy = new Texture2D(mask.width, mask.height, TextureFormat.RGB24, false);
            copy.ReadPixels(new Rect(0, 0, mask.width, mask.height), 0, 0);
            copy.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            var bytes = copy.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(copy);
            return bytes;
        }

        // 内容ハッシュ付きの名前でプロジェクトへ取り込む。同じ内容が既にあれば再利用する。
        static Texture2D ImportMask(string name, byte[] png)
        {
            string hash;
            using (var md5 = MD5.Create())
                hash = BitConverter.ToString(md5.ComputeHash(png)).Replace("-", "").Substring(0, 8).ToLowerInvariant();

            var assetPath = MaskFolder + "/" + name + "_" + hash + ".png";
            if (!File.Exists(assetPath))
            {
                System.IO.Directory.CreateDirectory(MaskFolder);
                File.WriteAllBytes(assetPath, png);
                AssetDatabase.ImportAsset(assetPath);
                if (AssetImporter.GetAtPath(assetPath) is TextureImporter importer)
                {
                    importer.sRGBTexture = false;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.mipmapEnabled = true;
                    importer.SaveAndReimport();
                }
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }
    }
}
