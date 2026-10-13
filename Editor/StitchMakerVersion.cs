using UnityEditor;
using UnityEngine;
using System;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;

namespace Dennokoworks.StitchMaker
{
    // インポート時（コンパイル完了時）や起動時に自動的にアップデートチェックを実行する。
    //
    // ただし実際にリクエストを飛ばすのは前回から CheckIntervalHours 以上経過したときだけで、
    // その間は前回結果（EditorPrefs キャッシュ）を表示に使う。ドメインリロードのたびに
    // 取得しに行くと GitHub のレート制限に掛かるため。
    [InitializeOnLoad]
    internal static class StitchMakerVersion
    {
        // version.json の GUID (アセット移動に対応するため GUID 経由でパス解決する)
        private const string VersionJsonGuid = "3d8ed064a35d498ba530c93f79995159";
        // version.json をどうしても読めなかった場合の最終フォールバック（通常は使われない）
        private const string FallbackVersion = "0.0.0";
        private static string _currentCache = null;

        internal static string Current
        {
            get
            {
                // 失敗（null）はキャッシュしない。インポート直後で version.json が
                // まだ読めなかった場合でも、次回アクセス時に再試行できるようにする。
                if (string.IsNullOrEmpty(_currentCache))
                {
                    _currentCache = LoadLocalVersion();
                }
                return string.IsNullOrEmpty(_currentCache) ? FallbackVersion : _currentCache;
            }
        }

        // チェック先（設定されているリモートリポジトリに合わせる）
        internal const string RepoOwner       = "dennoko";
        internal const string RepoName        = "StitchMaker";
        internal const string RepoBranch      = "main";
        internal const string VersionFilePath = "version.json";

        // セッションキー。State（比較結果）は保存しない — ローカル版が後から正しく解決され得るため、
        // 表示のたびに「保存した最新版 vs 現在のローカル版」で更新有無を再計算する。
        // ここでは取得が成功したか（Error だったか）だけ保存する。
        internal const string VerCheckDoneKey   = "StitchMaker_VerCheck_Done";
        internal const string VerCheckErrorKey  = "StitchMaker_VerCheck_Error";
        internal const string VerCheckLatestKey = "StitchMaker_VerCheck_Latest";
        internal const string VerCheckUrlKey    = "StitchMaker_VerCheck_Url";
        internal const string VerCheckMessageKey = "StitchMaker_VerCheck_Message";

        // 以下はエディタ再起動をまたいで保持する必要があるため EditorPrefs に置く。
        // SessionState だと再起動のたびにリセットされ、レート制限中でも撃ち続けてしまう。
        internal const string VerCheckLastAttemptKey  = "StitchMaker_VerCheck_LastAttemptUtc";
        internal const string VerCheckCachedLatestKey = "StitchMaker_VerCheck_CachedLatest";
        internal const string VerCheckCachedUrlKey    = "StitchMaker_VerCheck_CachedUrl";
        internal const string VerCheckCachedMessageKey = "StitchMaker_VerCheck_CachedMessage";

        // 前回リクエストからこの時間が経つまで自動チェックを行わない。ドメインリロード
        // （スクリプト保存・Play mode 出入りごとに走る）で無制限に再試行すると、GitHub 側の
        // レート制限を自分で悪化させ「制限 → エラー → 即再試行」のループに入るため。
        private const double CheckIntervalHours = 6.0;

        static StitchMakerVersion()
        {
            // 静的コンストラクタはドメインリロード中に走り、この時点では version.json が
            // AssetDatabase 未登録のことがある。delayCall で 1 tick 遅らせてから開始する。
            EditorApplication.delayCall += StartCheckBackgroundTask;
        }

        // 同一ドメイン内での二重リクエスト防止（ドメインリロードで false に戻る）
        private static bool _checking;

        internal static void StartCheckBackgroundTask()
        {
            // 成功済みなら再取得しない。だがエラー時は「インポート直後の一時的な失敗
            // （パッケージ取り込み時のドメインリロードでリクエストが中断される等）」を想定し、
            // 次のトリガー（ウィンドウを開く / ドメインリロード）で再試行する。
            bool done  = SessionState.GetBool(VerCheckDoneKey, false);
            bool error = SessionState.GetBool(VerCheckErrorKey, false);
            if (done && !error) return;
            if (_checking) return;

            // 間隔内はリクエストを送らず、前回取得できた結果をそのまま表示に使う。
            // ここで done を立てないと「確認中...」のまま固まってしまう。
            if (IsInCheckInterval())
            {
                ApplyCachedResult();
                return;
            }

            _checking = true;
            EditorPrefs.SetString(VerCheckLastAttemptKey, DateTime.UtcNow.ToString("o"));

            DennokoVersionChecker.CheckAsync(
                RepoOwner, RepoName, RepoBranch, VersionFilePath, Current, OnVersionChecked);
        }

        /// <summary>前回リクエストから CheckIntervalHours 経っていなければ true。</summary>
        private static bool IsInCheckInterval()
        {
            var last = EditorPrefs.GetString(VerCheckLastAttemptKey, string.Empty);
            if (string.IsNullOrEmpty(last)) return false;
            if (!DateTime.TryParse(last, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var lastUtc)) return false;

            // 端末時刻が巻き戻された場合に永久に待ち続けないよう、未来の記録は無効扱いにする
            var elapsed = DateTime.UtcNow - lastUtc.ToUniversalTime();
            if (elapsed < TimeSpan.Zero) return false;

            return elapsed.TotalHours < CheckIntervalHours;
        }

