using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dennokoworks.StitchMaker
{
    /// <summary>状態保持・設定 UI（UI Toolkit）・再構築の起点。</summary>
    public class StitchMakerWindow : EditorWindow
    {
        // UXML / USS は GUID でロードする（.meta に記載の GUID）。
        const string UXML_GUID = "e8e2baa91058430086be3de06b734b35";
        const string THEME_USS_GUID = "392dd82577e34eada11daa10c3bf7d88";
        const string USS_GUID = "aca43ac834414adca1b2ffa76ae1cf32";

        const string ActiveClass = "dennoko-button-active";
        const string HiddenClass = "stitch-hidden";

        static readonly List<string> ResolutionChoices = new List<string> { "512", "1024", "2048", "4096" };
        static readonly List<string> MirrorAxisChoices = new List<string> { "None", "X", "Y", "Z" };

        public enum StatusType { Info, Success, Error }

        [SerializeField] StitchDocument document;
        [SerializeField] Renderer target;
        [SerializeField] int materialIndex;
        [SerializeField] int selectedPath = -1;
        [SerializeField] int selectedNode = -1;
        [SerializeField] bool editMode = true;
        [SerializeField] bool previewOn = true;
        [SerializeField] bool dirty;
        [SerializeField] PreviewMode previewMode = PreviewMode.Combined;
        [SerializeField] string presetName = "";

        MeshSnapshot snapshot;
        string snapshotError;
        TextureBaker baker;
        bool needsRebuild;

        // UI 要素（CreateGUI で取得）
        bool uiReady;
        Label statusLabel, versionLabel, targetMessage, maskWarning;
        IVisualElementScheduledItem statusResetSchedule;
        Button langJaButton, langEnButton, reloadButton, editModeButton;
        Button clearTargetButton, refreshMeshButton;
        Button previewCombinedButton, previewNormalButton, previewHeightButton;
        ObjectField targetField, maskField;
        DropdownField materialDropdown, resolutionDropdown;
        VisualElement pathList, settingsCard;
        Toggle closedToggle, bezierToggle, previewToggle;
        Toggle exportColorToggle, exportNormalToggle, exportHeightToggle, exportMaskToggle, exportOverwriteToggle;
        Toggle exportTransparentToggle;
        TextField presetNameField, pathNameField, exportFolderField;
        ColorField threadColorField;
        IntegerField seedField;
        DropdownField mirrorAxisDropdown;
        FloatField mirrorSnapField;
        Button bakeMirrorButton;
        Label mirrorNote;
        SliderInt paddingSlider;
        // 選択中パスの設定値をコントロールへ流し込む処理（スライダーごとに 1 つ）
        readonly List<Action<StitchSettings>> settingsRefreshers = new List<Action<StitchSettings>>();

        // フィールド初期化子で StitchMakerVersion.Current を呼ばないこと（ScriptableObject コンストラクタ制約）。
        DennokoVersionChecker.Result versionResult = new DennokoVersionChecker.Result
        {
            State = DennokoVersionChecker.State.Checking,
            LocalVersion = "0.0.0",
        };

        [MenuItem("dennokoworks/StitchMaker")]
        public static void ShowWindow()
        {
            var window = GetWindow<StitchMakerWindow>();
            window.titleContent = new GUIContent("StitchMaker");
            window.minSize = new Vector2(360, 480);
        }

        void OnEnable()
        {
            if (document == null) document = CreateDocument();
            needsRebuild = true;
            SceneView.duringSceneGui += OnSceneGUI;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.update += OnEditorUpdate;
        }

        void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.update -= OnEditorUpdate;
            Tools.hidden = false;
            DisposeSnapshot();
            baker?.Dispose();
            baker = null;
            PresetPicker.ClearCache();
        }

        void OnDestroy()
        {
            // ウィンドウを閉じたらメモリ上のドキュメントも破棄する。
            if (document != null)
            {
                Undo.ClearUndo(document);
                DestroyImmediate(document);
            }
        }

        // ---------------------------------------------------------------- UI 構築

        public void CreateGUI()
        {
            uiReady = false;
            settingsRefreshers.Clear();
            var root = rootVisualElement;
            ApplyTheme(root);

            string uxmlPath = AssetDatabase.GUIDToAssetPath(UXML_GUID);
            var uxml = string.IsNullOrEmpty(uxmlPath) ? null : AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
            if (uxml == null)
            {
                root.Add(new Label(Loc.T("UXML が見つかりません。GUID を確認してください。")));
                return;
            }
            uxml.CloneTree(root);

            if (document == null) document = CreateDocument();
            else if (target != null && document.export.folder == ExportSettings.FallbackFolder)
            {
                document.export.folder = GetDefaultExportFolder();
            }
            InitializeUI(root);
            uiReady = true;

            Loc.Apply(root);
            ApplyLocalizedTooltips();
            SetStatus(Loc.T("準備完了"), StatusType.Info);
            RefreshUI();
            StartVersionCheck();
        }

        // ウィンドウとプリセット一覧ポップアップで共通のテーマ設定。
        static void ApplyTheme(VisualElement root)
        {
            // テーマ非依存のためのルートクラス。USS ロード失敗時も背景が明るくならないよう C# 側でも保証する。
            root.AddToClassList("dennoko-root");
            root.style.backgroundColor = (Color)new Color32(0x12, 0x12, 0x12, 0xFF);
            root.style.flexGrow = 1;
            DennokoUIFont.Apply(root);

            AddStyleSheet(root, THEME_USS_GUID);
            AddStyleSheet(root, USS_GUID);
        }

        static void AddStyleSheet(VisualElement root, string guid)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var sheet = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            if (sheet != null)
                root.styleSheets.Add(sheet);
            else
                Debug.LogWarning($"[{nameof(StitchMakerWindow)}] USS が見つかりません。GUID を確認してください: {guid}");
        }

        void InitializeUI(VisualElement root)
        {
            statusLabel = root.Q<Label>("status-label");
            versionLabel = root.Q<Label>("version-label");

            // ヘッダー
            reloadButton = root.Q<Button>("version-reload-button");
            reloadButton.clicked += () =>
            {
                StitchMakerVersion.ForceRecheck();
                LoadVersionResultFromSessionState(); // 即座に「確認中...」表示へ
            };
            langJaButton = root.Q<Button>("lang-ja-button");
            langEnButton = root.Q<Button>("lang-en-button");
            langJaButton.clicked += () => SetLanguage(false);
            langEnButton.clicked += () => SetLanguage(true);

            // 対象
            targetField = root.Q<ObjectField>("target-field");
            targetField.RegisterValueChangedCallback(evt =>
            {
                target = evt.newValue as Renderer;
                materialIndex = 0;
                UpdateExportFolder();
                RefreshSnapshot();
            });
            clearTargetButton = root.Q<Button>("clear-target-button");
            clearTargetButton.clicked += () =>
            {
                target = null;
                materialIndex = 0;
                targetField.value = null;
                UpdateExportFolder();
                RefreshSnapshot();
            };
            materialDropdown = root.Q<DropdownField>("material-dropdown");
            materialDropdown.RegisterValueChangedCallback(evt =>
            {
                materialIndex = Mathf.Max(0, materialDropdown.index);
                UpdateExportFolder();
                RefreshSnapshot();
            });
            refreshMeshButton = root.Q<Button>("refresh-mesh-button");
            refreshMeshButton.clicked += RefreshSnapshot;
            targetMessage = root.Q<Label>("target-message");

            // パス
            root.Q<Button>("new-document-button").clicked += NewDocument;
            root.Q<Button>("load-document-button").clicked += LoadDocument;
            root.Q<Button>("save-document-button").clicked += SaveDocument;
            pathList = root.Q<VisualElement>("path-list");
            root.Q<Button>("add-path-button").clicked += AddPath;
            editModeButton = root.Q<Button>("edit-mode-button");
            editModeButton.clicked += () =>
            {
                editMode = !editMode;
                RefreshUI();
                SceneView.RepaintAll();
            };
            closedToggle = root.Q<Toggle>("closed-toggle");
            closedToggle.RegisterValueChangedCallback(evt =>
                EditPath("Toggle Closed Path", path => path.closed = evt.newValue));
            bezierToggle = root.Q<Toggle>("bezier-toggle");
            bezierToggle.RegisterValueChangedCallback(evt => EditPath("Toggle Bezier Handles", path =>
            {
                // 見た目が変わらないよう、現在の自動ハンドルを書き込んでから切り替える。
                if (evt.newValue) PathSampler.BakeAutoHandles(path);
                path.manualHandles = evt.newValue;
            }));

            // ステッチ設定
            settingsCard = root.Q<VisualElement>("settings-card");
            var applyPresetButton = root.Q<Button>("apply-preset-button");
            applyPresetButton.clicked += () => ShowApplyMenu(applyPresetButton);
            var deletePresetButton = root.Q<Button>("delete-preset-button");
            deletePresetButton.clicked += () => ShowDeleteMenu(deletePresetButton);
            root.Q<Button>("preset-folder-button").clicked += () => EditorUtility.RevealInFinder(PresetStore.Directory);
            presetNameField = root.Q<TextField>("preset-name-field");
            presetNameField.RegisterValueChangedCallback(evt => presetName = evt.newValue);
            root.Q<Button>("save-preset-button").clicked += SavePreset;

            pathNameField = root.Q<TextField>("path-name-field");
            pathNameField.RegisterValueChangedCallback(evt =>
            {
                EditPath("Rename Stitch Path", path => path.name = evt.newValue);
                RefreshPathList();
            });
            maskField = root.Q<ObjectField>("mask-field");
            maskField.RegisterValueChangedCallback(evt =>
            {
                EditPath("Edit Stitch Settings", path => path.mask = evt.newValue as Texture2D);
                RefreshMaskWarning();
            });
            maskWarning = root.Q<Label>("mask-warning");

            BindSlider(root, "scale-slider", 1f, s => s.scale, (s, v) => s.scale = Mathf.Max(v, StitchSettings.MinScale));
            // スライダーは mm 表示、データはメートル。
            BindSlider(root, "length-slider", 1000f, s => s.length, (s, v) => s.length = v);
            BindSlider(root, "width-slider", 1000f, s => s.width, (s, v) => s.width = v);
            BindSlider(root, "spacing-slider", 1000f, s => s.spacing, (s, v) => s.spacing = v);
            BindSlider(root, "angle-slider", 1f, s => s.angleOffset, (s, v) => s.angleOffset = v);
            BindSlider(root, "height-slider", 1f, s => s.heightScale, (s, v) => s.heightScale = v);
            BindSlider(root, "normal-slider", 1f, s => s.normalStrength, (s, v) => s.normalStrength = v);
            BindSlider(root, "jitter-position-slider", 1000f, s => s.jitterPosition, (s, v) => s.jitterPosition = v);
            BindSlider(root, "jitter-angle-slider", 1f, s => s.jitterAngle, (s, v) => s.jitterAngle = v);
            threadColorField = root.Q<ColorField>("thread-color-field");
            threadColorField.RegisterValueChangedCallback(evt =>
                EditPath("Edit Stitch Settings", path => path.settings.threadColor = evt.newValue));
            seedField = root.Q<IntegerField>("seed-field");
            seedField.RegisterValueChangedCallback(evt =>
                EditPath("Edit Stitch Settings", path => path.settings.seed = evt.newValue));

            // ミラー。選択肢の並びは MirrorAxis の値と一致させる。
            mirrorAxisDropdown = root.Q<DropdownField>("path-mirror-axis");
            mirrorAxisDropdown.choices = MirrorAxisChoices;
            mirrorAxisDropdown.RegisterValueChangedCallback(evt =>
            {
                EditPath("Edit Stitch Mirror", path => path.mirrorAxis = (MirrorAxis)Mathf.Max(0, mirrorAxisDropdown.index));
                RefreshMirrorControls();
            });
            // 表示は mm、データはメートル。
            mirrorSnapField = root.Q<FloatField>("path-mirror-snap-radius");
            mirrorSnapField.RegisterValueChangedCallback(evt =>
                EditPath("Edit Stitch Mirror", path => path.mirrorSnapRadius = Mathf.Max(0f, evt.newValue) / 1000f));
            bakeMirrorButton = root.Q<Button>("path-bake-mirror-button");
            bakeMirrorButton.clicked += BakeMirrorToPath;
            mirrorNote = root.Q<Label>("mirror-note");

            // プレビュー・出力
            previewToggle = root.Q<Toggle>("preview-toggle");
            previewToggle.RegisterValueChangedCallback(evt =>
            {
                previewOn = evt.newValue;
                needsRebuild = true;
                SceneView.RepaintAll();
            });
            previewCombinedButton = root.Q<Button>("preview-combined-button");
            previewNormalButton = root.Q<Button>("preview-normal-button");
            previewHeightButton = root.Q<Button>("preview-height-button");
            previewCombinedButton.clicked += () => SetPreviewMode(PreviewMode.Combined);
            previewNormalButton.clicked += () => SetPreviewMode(PreviewMode.Normal);
            previewHeightButton.clicked += () => SetPreviewMode(PreviewMode.Height);

            resolutionDropdown = root.Q<DropdownField>("resolution-dropdown");
            resolutionDropdown.choices = ResolutionChoices;
            resolutionDropdown.RegisterValueChangedCallback(evt =>
            {
                if (int.TryParse(evt.newValue, out int resolution))
                    EditExport(export => export.resolution = resolution);
            });
            paddingSlider = root.Q<SliderInt>("padding-slider");
            paddingSlider.RegisterValueChangedCallback(evt => EditExport(export => export.padding = evt.newValue));
            exportColorToggle = BindExportToggle(root, "export-color-toggle", (export, v) => export.color = v);
            exportNormalToggle = BindExportToggle(root, "export-normal-toggle", (export, v) => export.normal = v);
            exportHeightToggle = BindExportToggle(root, "export-height-toggle", (export, v) => export.height = v);
            exportMaskToggle = BindExportToggle(root, "export-mask-toggle", (export, v) => export.mask = v);
            exportOverwriteToggle = BindExportToggle(root, "export-overwrite-toggle", (export, v) => export.overwrite = v);
            exportTransparentToggle = BindExportToggle(root, "export-transparent-toggle", (export, v) => export.transparent = v);
            exportFolderField = root.Q<TextField>("export-folder-field");
            exportFolderField.isDelayed = true;
            exportFolderField.RegisterValueChangedCallback(evt => EditExport(export => export.folder = evt.newValue));
            root.Q<Button>("export-folder-button").clicked += PickExportFolder;
            root.Q<Button>("export-button").clicked += ExportTextures;
        }

        void BindSlider(VisualElement root, string name, float scale,
            Func<StitchSettings, float> get, Action<StitchSettings, float> set)
        {
            var slider = root.Q<Slider>(name);
            slider.RegisterValueChangedCallback(evt =>
                EditPath("Edit Stitch Settings", path => set(path.settings, evt.newValue / scale)));
            settingsRefreshers.Add(settings => slider.SetValueWithoutNotify(get(settings) * scale));
        }

        Toggle BindExportToggle(VisualElement root, string name, Action<ExportSettings, bool> set)
        {
            var toggle = root.Q<Toggle>(name);
            toggle.RegisterValueChangedCallback(evt => EditExport(export => set(export, evt.newValue)));
            return toggle;
        }

        // ---------------------------------------------------------------- モデルの変更

        void EditPath(string undoName, Action<StitchPath> change)
        {
            var path = SelectedPath;
            if (path == null) return;
            Undo.RecordObject(document, undoName);
            change(path);
            MarkChanged();
        }

        void EditExport(Action<ExportSettings> change)
        {
            if (document == null) return;
            Undo.RecordObject(document, "Edit Stitch Export Settings");
            change(document.export);
            MarkChanged();
        }

        void MarkChanged()
        {
            dirty = true;
            needsRebuild = true;
            SceneView.RepaintAll();
        }

        void AddPath()
        {
            if (document == null) return;
            Undo.RecordObject(document, "Add Stitch Path");
            var added = document.AddPath();
            SelectPath(document.paths.Count - 1);
            presetName = added.name;
            MarkChanged();
            RefreshUI();
        }

        void DeletePath(int index)
        {
            if (document == null || index < 0 || index >= document.paths.Count) return;
            Undo.RecordObject(document, "Delete Stitch Path");
            document.paths.RemoveAt(index);
            if (selectedPath == index) selectedNode = -1;
            if (selectedPath > index) selectedPath--;
            ClampSelection();
            MarkChanged();
            RefreshUI();
        }

        /// <summary>ミラー側を独立したパスとして追加し、元パスのミラーを解除する。</summary>
        void BakeMirrorToPath()
        {
            var path = SelectedPath;
            if (path == null || path.mirrorAxis == MirrorAxis.None) return;
            EnsureSnapshot();
            if (snapshot == null)
            {
                SetStatus(Loc.T(snapshotError ?? "対象メッシュを取得できません。"), StatusType.Error);
                return;
            }

            Undo.RecordObject(document, "Bake Stitch Mirror to Path");
            var mirrored = PathSampler.CreateMirroredPath(path, snapshot);
            mirrored.name = path.name + " (Mirror)";
            path.mirrorAxis = MirrorAxis.None;
            document.paths.Add(mirrored);
            SelectPath(document.paths.Count - 1);
            MarkChanged();
            RefreshUI();
            SetStatus(Loc.F("ミラーを独立パスとして実体化しました: {0}", mirrored.name), StatusType.Success);
        }

        void SelectPath(int index)
        {
            selectedPath = index;
            selectedNode = -1;
            var path = SelectedPath;
            if (path != null) presetName = path.name;
        }

        void SetPreviewMode(PreviewMode mode)
        {
            previewMode = mode;
            RefreshUI();
            SceneView.RepaintAll();
        }

        void SetLanguage(bool english)
        {
            Loc.IsEnglish = english;
            Loc.Apply(rootVisualElement);
            ApplyLocalizedTooltips();
            SetStatus(Loc.T("準備完了"), StatusType.Info);
            RefreshUI();
            ApplyVersionLabel();
        }

        void ApplyLocalizedTooltips()
        {
            reloadButton.tooltip = Loc.T("アップデートを再確認");
            langJaButton.tooltip = Loc.T("日本語で表示");
            langEnButton.tooltip = Loc.T("英語で表示");
            editModeButton.tooltip = Loc.T("シーンビューでパスを編集する（ON の間はオブジェクトを選択できません）");
            if (clearTargetButton != null) clearTargetButton.tooltip = Loc.T("対象の Renderer を解除");
            rootVisualElement.Q<Slider>("scale-slider").tooltip = Loc.T("長さ・幅・間隔・ゆらぎ（位置）にまとめて掛ける倍率。比率を保ったまま大きさを変える（各項目の表示値は倍率を掛ける前の値）");
            exportTransparentToggle.tooltip = Loc.T("Normal / Height もステッチ以外を透過にして出力する（Color は常に透過、Mask は対象外）");
            mirrorAxisDropdown.tooltip = Loc.T("メッシュのローカル座標で、選んだ軸の反対側にも同じステッチを描く（左右で UV アイランドを重ねているメッシュでは OFF にしてください）");
            mirrorSnapField.tooltip = Loc.T("反転した制御点を反対側の表面へ吸着させる距離。左右で形が違うメッシュ向け（0 で吸着しない）");
            bakeMirrorButton.tooltip = Loc.T("ミラー側を個別に編集できる新しいパスに変換し、このパスのミラーを解除する");
            if (exportOverwriteToggle != null)
                exportOverwriteToggle.tooltip = Loc.T("同名ファイルが存在する場合に上書きする（OFF の場合は連番を付与）");
        }

        // ---------------------------------------------------------------- モデル → UI

        /// <summary>現在の状態を UI 全体へ反映する（コールバックは発火させない）。</summary>
        void RefreshUI()
        {
            if (!uiReady || document == null) return;
            ClampSelection();

            langJaButton.EnableInClassList(ActiveClass, !Loc.IsEnglish);
            langEnButton.EnableInClassList(ActiveClass, Loc.IsEnglish);

            targetField.SetValueWithoutNotify(target);
            clearTargetButton?.SetEnabled(target != null);
            refreshMeshButton?.SetEnabled(target != null);
            RefreshMaterialDropdown();
            RefreshTargetMessage();

            RefreshPathList();
            editModeButton.EnableInClassList(ActiveClass, editMode);

            var path = SelectedPath;
            closedToggle.SetEnabled(path != null);
            bezierToggle.SetEnabled(path != null);
            closedToggle.SetValueWithoutNotify(path != null && path.closed);
            bezierToggle.SetValueWithoutNotify(path != null && path.manualHandles);

            settingsCard.EnableInClassList(HiddenClass, path == null);
            if (path != null)
            {
                presetNameField.SetValueWithoutNotify(presetName);
                pathNameField.SetValueWithoutNotify(path.name);
                maskField.SetValueWithoutNotify(path.mask);
                threadColorField.SetValueWithoutNotify(path.settings.threadColor);
                seedField.SetValueWithoutNotify(path.settings.seed);
                foreach (var refresh in settingsRefreshers) refresh(path.settings);
                mirrorAxisDropdown.SetValueWithoutNotify(
                    MirrorAxisChoices[Mathf.Clamp((int)path.mirrorAxis, 0, MirrorAxisChoices.Count - 1)]);
                mirrorSnapField.SetValueWithoutNotify(path.mirrorSnapRadius * 1000f);
            }
            RefreshMirrorControls();
            RefreshMaskWarning();

            previewToggle.SetValueWithoutNotify(previewOn);
            previewCombinedButton.EnableInClassList(ActiveClass, previewMode == PreviewMode.Combined);
            previewNormalButton.EnableInClassList(ActiveClass, previewMode == PreviewMode.Normal);
            previewHeightButton.EnableInClassList(ActiveClass, previewMode == PreviewMode.Height);

            var export = document.export;
            resolutionDropdown.SetValueWithoutNotify(export.resolution.ToString());
            paddingSlider.SetValueWithoutNotify(export.padding);
            exportColorToggle.SetValueWithoutNotify(export.color);
            exportNormalToggle.SetValueWithoutNotify(export.normal);
            exportHeightToggle.SetValueWithoutNotify(export.height);
            exportMaskToggle.SetValueWithoutNotify(export.mask);
            exportOverwriteToggle.SetValueWithoutNotify(export.overwrite);
            exportTransparentToggle.SetValueWithoutNotify(export.transparent);
            exportFolderField.SetValueWithoutNotify(export.folder);
        }

        void RefreshMaterialDropdown()
        {
            var choices = new List<string>();
            if (target != null)
            {
                var materials = target.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                    choices.Add(i + ": " + (materials[i] != null ? materials[i].name : "(None)"));
            }
            materialDropdown.choices = choices;
            materialDropdown.SetEnabled(choices.Count > 0);
            if (choices.Count > 0)
            {
                materialIndex = Mathf.Clamp(materialIndex, 0, choices.Count - 1);
                materialDropdown.SetValueWithoutNotify(choices[materialIndex]);
            }
            else
            {
                materialDropdown.SetValueWithoutNotify("");
            }
        }

        void RefreshTargetMessage()
        {
            if (!uiReady) return;
            string message = null;
            if (target == null) message = Loc.T("ステッチを描く Renderer を指定してください。");
            else if (snapshotError != null) message = Loc.T(snapshotError);

            targetMessage.text = message ?? "";
            targetMessage.EnableInClassList(HiddenClass, message == null);
            targetMessage.EnableInClassList("stitch-message--error", target != null && snapshotError != null);
        }

        // 吸着半径・実体化ボタン・注記はミラー有効時だけ意味を持つ。
        void RefreshMirrorControls()
        {
            var path = SelectedPath;
            bool mirrored = path != null && path.mirrorAxis != MirrorAxis.None;
            mirrorSnapField.SetEnabled(mirrored);
            bakeMirrorButton.EnableInClassList(HiddenClass, !mirrored);
            mirrorNote.EnableInClassList(HiddenClass, !mirrored);
        }

        void RefreshMaskWarning()
        {
            var path = SelectedPath;
            maskWarning.EnableInClassList(HiddenClass, path == null || path.mask != null);
        }

        // パス一覧は動的リストなので C# で行を生成する（見た目は USS クラスで付ける）。
        void RefreshPathList()
        {
            if (!uiReady || document == null) return;
            pathList.Clear();
            for (int i = 0; i < document.paths.Count; i++)
            {
                int index = i;
                var path = document.paths[i];

                var row = new VisualElement();
                row.AddToClassList("stitch-row");

                var visible = new Toggle { value = path.visible, tooltip = Loc.T("表示 / 非表示（非表示のパスは出力されません）") };
                visible.AddToClassList("stitch-fixed");
                visible.RegisterValueChangedCallback(evt =>
                {
                    Undo.RecordObject(document, "Toggle Stitch Path");
                    path.visible = evt.newValue;
                    MarkChanged();
                });

                var select = new Button(() =>
                {
                    SelectPath(index);
                    RefreshUI();
                    SceneView.RepaintAll();
                }) { text = path.name };
                select.AddToClassList("stitch-grow");
                select.AddToClassList("stitch-path-name");
                select.EnableInClassList(ActiveClass, index == selectedPath);

                // クリック処理の途中で自分の行を作り直さないよう、次のフレームで削除する。
                var delete = new Button(() => rootVisualElement.schedule.Execute(() => DeletePath(index)))
                {
                    text = "−",
                    tooltip = Loc.T("このパスを削除"),
                };
                delete.AddToClassList("stitch-icon-button");

                row.Add(visible);
                row.Add(select);
                row.Add(delete);
                pathList.Add(row);
            }
        }

        // ---------------------------------------------------------------- ステータスバー

        /// <summary>ステータスを表示する。Success / Error は数秒後に「準備完了」へ戻る。</summary>
        void SetStatus(string message, StatusType type, long autoResetMs = 4000)
        {
            if (statusLabel == null) return;

            statusLabel.text = message;
            statusLabel.EnableInClassList("dennoko-status--success", type == StatusType.Success);
            statusLabel.EnableInClassList("dennoko-status--error", type == StatusType.Error);

            statusResetSchedule?.Pause();
            if (type != StatusType.Info)
            {
                statusResetSchedule = statusLabel.schedule
                    .Execute(() => SetStatus(Loc.T("準備完了"), StatusType.Info))
                    .StartingIn(autoResetMs);
            }
        }

        // ---------------------------------------------------------------- バージョン表示

        void StartVersionCheck()
        {
            LoadVersionResultFromSessionState();
            // 取得の要否は StartCheckBackgroundTask 内で判定する（毎回呼んでよい）。
            StitchMakerVersion.StartCheckBackgroundTask();
        }

        internal void LoadVersionResultFromSessionState()
        {
            // State はキャッシュせず、常に「現在のローカル版 vs 取得済みの最新版」で再計算する。
            string local = StitchMakerVersion.Current;
            string latest = SessionState.GetString(StitchMakerVersion.VerCheckLatestKey, string.Empty);
            bool done = SessionState.GetBool(StitchMakerVersion.VerCheckDoneKey, false);
            bool error = SessionState.GetBool(StitchMakerVersion.VerCheckErrorKey, false);

            DennokoVersionChecker.State state;
            if (!done)
                state = DennokoVersionChecker.State.Checking;
            else if (error || string.IsNullOrEmpty(latest))
                state = DennokoVersionChecker.State.Error;
            else if (DennokoVersionChecker.IsUpdateAvailable(latest, local))
                state = DennokoVersionChecker.State.UpdateAvailable;
            else
                state = DennokoVersionChecker.State.UpToDate;

            versionResult = new DennokoVersionChecker.Result
            {
                State = state,
                LocalVersion = local,
                LatestVersion = latest,
                Url = SessionState.GetString(StitchMakerVersion.VerCheckUrlKey, string.Empty),
                Message = SessionState.GetString(StitchMakerVersion.VerCheckMessageKey, string.Empty),
            };
            ApplyVersionLabel();
        }

        void ApplyVersionLabel()
        {
            if (versionLabel == null) return;

            var r = versionResult;
            string text = "v" + r.LocalVersion;
            bool update = false, error = false;
            switch (r.State)
            {
                case DennokoVersionChecker.State.UpdateAvailable:
                    text += "  " + Loc.F("更新あり {0}", r.LatestVersion);
                    update = true;
                    break;
                case DennokoVersionChecker.State.Error:
                    text += "  " + Loc.T("最新版を取得できません");
                    error = true;
                    break;
                case DennokoVersionChecker.State.Checking:
                    text += "  " + Loc.T("確認中...");
                    break;
            }
            versionLabel.text = text;
            versionLabel.tooltip = update ? r.Url : "";
            versionLabel.EnableInClassList("dennoko-version-label--update", update);
            versionLabel.EnableInClassList("dennoko-version-label--error", error);
        }

        // ---------------------------------------------------------------- 再構築

        void OnUndoRedo()
        {
            if (document == null) return;
            ClampSelection();
            needsRebuild = true;
            RefreshUI();
            SceneView.RepaintAll();
        }

        // シーンビューの描画中に RenderTarget を切り替えないよう、ベイクはここで行う。
        void OnEditorUpdate()
        {
            if (!needsRebuild || !previewOn || document == null) return;
            EnsureSnapshot();
            if (snapshot == null) return;
            if (!Bake()) return;
            SceneView.RepaintAll();
        }

        bool Bake()
        {
            try
            {
                if (baker == null) baker = new TextureBaker();
                baker.Render(snapshot, document);
                needsRebuild = false;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                needsRebuild = false;
                previewOn = false;
                RefreshUI();
                SetStatus(Loc.T("プレビューの描画に失敗しました（Console を確認してください）"), StatusType.Error);
                return false;
            }
        }

        void EnsureSnapshot()
        {
            if (target == null)
            {
                if (snapshot != null || snapshotError != null)
                {
                    DisposeSnapshot();
                    RefreshUI();
                }
                return;
            }
            if (snapshot != null && snapshot.SourceMesh != MeshSnapshot.GetSourceMesh(target))
                DisposeSnapshot();
            if (snapshot != null || snapshotError != null) return;

            try
            {
                snapshot = new MeshSnapshot(target, materialIndex);
                needsRebuild = true;
            }
            catch (Exception e)
            {
                snapshotError = e.Message;
            }
            RefreshTargetMessage();
        }

        void DisposeSnapshot()
        {
            snapshot?.Dispose();
            snapshot = null;
            snapshotError = null;
        }

        void RefreshSnapshot()
        {
            DisposeSnapshot();
            EnsureSnapshot();
            needsRebuild = true;
            RefreshUI();
            SceneView.RepaintAll();
        }

        void ClampSelection()
        {
            selectedPath = Mathf.Clamp(selectedPath, -1, document.paths.Count - 1);
            var path = SelectedPath;
            selectedNode = path != null ? Mathf.Clamp(selectedNode, -1, path.nodes.Count - 1) : -1;
        }

        StitchPath SelectedPath
        {
            get
            {
                return document != null && selectedPath >= 0 && selectedPath < document.paths.Count
                    ? document.paths[selectedPath]
                    : null;
            }
        }

        // ---------------------------------------------------------------- シーンビュー

        void OnSceneGUI(SceneView view)
        {
            if (document == null) return;
            EnsureSnapshot();
            Tools.hidden = editMode && snapshot != null;
            if (snapshot == null) return;

            var t = target.transform;
            var meshToWorld = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);

            if (previewOn && baker != null)
                ScenePreview.Draw(snapshot, baker, meshToWorld, previewMode);

            if (!editMode) return;
            int previousPath = selectedPath, previousCount = document.paths.Count;
            bool changed = ScenePathEditor.OnSceneGUI(document, snapshot, meshToWorld, ref selectedPath, ref selectedNode);
            if (changed) MarkChanged();

            // シーン側でパスが自動作成された・選択が変わったときだけ UI を作り直す。
            if (previousPath != selectedPath || previousCount != document.paths.Count)
            {
                var path = SelectedPath;
                if (path != null) presetName = path.name;
                RefreshUI();
            }
        }

        // ---------------------------------------------------------------- プリセット

        // 同梱プリセット（カテゴリ別）と、ユーザーが保存したプリセットをサムネイル付きの一覧で開く。
        void ShowApplyMenu(VisualElement anchor)
        {
            var path = SelectedPath;
            if (path == null) return;

            var sections = new List<PresetPicker.Section>();
            foreach (var builtIn in PresetStore.GetBuiltIn())
            {
                string assetPath = builtIn.assetPath;
                string presetLabel = builtIn.name;
                // 同梱プリセットは表示順に並んでいるので、カテゴリが変わったところで見出しを切る。
                string title = string.IsNullOrEmpty(builtIn.category) ? Loc.T("同梱") : Loc.T(builtIn.category);
                if (sections.Count == 0 || sections[sections.Count - 1].title != title)
                    sections.Add(new PresetPicker.Section { title = title });
                sections[sections.Count - 1].items.Add(PresetPicker.CreateBuiltInItem(builtIn,
                    () => ApplyPreset(path, presetLabel, () => PresetStore.ApplyBuiltIn(assetPath, path))));
            }

            var saved = new PresetPicker.Section
            {
                title = Loc.T("保存済み"),
                emptyText = Loc.T("（保存したプリセットはありません）"),
            };
            foreach (var name in PresetStore.GetNames())
            {
                string captured = name;
                saved.items.Add(PresetPicker.CreateUserItem(captured,
                    () => ApplyPreset(path, captured, () => PresetStore.Apply(captured, path))));
            }
            sections.Add(saved);

            UnityEditor.PopupWindow.Show(anchor.worldBound, new PresetPicker(sections, ApplyTheme));
        }

        void ShowDeleteMenu(VisualElement anchor)
        {
            var menu = new GenericMenu();
            AddUserPresets(menu, DeletePreset);
            menu.DropDown(anchor.worldBound);
        }

        static void AddUserPresets(GenericMenu menu, Action<string> onSelected)
        {
            var names = PresetStore.GetNames();
            if (names.Length == 0)
                menu.AddDisabledItem(new GUIContent(Loc.T("（保存したプリセットはありません）")));
            foreach (var name in names)
            {
                string captured = name;
                menu.AddItem(new GUIContent(captured), false, () => onSelected(captured));
            }
        }

        void ApplyPreset(StitchPath path, string label, Action apply)
        {
            if (document == null || !document.paths.Contains(path)) return;
            Undo.RecordObject(document, "Apply Stitch Preset");
            apply();
            MarkChanged();
            RefreshUI();
            SetStatus(Loc.F("プリセットを適用しました: {0}", label), StatusType.Success);
        }

        void DeletePreset(string name)
        {
            if (!EditorUtility.DisplayDialog("StitchMaker", Loc.F("プリセット「{0}」を削除しますか？", name),
                    Loc.T("削除"), Loc.T("キャンセル")))
                return;
            PresetStore.Delete(name);
            SetStatus(Loc.F("プリセットを削除しました: {0}", name), StatusType.Success);
        }

        void SavePreset()
        {
            var path = SelectedPath;
            if (path == null) return;

            string name = PresetStore.SanitizeName(presetName);
            if (name.Length == 0)
            {
                SetStatus(Loc.T("プリセット名を入力してください。"), StatusType.Error);
                return;
            }
            if (PresetStore.Exists(name) &&
                !EditorUtility.DisplayDialog("StitchMaker", Loc.F("プリセット「{0}」を上書きしますか？", name),
                    Loc.T("上書き"), Loc.T("キャンセル")))
                return;

            PresetStore.Save(name, path.settings, path.mask);
            presetName = name;
            presetNameField.SetValueWithoutNotify(name);
            SetStatus(Loc.F("プリセットを保存しました: {0}", name), StatusType.Success);
        }

        // ---------------------------------------------------------------- 出力

        void PickExportFolder()
        {
            if (document == null) return;
            string start = Directory.Exists(document.export.folder) ? document.export.folder : "Assets";
            string picked = EditorUtility.OpenFolderPanel(Loc.T("出力先フォルダ"), start, "");
            if (string.IsNullOrEmpty(picked)) return;

            string relative = FileUtil.GetProjectRelativePath(picked.Replace('\\', '/'));
            if (string.IsNullOrEmpty(relative) || !relative.StartsWith("Assets"))
            {
                SetStatus(Loc.T("プロジェクトの Assets フォルダ内を指定してください。"), StatusType.Error);
                return;
            }
            EditExport(export => export.folder = relative);
            RefreshUI();
        }

        void ExportTextures()
        {
            if (document == null) return;
            string folder = (document.export.folder ?? "").Replace('\\', '/').TrimEnd('/');
            if (folder != "Assets" && !folder.StartsWith("Assets/"))
            {
                SetStatus(Loc.T("出力先は Assets フォルダ内を指定してください。"), StatusType.Error);
                return;
            }

            EnsureSnapshot();
            if (snapshot == null)
            {
                string reason = target == null
                    ? "ステッチを描く Renderer を指定してください。"
                    : snapshotError ?? "対象メッシュを取得できません。";
                SetStatus(Loc.T(reason), StatusType.Error);
                return;
            }

            try
            {
                if (baker == null) baker = new TextureBaker();
                baker.Render(snapshot, document);
                needsRebuild = false;
                baker.Export(document.export, SanitizeFileName(target.name));
                AssetDatabase.Refresh();
                SetStatus(Loc.F("テクスチャを出力しました: {0}", folder), StatusType.Success);
                var folderAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(folder);
                if (folderAsset != null) EditorGUIUtility.PingObject(folderAsset);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                SetStatus(Loc.F("出力に失敗しました: {0}", e.Message), StatusType.Error, 8000);
            }
        }

        static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            string result = new string(name.Where(c => Array.IndexOf(invalid, c) < 0).ToArray()).Trim();
            return result.Length > 0 ? result : "Mesh";
        }

        // ---------------------------------------------------------------- ドキュメントの新規・読込・保存

        StitchDocument CreateDocument()
        {
            var created = CreateInstance<StitchDocument>();
            created.name = "StitchDocument";
            created.hideFlags = HideFlags.DontSave;
            created.export.folder = GetDefaultExportFolder();
            return created;
        }

        string GetDefaultExportFolder()
        {
            if (target != null)
            {
                var materials = target.sharedMaterials;
                if (materialIndex >= 0 && materialIndex < materials.Length)
                {
                    var mat = materials[materialIndex];
                    if (mat != null)
                    {
                        Texture mainTex = mat.mainTexture;
                        if (mainTex == null)
                        {
                            if (mat.HasProperty("_MainTex")) mainTex = mat.GetTexture("_MainTex");
                            else if (mat.HasProperty("_BaseMap")) mainTex = mat.GetTexture("_BaseMap");
                        }
                        if (mainTex != null)
                        {
                            string texPath = AssetDatabase.GetAssetPath(mainTex);
                            if (!string.IsNullOrEmpty(texPath) && texPath.StartsWith("Assets"))
                            {
                                string dir = Path.GetDirectoryName(texPath).Replace('\\', '/');
                                return dir + "/StitchMakerTexture";
                            }
                        }
                    }
                }
            }
            return ExportSettings.FallbackFolder;
        }

        void UpdateExportFolder()
        {
            if (document == null) return;
            string defaultFolder = GetDefaultExportFolder();
            EditExport(export => export.folder = defaultFolder);
            exportFolderField?.SetValueWithoutNotify(defaultFolder);
        }

        bool ConfirmDiscard()
        {
            return !dirty || EditorUtility.DisplayDialog("StitchMaker",
                Loc.T("保存していない変更があります。破棄してよろしいですか？"), Loc.T("破棄"), Loc.T("キャンセル"));
        }

        void ReplaceDocument(StitchDocument replacement)
        {
            if (document != null)
            {
                Undo.ClearUndo(document);
                DestroyImmediate(document);
            }
            document = replacement;
            SelectPath(document.paths.Count > 0 ? 0 : -1);
            dirty = false;
            needsRebuild = true;
            RefreshUI();
            SceneView.RepaintAll();
        }

        void NewDocument()
        {
            if (!ConfirmDiscard()) return;
            ReplaceDocument(CreateDocument());
            SetStatus(Loc.T("新しいドキュメントを作成しました"), StatusType.Success);
        }

        void LoadDocument()
        {
            if (!ConfirmDiscard()) return;
            string file = EditorUtility.OpenFilePanel(Loc.T("パスを読み込む"), "Assets", "asset");
            if (string.IsNullOrEmpty(file)) return;

            string assetPath = FileUtil.GetProjectRelativePath(file.Replace('\\', '/'));
            var asset = string.IsNullOrEmpty(assetPath) ? null : AssetDatabase.LoadAssetAtPath<StitchDocument>(assetPath);
            if (asset == null)
            {
                SetStatus(Loc.T("StitchMaker のパスアセットではありません。"), StatusType.Error);
                return;
            }

            // アセット自体は編集せず、コピーを編集対象にする。
            var copy = Instantiate(asset);
            copy.name = asset.name;
            copy.hideFlags = HideFlags.DontSave;
            ReplaceDocument(copy);
            SetStatus(Loc.F("パスを読み込みました: {0}", assetPath), StatusType.Success);
        }

        void SaveDocument()
        {
            if (document == null) return;
            string assetPath = EditorUtility.SaveFilePanelInProject(
                Loc.T("パスを保存"), "StitchPaths", "asset", Loc.T("保存先を指定してください。"));
            if (string.IsNullOrEmpty(assetPath)) return;

            var copy = Instantiate(document);
            copy.name = Path.GetFileNameWithoutExtension(assetPath);
            copy.hideFlags = HideFlags.None;

            var existing = AssetDatabase.LoadAssetAtPath<StitchDocument>(assetPath);
            if (existing != null)
            {
                // GUID を維持したまま上書きする。
                EditorUtility.CopySerialized(copy, existing);
                EditorUtility.SetDirty(existing);
                DestroyImmediate(copy);
            }
            else
            {
                AssetDatabase.CreateAsset(copy, assetPath);
            }
            AssetDatabase.SaveAssets();
            dirty = false;
            SetStatus(Loc.F("パスを保存しました: {0}", assetPath), StatusType.Success);
        }
    }
}
