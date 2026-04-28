using System.Globalization;

namespace LoopcastUA.Infrastructure
{
    internal static class Strings
    {
        public enum Lang { Auto, En, Ja }

        private static Lang _lang = Lang.Auto;

        public static void SetLang(Lang lang) => _lang = lang;

        public static Lang ParseCode(string code)
        {
            switch (code?.ToLowerInvariant())
            {
                case "en": return Lang.En;
                case "ja": return Lang.Ja;
                default:   return Lang.Auto;
            }
        }

        private static bool Ja =>
            _lang == Lang.Ja ||
            (_lang == Lang.Auto && CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja");

        // ── Tray status ──────────────────────────────────────────────────────
        public static string StatusConnecting   => Ja ? "接続中..."       : "Connecting...";
        public static string StatusIdle         => Ja ? "接続済み（無音）" : "Connected (silent)";
        public static string StatusPlaying      => Ja ? "再生中"           : "Playing";
        public static string StatusReconnecting => Ja ? "再接続中..."      : "Reconnecting...";

        public static string TipConnecting   => Ja ? "LoopcastUA — 接続中..."   : "LoopcastUA — Connecting...";
        public static string TipIdle         => Ja ? "LoopcastUA — 接続済み"    : "LoopcastUA — Connected";
        public static string TipPlaying      => Ja ? "LoopcastUA — 再生中"      : "LoopcastUA — Playing";
        public static string TipReconnecting => Ja ? "LoopcastUA — 再接続中..." : "LoopcastUA — Reconnecting...";

        // ── Tray menu ────────────────────────────────────────────────────────
        public static string MenuSettings     => Ja ? "設定..."                 : "Settings...";
        public static string MenuOpenLog      => Ja ? "ログフォルダを開く"      : "Open log folder";
        public static string MenuReloadConfig => Ja ? "設定ファイルを再読み込み" : "Reload config";
        public static string MenuExit         => Ja ? "終了"                    : "Exit";

        // ── Message boxes ────────────────────────────────────────────────────
        public static string AppTitle => "LoopcastUA";

        public static string FirstRunTitle => Ja ? "LoopcastUA — 初回起動" : "LoopcastUA — First Run";
        public static string FirstRunBody  => Ja
            ? "SIP接続が設定されていません。\n設定画面を開いて初期設定を行ってください。"
            : "SIP connection is not configured.\nPlease open Settings to get started.";

        public static string AlreadyRunning => Ja
            ? "LoopcastUA はすでに起動しています。"
            : "LoopcastUA is already running.";

        public static string LogFolderNotFound => Ja ? "ログフォルダが見つかりません:\n" : "Log folder not found:\n";

        public static string ConfigReloaded => Ja
            ? "設定ファイルを再読み込みしました。SIP・オーディオ設定の変更は再起動後に反映されます。"
            : "Config reloaded. SIP/audio changes take effect after restart.";

        public static string UnexpectedError => Ja
            ? "予期しないエラーが発生しました:\n\n"
            : "An unexpected error occurred:\n\n";

        // ── Settings form ────────────────────────────────────────────────────
        public static string SettingsTitle => Ja ? "LoopcastUA — 設定" : "LoopcastUA — Settings";

        public static string TabGeneral   => Ja ? "全般"       : "General";
        public static string TabSip       => "SIP";
        public static string TabAudio     => Ja ? "オーディオ" : "Audio";
        public static string TabDetection => Ja ? "無音検出"   : "Detection";
        public static string TabBatch     => Ja ? "バッチ"     : "Batch";

        public static string LabelServer      => Ja ? "サーバー"              : "Server";
        public static string LabelPort        => Ja ? "ポート"                : "Port";
        public static string LabelExtension   => Ja ? "内線番号（ユーザー名）" : "Extension (username)";
        public static string LabelPassword    => Ja ? "パスワード"            : "Password";
        public static string LabelRoom        => Ja ? "会議室番号"            : "Conference room";
        public static string LabelDisplayName => Ja ? "表示名"               : "Display name";

        public static string LabelCaptureMode  => Ja ? "キャプチャモード"       : "Capture mode";
        public static string CaptureModeDirect   => Ja
            ? "ダイレクトキャプチャ（音量設定の影響なし）"
            : "Direct capture (volume-independent)";
        public static string CaptureModeRendered => Ja
            ? "レンダードキャプチャ（音量設定に依存）"
            : "Rendered capture (volume-dependent)";

        public static string CaptureDirectUnsupported(int build) => Ja
            ? $"Windows 10 20H2 以降が必要です（現在のビルド: {build}）"
            : $"Requires Windows 10 20H2 or later (current build: {build})";

        public static string LabelCaptureDevice => Ja ? "キャプチャデバイス"      : "Capture device";
        public static string LabelOpusBitrate   => Ja ? "Opus ビットレート (bps)" : "Opus bitrate (bps)";

        public static string LabelThreshold    => Ja ? "閾値 (dBFS)"            : "Threshold (dBFS)";
        public static string LabelEnterSilence => Ja ? "無音判定ガード時間 (ms)" : "Enter silence guard (ms)";
        public static string LabelExitSilence  => Ja ? "再生判定ガード時間 (ms)" : "Exit silence guard (ms)";

        public static string LabelOnStart => Ja ? "再生開始時"            : "On playback start";
        public static string LabelOnStop  => Ja ? "再生停止時"            : "On playback stop";
        public static string LabelTimeout => Ja ? "実行タイムアウト (ms)" : "Execution timeout (ms)";

        public static string LabelLanguage  => Ja ? "言語 (Language)"    : "Language";
        public static string DefaultDevice  => Ja ? "既定の再生デバイス" : "Default render device";

        public static string BtnOk     => "OK";
        public static string BtnApply  => Ja ? "適用"       : "Apply";
        public static string BtnCancel => Ja ? "キャンセル" : "Cancel";

        public static string ValidationTitle => Ja ? "入力エラー"                : "Validation Error";
        public static string ValidationBody  => Ja ? "以下の項目を確認してください:" : "Please fix the following:";

        public static string LangAuto => Ja ? "自動（システムに合わせる）" : "Auto (follow system)";
        public static string LangEn   => "English";
        public static string LangJa   => "日本語";

        // ── Validation messages ──────────────────────────────────────────────
        public static string ValNoSip        => Ja ? "SIP 設定がありません"                                          : "SIP configuration is missing";
        public static string ValSipServer    => Ja ? "SIP: サーバーアドレスを入力してください"                        : "SIP: Server address is required";
        public static string ValSipPort      => Ja ? "SIP: ポートは 1〜65535 の範囲で入力してください"                : "SIP: Port must be between 1 and 65535";
        public static string ValSipUser      => Ja ? "SIP: Extension (ユーザー名) を入力してください"                 : "SIP: Extension (username) is required";
        public static string ValSipPassword  => Ja ? "SIP: パスワードを入力してください"                              : "SIP: Password is required";
        public static string ValSipRoom      => Ja ? "SIP: 会議室番号を入力してください"                              : "SIP: Conference room is required";
        public static string ValAudioBitrate => Ja ? "Audio: Opus ビットレートは 8000〜128000 の範囲で入力してください" : "Audio: Opus bitrate must be between 8000 and 128000";
        public static string ValDetThreshold => Ja ? "Detection: 閾値は -90〜0 dBFS の範囲で入力してください"          : "Detection: Threshold must be between -90 and 0 dBFS";
        public static string ValDetEnter     => Ja ? "Detection: 無音判定ガード時間は 0 以上で入力してください"         : "Detection: Enter silence guard must be 0 or greater";
        public static string ValDetExit      => Ja ? "Detection: 再生判定ガード時間は 0 以上で入力してください"         : "Detection: Exit silence guard must be 0 or greater";
        public static string ValBatchTimeout => Ja ? "Batch: 実行タイムアウトは 0 以上で入力してください"              : "Batch: Execution timeout must be 0 or greater";

        public static string ValBatchStart(string path) => Ja
            ? $"Batch: 再生開始バッチが見つかりません: {path}"
            : $"Batch: Playback start script not found: {path}";
        public static string ValBatchStop(string path) => Ja
            ? $"Batch: 再生停止バッチが見つかりません: {path}"
            : $"Batch: Playback stop script not found: {path}";
    }
}