        /// <summary>EditorPrefs に残っている前回の取得結果をセッションへ反映する。</summary>
        private static void ApplyCachedResult()
        {
            var latest = EditorPrefs.GetString(VerCheckCachedLatestKey, string.Empty);
            SessionState.SetBool(VerCheckDoneKey, true);
            // 前回も取得できていなければエラー表示のまま（次の間隔明けに再試行される）
            SessionState.SetBool(VerCheckErrorKey, string.IsNullOrEmpty(latest));
            SessionState.SetString(VerCheckLatestKey, latest);
            SessionState.SetString(VerCheckUrlKey, EditorPrefs.GetString(VerCheckCachedUrlKey, string.Empty));
            SessionState.SetString(VerCheckMessageKey, EditorPrefs.GetString(VerCheckCachedMessageKey, string.Empty));

            RefreshOpenWindows();
        }

        /// <summary>手動での再取得。前回結果（成功/失敗・ローカル版キャッシュ）を破棄して再チェックする。</summary>
        internal static void ForceRecheck()
        {
            if (_checking) return; // 進行中なら何もしない
            _currentCache = null;  // ローカル版も読み直す（version.json を直したケースに対応）
            SessionState.SetBool(VerCheckDoneKey, false);
            SessionState.SetBool(VerCheckErrorKey, false);
            // 明示的なユーザー操作なので間隔は無視する（抑制対象は自動チェックのみ）
            EditorPrefs.DeleteKey(VerCheckLastAttemptKey);
            StartCheckBackgroundTask();
        }

        private static void OnVersionChecked(DennokoVersionChecker.Result result)
        {
            _checking = false;
            bool failed = result.State == DennokoVersionChecker.State.Error;

            SessionState.SetBool(VerCheckDoneKey, true);
            SessionState.SetBool(VerCheckErrorKey, failed);
            SessionState.SetString(VerCheckLatestKey, result.LatestVersion ?? string.Empty);
            SessionState.SetString(VerCheckUrlKey, result.Url ?? string.Empty);
            SessionState.SetString(VerCheckMessageKey, result.Message ?? string.Empty);

            // 成功時のみ永続キャッシュを更新する。失敗で上書きすると、間隔内の表示から
            // 「前回取得できていた最新版」が消えてしまうため。
            if (!failed)
            {
                EditorPrefs.SetString(VerCheckCachedLatestKey, result.LatestVersion ?? string.Empty);
                EditorPrefs.SetString(VerCheckCachedUrlKey, result.Url ?? string.Empty);
                EditorPrefs.SetString(VerCheckCachedMessageKey, result.Message ?? string.Empty);
            }

            RefreshOpenWindows();
        }

        /// <summary>すでに開かれているウィンドウに取得結果を反映させる。</summary>
        private static void RefreshOpenWindows()
        {
            var windows = Resources.FindObjectsOfTypeAll<StitchMakerWindow>();
            if (windows == null) return;
            foreach (var w in windows)
            {
                if (w != null) w.LoadVersionResultFromSessionState();
            }
        }

        [Serializable]
        private class VersionInfo
        {
            public string version;
        }

        /// <summary>ローカルの version.json を読む。読めなければ null（呼び出し側で
        /// フォールバックし、次回アクセス時に再試行する）。</summary>
        private static string LoadLocalVersion()
        {
            // 1) GUID 経由（アセット移動に追従。ただし AssetDatabase 準備前は空を返し得る）
            var v = TryReadVersion(AssetDatabase.GUIDToAssetPath(VersionJsonGuid));
            if (v != null) return v;

            // 2) スクリプト位置からの相対探索（AssetDatabase 未準備でも解決できる保険）
            return TryReadVersion(ResolveVersionJsonByScriptPath());
        }

        private static string TryReadVersion(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                var info = JsonUtility.FromJson<VersionInfo>(File.ReadAllText(path));
                if (info != null && !string.IsNullOrEmpty(info.version)) return info.version;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[StitchMakerVersion] Failed to read version.json ({path}): {e.Message}");
            }
            return null;
        }

        /// <summary>
        /// このスクリプトの位置を起点に上位フォルダを辿って version.json を探す。
        /// [CallerFilePath] はコンパイル時パスなので、他プロジェクトへインポートして
        /// 再コンパイルされれば、そのプロジェクト内の正しいパスに解決される
        /// （AssetDatabase のインポート完了状況に依存しない）。
        /// </summary>
        private static string ResolveVersionJsonByScriptPath([CallerFilePath] string scriptPath = null)
        {
            if (string.IsNullOrEmpty(scriptPath)) return null;
            var dir = Path.GetDirectoryName(scriptPath);
            for (int i = 0; i < 5 && !string.IsNullOrEmpty(dir); i++)
            {
                var candidate = Path.Combine(dir, "version.json");
                if (File.Exists(candidate)) return candidate;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }
    }
}
