using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dennokoworks.StitchMaker
{
    /// <summary>
    /// 日本語 / 英語のローカライズ。日本語の文言そのものをキーにし、英語は対訳表から引く。
    /// 対訳表に無い文言は日本語のまま返す。
    /// </summary>
    internal static class Loc
    {
        const string PrefKey = "StitchMaker_Language";
        // この USS クラスが付いた要素とその子孫は、Apply() の対象から外す（コードが文言を入れる要素）。
        const string NoLocClass = "stitch-no-loc";

        class Source
        {
            public string text;
        }

        // UXML に書かれていた元の文言（= キー）。要素が破棄されれば一緒に解放される。
        static readonly ConditionalWeakTable<VisualElement, Source> sources =
            new ConditionalWeakTable<VisualElement, Source>();

        static bool loaded;
        static bool english;

        /// <summary>表示言語。既定は OS の言語（日本語以外は英語）。</summary>
        public static bool IsEnglish
        {
            get
            {
                if (!loaded)
                {
                    string fallback = Application.systemLanguage == SystemLanguage.Japanese ? "ja" : "en";
                    english = EditorPrefs.GetString(PrefKey, fallback) == "en";
                    loaded = true;
                }
                return english;
            }
            set
            {
                english = value;
                loaded = true;
                EditorPrefs.SetString(PrefKey, value ? "en" : "ja");
            }
        }

        public static string T(string japanese)
        {
            if (string.IsNullOrEmpty(japanese) || !IsEnglish) return japanese;
            return Table.TryGetValue(japanese, out var translated) ? translated : japanese;
        }

        public static string F(string japanese, params object[] args)
        {
            return string.Format(T(japanese), args);
        }

        /// <summary>
        /// root 以下の Label / Button（フィールドのラベル・トグルの文言を含む）を現在の言語にする。
        /// 言語を切り替えるたびに呼び直してよい。
        /// </summary>
        public static void Apply(VisualElement root)
        {
            root.Query<TextElement>().ForEach(element =>
            {
                if (!(element is Label) && !(element is Button)) return;
                // ObjectField に表示されるアセット名は文言ではない。
                if (element.ClassListContains("unity-object-field-display__label")) return;
                if (IsExcluded(element, root)) return;

                if (!sources.TryGetValue(element, out var source))
                {
                    if (!Table.ContainsKey(element.text)) return;
                    source = new Source { text = element.text };
                    sources.Add(element, source);
                }
                element.text = T(source.text);
            });
        }

        static bool IsExcluded(VisualElement element, VisualElement root)
        {
            for (var e = element; e != null && e != root; e = e.parent)
            {
                if (e.ClassListContains(NoLocClass)) return true;
            }
            return false;
        }

        static readonly Dictionary<string, string> Table = new Dictionary<string, string>
        {
            // ---- UXML
            { "対象", "TARGET" },
            { "クリア", "Clear" },
            { "メッシュを再取得", "Reload Mesh" },
            { "パス", "PATHS" },
            { "新規", "New" },
            { "読込", "Load" },
            { "保存", "Save" },
            { "+ パスを追加", "+ Add Path" },
            { "編集モード", "Edit Mode" },
            { "閉じる", "Closed" },
            { "ベジェハンドル", "Bezier Handles" },
            { "ステッチ設定（選択中のパス）", "STITCH SETTINGS (SELECTED PATH)" },
            { "プリセット", "Preset" },
            { "適用…", "Apply…" },
            { "削除…", "Delete…" },
            { "フォルダ", "Folder" },
            { "プリセット名", "Preset Name" },
            { "名前", "Name" },
            { "マスク", "Mask" },
            { "長さ (mm)", "Length (mm)" },
            { "幅 (mm)", "Width (mm)" },
            { "間隔 (mm)", "Spacing (mm)" },
            { "角度 (°)", "Angle (°)" },
            { "糸の色", "Thread Color" },
            { "高さ", "Height" },
            { "凹凸の強さ", "Normal Strength" },
            { "ゆらぎ・位置 (mm)", "Jitter: Position (mm)" },
            { "ゆらぎ・角度 (°)", "Jitter: Angle (°)" },
            { "マスクが未設定のパスは描画されません。", "A path without a mask is not rendered." },
            { "プレビュー・出力", "PREVIEW / EXPORT" },
            { "プレビュー", "Preview" },
            { "解像度", "Resolution" },
            { "パディング (px)", "Padding (px)" },
            { "出力", "Output" },
            { "出力先", "Output Folder" },
            { "上書き保存", "Overwrite" },
            { "テクスチャを出力", "Export Textures" },

            // ---- ウィンドウ（コードから設定する文言）
            { "準備完了", "Ready" },
            { "ステッチを描く Renderer を指定してください。", "Assign the Renderer to draw stitches on." },
            { "更新あり {0}", "Update available {0}" },
            { "最新版を取得できません", "Could not check for updates" },
            { "確認中...", "Checking..." },
            { "アップデートを再確認", "Check for updates again" },
            { "日本語で表示", "Display in Japanese" },
            { "英語で表示", "Display in English" },
            { "シーンビューでパスを編集する（ON の間はオブジェクトを選択できません）", "Edit paths in the Scene view (objects cannot be selected while ON)" },
            { "対象の Renderer を解除", "Clear the assigned Renderer" },
            { "同名ファイルが存在する場合に上書きする（OFF の場合は連番を付与）", "Overwrite existing files (append sequential numbers when OFF)" },
            { "表示 / 非表示（非表示のパスは出力されません）", "Show / hide (hidden paths are not exported)" },
            { "このパスを削除", "Delete this path" },
            { "同梱", "Built-in" },
            { "基本", "Basic" },
            { "糸の質感", "Thread Texture" },
            { "かがり・端処理", "Edge Finishing" },
            { "装飾", "Decorative" },
            { "特殊", "Special" },
            { "（保存したプリセットはありません）", "(No saved presets)" },
            { "プリセットを適用しました: {0}", "Preset applied: {0}" },
            { "プリセット「{0}」を削除しますか？", "Delete the preset \"{0}\"?" },
            { "削除", "Delete" },
            { "キャンセル", "Cancel" },
            { "プリセットを削除しました: {0}", "Preset deleted: {0}" },
            { "プリセット名を入力してください。", "Enter a preset name." },
            { "プリセット「{0}」を上書きしますか？", "Overwrite the preset \"{0}\"?" },
            { "上書き", "Overwrite" },
            { "プリセットを保存しました: {0}", "Preset saved: {0}" },
            { "出力先フォルダ", "Output Folder" },
            { "プロジェクトの Assets フォルダ内を指定してください。", "Choose a folder inside the project's Assets folder." },
            { "出力先は Assets フォルダ内を指定してください。", "The output folder must be inside the Assets folder." },
            { "対象メッシュを取得できません。", "Could not read the target mesh." },
            { "テクスチャを出力しました: {0}", "Textures exported: {0}" },
            { "出力に失敗しました: {0}", "Export failed: {0}" },
            { "プレビューの描画に失敗しました（Console を確認してください）", "Preview rendering failed (see the Console)" },
            { "保存していない変更があります。破棄してよろしいですか？", "There are unsaved changes. Discard them?" },
            { "破棄", "Discard" },
            { "新しいドキュメントを作成しました", "Created a new document" },
            { "パスを読み込む", "Load Paths" },
            { "StitchMaker のパスアセットではありません。", "This is not a StitchMaker path asset." },
            { "パスを読み込みました: {0}", "Paths loaded: {0}" },
            { "パスを保存", "Save Paths" },
            { "保存先を指定してください。", "Choose where to save." },
            { "パスを保存しました: {0}", "Paths saved: {0}" },
            { "UXML が見つかりません。GUID を確認してください。", "UXML asset not found. Check the GUID." },

            // ---- MeshSnapshot のエラー
            { "対象の Renderer にメッシュがありません（SkinnedMeshRenderer か、MeshFilter 付きの MeshRenderer を指定してください）。",
              "The Renderer has no mesh (use a SkinnedMeshRenderer, or a MeshRenderer with a MeshFilter)." },
            { "メッシュの頂点を読み取れません（Read/Write を有効にしてください）。", "Cannot read the mesh vertices (enable Read/Write)." },
            { "メッシュに UV0 がありません。", "The mesh has no UV0." },
            { "三角形以外のトポロジーには対応していません。", "Only triangle topology is supported." },
        };
    }
}
