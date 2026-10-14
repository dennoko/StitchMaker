using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dennokoworks.StitchMaker
{
    /// <summary>「適用…」で開くプリセット一覧。見出しごとにサムネイル付きのタイルを並べ、クリックで適用して閉じる。</summary>
    class PresetPicker : PopupWindowContent
    {
        public class Item
        {
            public string label;
            public string tooltip;
            public Texture2D thumbnail;
            public Action apply;
        }

        public class Section
        {
            public string title;
            public string emptyText;   // items が空のときに出す文言（null なら見出しごと省く）
            public readonly List<Item> items = new List<Item>();
        }

        const float WindowWidth = 460f, WindowHeight = 440f;

        readonly List<Section> sections;
        readonly Action<VisualElement> setupRoot;

        /// <param name="setupRoot">テーマ・フォントをポップアップのルートへ適用する処理。</param>
        public PresetPicker(List<Section> sections, Action<VisualElement> setupRoot)
        {
            this.sections = sections;
            this.setupRoot = setupRoot;
        }

        public override Vector2 GetWindowSize()
        {
            return new Vector2(WindowWidth, WindowHeight);
        }

        // 中身は UI Toolkit で作る。
        public override void OnGUI(Rect rect)
        {
        }

        public override void OnOpen()
        {
            var root = editorWindow.rootVisualElement;
            setupRoot(root);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("stitch-preset-scroll");
            root.Add(scroll);

            foreach (var section in sections)
            {
                if (section.items.Count == 0 && section.emptyText == null) continue;

                var heading = new Label(section.title);
                heading.AddToClassList("stitch-preset-heading");
                scroll.Add(heading);

                if (section.items.Count == 0)
                {
                    var empty = new Label(section.emptyText);
                    empty.AddToClassList("stitch-message");
                    scroll.Add(empty);
                    continue;
                }

                var grid = new VisualElement();
                grid.AddToClassList("stitch-preset-grid");
                foreach (var item in section.items)
                    grid.Add(CreateTile(item));
                scroll.Add(grid);
            }
        }

        VisualElement CreateTile(Item item)
        {
            var tile = new VisualElement { tooltip = item.tooltip };
            tile.AddToClassList("stitch-preset-tile");

            var image = new Image { image = item.thumbnail, scaleMode = ScaleMode.ScaleToFit };
            image.AddToClassList("stitch-preset-thumbnail");
            tile.Add(image);

            var label = new Label(item.label);
            label.AddToClassList("stitch-preset-name");
            tile.Add(label);

            tile.AddManipulator(new Clickable(() =>
            {
                item.apply();
                editorWindow.Close();
            }));
            return tile;
        }

        // ---------------------------------------------------------------- サムネイル
        // プリセットのファイルごとに、更新時刻・設定・画像をキャッシュする。更新時刻が変われば描き直す。

        struct Entry
        {
            public DateTime stamp;
            public StitchSettings settings;
            public Texture2D texture;
        }

        static readonly Dictionary<string, Entry> cache = new Dictionary<string, Entry>();
        static PresetThumbnailRenderer renderer;

        public static Item CreateBuiltInItem(PresetStore.BuiltInPreset preset, Action apply)
        {
            string assetPath = preset.assetPath;
            var entry = GetEntry(assetPath, File.GetLastWriteTimeUtc(assetPath), () =>
            {
                var settings = PresetStore.LoadBuiltIn(assetPath, out var mask);
                return (settings, mask, false);
            });
            return CreateItem(preset.name, entry, apply);
        }

        public static Item CreateUserItem(string name, Action apply)
        {
            var entry = GetEntry("user:" + name, PresetStore.GetLastWriteTime(name), () =>
            {
                var settings = PresetStore.Load(name, out var mask);
                return (settings, mask, true);
            });
            return CreateItem(name, entry, apply);
        }

        /// <summary>キャッシュした画像と描画用リソースを破棄する。</summary>
        public static void ClearCache()
        {
            foreach (var entry in cache.Values)
            {
                if (entry.texture != null) UnityEngine.Object.DestroyImmediate(entry.texture);
            }
            cache.Clear();
            renderer?.Dispose();
            renderer = null;
        }

        static Item CreateItem(string label, Entry entry, Action apply)
        {
            var s = entry.settings;
            return new Item
            {
                label = label,
                tooltip = s == null ? label : label + "\n" + Loc.F("長さ {0:0.##} mm / 幅 {1:0.##} mm / 間隔 {2:0.##} mm",
                    s.ScaledLength * 1000f, s.ScaledWidth * 1000f, s.ScaledSpacing * 1000f),
                thumbnail = entry.texture,
                apply = apply,
            };
        }

        // load は (設定, マスク, マスクを破棄するか) を返す。
        static Entry GetEntry(string key, DateTime stamp, Func<(StitchSettings, Texture2D, bool)> load)
        {
            if (cache.TryGetValue(key, out var cached))
            {
                if (cached.stamp == stamp) return cached;
                if (cached.texture != null) UnityEngine.Object.DestroyImmediate(cached.texture);
            }

            var entry = new Entry { stamp = stamp };
            var (settings, mask, ownsMask) = load();
            try
            {
                entry.settings = settings;
                if (settings != null)
                {
                    if (renderer == null) renderer = new PresetThumbnailRenderer();
                    entry.texture = renderer.Render(settings, mask);
                }
            }
            catch (Exception e)
            {
                // サムネイルが描けなくてもプリセットは選べるようにする。
                Debug.LogException(e);
            }
            finally
            {
                if (ownsMask && mask != null) UnityEngine.Object.DestroyImmediate(mask);
            }
            cache[key] = entry;
            return entry;
        }
    }
}
