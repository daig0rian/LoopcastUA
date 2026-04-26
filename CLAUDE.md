# PC Audio SIP Bridge

Windows PC で再生されている音声を SIP 経由で FreePBX の会議ブリッジに常時送信し、拠点間で共通の音声配信を実現するシステム。タスクトレイ常駐型の Windows クライアントと、FreePBX + Asterisk ベースのサーバから成る。

---

## 1. プロジェクト目的と利用シナリオ

- 20〜40 台の Windows PC から、それぞれの PC で再生される音声 (BGM、通知音、アナウンスなど) を 1 つの会議ブリッジにミキシングして配信
- 各拠点の参加者は音を聞くだけで、発話はしない (片方向音声配信)
- 音量バランス調整や mute などのミキサー機能はサーバ側管理 UI で集中制御
- クライアントは各 PC に常駐させ、起動と同時に自動接続
- 音の再生状態変化 (無音 → 再生、再生 → 無音) をトリガーに、指定のバッチファイルを実行
- 業務 PC への展開は MSI パッケージ経由、将来的に GPO 一括配布も想定

---

## 2. 確定した技術選定

### サーバ側

| 項目 | 選定 | 理由 |
|---|---|---|
| ディストリビューション | **FreePBX Distro (Rocky Linux ベース)** | 一括インストール、Web UI 標準、日本語情報豊富 |
| SIP エンジン | **Asterisk (FreePBX 同梱)** | FreePBX という成熟した Web 管理 UI が使える |
| SIP ドライバ | **chan_pjsip** | Opus 対応、現行標準 |
| 会議ブリッジ | **ConfBridge** | Applications → Conferences で管理 |
| 管理 UI | **FreePBX Web UI + UCP** | 参加者一覧、音量調整、mute、kick が GUI で可能 |
| デプロイ | **VM (vCPU 4, RAM 4GB, 100Mbps NIC)** | 40 台規模に十分 |

### クライアント側 (Windows 常駐アプリ)

| 項目 | 選定 | 理由 |
|---|---|---|
| 言語 / ランタイム | **C# / .NET Framework 4.7.2** | Win7 SP1 対応 (4.7.2 は Win7 SP1 で動作可)、NAudio 2.x の最低要件 |
| SIP スタック | **SIPSorcery** | Pure managed、ネイティブ依存なし |
| 音声キャプチャ | **NAudio (WASAPI ループバック)** | 標準 API、Vista 以降対応 |
| コーデック | **Concentus (Opus pure C#)** | ネイティブ DLL 不要、managed only |
| リサンプラ | **NAudio WdlResamplingSampleProvider** | pure managed、Media Foundation 非依存 |
| UI フレームワーク | **WinForms (.NET Framework)** | 標準、軽量、タスクトレイ対応 |
| UI 実装方針 | **Code-first (デザイナー不使用)** | VS Code のみで開発可、差分が読みやすい |
| **配布形態** | **MSI インストーラー (WiX Toolset)** | GPO 配布対応、40 台運用に適合、一括更新・アンインストール容易 |
| **DLL 配置** | **外出し (インストールディレクトリに配置)** | メモリ効率、ウィルス誤検知回避、アップデート差分配布可 |
| 常駐方式 | **常駐アプリ (Windows サービスではない)** | WASAPI ループバックはユーザーセッション必須 |
| パスワード保護 | **DPAPI (Windows 標準暗号化)** | 同一ユーザーのみ復号可、追加ライブラリ不要 |

### 通信

| 項目 | 選定 | 理由 |
|---|---|---|
| コーデック | **Opus 48kbps / 48kHz / モノラル** | 音質良、帯域軽量、FreePBX 推奨 |
| フレーム長 | **20ms** | SIP 標準、pps 50 |
| トランスポート | **UDP (平文 RTP、平文 SIP)** | 社内 LAN 前提、TLS/SRTP 不要 |
| **REGISTER** | **不要 (発信専用 UA)** | 着信なしなので Extension は Digest 認証のみ使用 |
| 接続方式 | 起動時に会議室番号へ INVITE、切断時は指数バックオフで再接続 | |
| NAT | 社内 LAN 同一セグメント前提、PBX の Local Networks 設定のみ | |

---

## 3. 主要な数値パラメータ

### 帯域計算 (40 台同時接続)

- 1 セッション実効: 約 64kbps (Opus 48kbps + RTP/UDP/IP ヘッダ)
- 40 台合計 (上り + 下り): 約 5.1 Mbps
- 100Mbps NIC に対して約 5%、余裕あり

### 想定リソース (クライアント側)

- 定常メモリ: 15〜25 MB (CLR 含む、DLL 外出しで Costura 時より軽量)
- CPU 使用率: 0.5〜2% (Concentus Opus エンコード含む)
- インストール後のディスク使用量: 約 5〜10 MB (exe + DLL 数個)

### SIP / Extension 発番ルール (推奨)

- 常駐クライアント用 Extension: 9001〜9040 (40 個)
- 会議室番号: 8000
- Extension のパスワードはランダム生成 (Bulk Handler CSV 経由で一括登録)

### 無音検出 (ヒステリシス付き、設定画面から変更可)

- 閾値 (既定): -50 dBFS (RMS)
- 無音判定ガード時間 (既定): -50dBFS 未満が 1500ms 継続
- 再生判定ガード時間 (既定): -50dBFS 以上が 300ms 継続
- 判定ウィンドウ: 20ms ごと

---

## 4. FreePBX / Asterisk 側の設定ポイント

### Opus 有効化

- Settings → Asterisk SIP Settings → Codecs で Opus を最優先に配置
- `codec_opus.so` は FreePBX 17 / Asterisk 20 で標準同梱

### ConfBridge プロファイル設定 (会議室 8000)

**Bridge Profile:**
- Internal Sample Rate: **48000** (Opus ネイティブ、ダウンサンプル回避)
- Mixing Interval: 20ms
- Video Mode: None

**User Profile (参加者の既定動作):**
- Announce User Count: No
- Announce Join/Leave: No
- Quiet Mode: Yes
- Music on Hold When Empty: No

**Conference Profile:**
- Max Participants: 50 (将来余裕)
- Record Conference: No (必要に応じて後から有効化)

### Extension 一括作成

- FreePBX Admin → Bulk Handler で CSV インポート
- 列: `extension,name,secret,...`
- 40 行分の CSV を生成するスクリプトをプロジェクトで用意予定 (`tools/generate_extensions.py` など)

### NAT 設定 (社内 LAN)

- Settings → Asterisk SIP Settings → Local Networks に社内セグメント追加 (例: `192.168.0.0/16`, `10.0.0.0/8`)
- External IP は空欄
- これで PBX は local_net からの接続を NAT なし扱い

### ネットワーク / QoS (任意)

- VM NIC は VirtIO (KVM) / VMXNET3 (VMware) を使用、pps 性能向上
- Asterisk SIP Settings → Advanced → ToS for Audio = `ef` (DSCP 46) で優先制御用マーキング (既存ネットワーク側が QoS 対応している場合のみ有効)

---

## 5. クライアント側の設計

### プロジェクト構成

```
pc-audio-sip-bridge/
├── CLAUDE.md                       # このファイル
├── README.md
├── client/
│   ├── PcAudioSipBridge.sln
│   ├── PcAudioSipBridge/
│   │   ├── PcAudioSipBridge.csproj
│   │   ├── app.config               # GC モード設定など
│   │   ├── packages.config          # NuGet 参照
│   │   ├── config.sample.json       # 設定ファイル雛形
│   │   ├── Resources/
│   │   │   ├── TrayIconIdle.ico
│   │   │   ├── TrayIconActive.ico
│   │   │   └── TrayIconError.ico
│   │   └── src/
│   │       ├── Program.cs           # エントリポイント、ApplicationContext 起動
│   │       ├── TrayContext.cs       # NotifyIcon、ContextMenuStrip、ライフサイクル管理
│   │       ├── Forms/
│   │       │   └── SettingsForm.cs  # 設定画面 (Code-first)
│   │       ├── Config/
│   │       │   ├── AppConfig.cs     # POCO (設定値のコンテナ)
│   │       │   ├── ConfigStore.cs   # 読み書き、ConfigChanged イベント
│   │       │   ├── ConfigValidator.cs
│   │       │   └── DpapiProtector.cs # パスワード暗号化
│   │       ├── Audio/
│   │       │   ├── LoopbackCapturer.cs # WASAPI ループバック
│   │       │   ├── AudioMixer.cs    # ステレオ → モノラル
│   │       │   ├── OpusEncoder.cs   # Concentus ラッパー
│   │       │   └── SilenceDetector.cs
│   │       ├── Sip/
│   │       │   ├── SipClient.cs     # SIPSorcery 制御、再接続
│   │       │   └── RtpSender.cs     # 受信 RTP は破棄
│   │       ├── Batch/
│   │       │   └── BatchRunner.cs   # Process.Start、タイムアウト付き
│   │       └── Infrastructure/
│   │           ├── BufferPool.cs    # byte[] プール、GC 抑制
│   │           └── Logger.cs        # Trace ベースのロガー
│   └── PcAudioSipBridge.Tests/      # (任意) ユニットテスト
├── installer/
│   ├── PcAudioSipBridge.Installer.wixproj
│   ├── Product.wxs                  # WiX メイン定義
│   ├── UI/                          # WiX UI カスタマイズ
│   └── Resources/                   # LICENSE.rtf, BannerImage など
├── tools/
│   └── generate_extensions.py       # CSV 生成スクリプト
└── docs/
    ├── freepbx-runbook.md
    └── installation-guide.md
```

### ランタイム動作フロー

```
起動
 ↓
config.json 読み込み (DPAPI で password 復号)
 ↓
起動ジッタ (0〜5 秒ランダム待機、40 台一斉起動時の INVITE 集中回避)
 ↓
WASAPI ループバックキャプチャ開始 (待機状態)
 ↓
FreePBX ConfBridge へ INVITE (To: sip:8000@pbx, From: sip:9001@pbx)
  - 401 Digest Auth 応答 → 認証
  - 200 OK → SDP ネゴ (Opus 48kHz mono)
 ↓
ACK → RTP 送信開始
 ↓
常駐ループ (20ms ごと):
  - ループバック取得 (48kHz stereo)
  - モノラルミックス (L+R)/2
  - Opus エンコード (48kbps, frame 20ms)
  - RTP 送信
  - RMS 計算 → ヒステリシス判定 → 状態遷移時にバッチ実行
  - 受信 RTP は破棄
 ↓
切断検知 → 指数バックオフで再接続 (5s → 10s → 20s → ... 上限 60s)
 ↓
終了時: BYE 送信 → キャプチャ停止 → クリーンアップ
```

### メモリ / CPU 最適化ポイント

1. **GC モード**: app.config で Workstation GC + Concurrent GC
2. **バッファプール**: 20ms フレームの byte[] を毎回 new しない
3. **受信 RTP 即破棄**: ジッタバッファ・デコーダを立てない
4. **起動直後に `Environment.SetProcessWorkingSetSize`** で WS トリム
5. **ログは Trace + 自前 RollingFileWriter**、NLog 等の重いライブラリ不使用
6. **LINQ をホットパスで使わない** (隠れアロケーション回避)

### 再接続戦略

- 初期遅延 5000ms
- バックオフ倍率 2.0
- 上限 60000ms
- 再接続中もタスクトレイアイコンで状態表示 (緑: 接続中、黄: 再接続中、赤: エラー)

### タスクトレイ UI

- 3 状態アイコン (接続中/再接続中/エラー)
- 右クリックメニュー:
  - Status (現在の接続状態、会議室、送信レート)
  - Settings... (設定画面を開く)
  - Open log folder
  - Reload config (ファイル直接編集した時用)
  - Exit

### 設定画面 (Settings)

- **WinForms フォーム 1 画面、Code-first (デザイナー未使用)**
- TableLayoutPanel で項目を機械的に並べる。レイアウトはコードで動的生成
- タブ分類 (任意): SIP / Audio / Detection / Batch
- OK / Apply / Cancel ボタン

**編集可能項目:**

| カテゴリ | 項目 | コントロール |
|---|---|---|
| SIP | サーバ | TextBox |
| SIP | ポート | NumericUpDown (1-65535) |
| SIP | Extension (ユーザー名) | TextBox |
| SIP | パスワード | TextBox (PasswordChar='*') |
| SIP | 会議室番号 | TextBox |
| SIP | 表示名 | TextBox |
| Audio | キャプチャデバイス | ComboBox (WASAPI loopback 可能なデバイス列挙) |
| Audio | Opus ビットレート | NumericUpDown (8000-128000) |
| Detection | 閾値 (dBFS) | NumericUpDown (decimal, -90 ~ 0) |
| Detection | 無音判定ガード時間 (ms) | NumericUpDown |
| Detection | 再生判定ガード時間 (ms) | NumericUpDown |
| Batch | 再生開始時バッチパス | TextBox + Browse ボタン |
| Batch | 再生停止時バッチパス | TextBox + Browse ボタン |
| Batch | 実行タイムアウト (ms) | NumericUpDown |

**バリデーション:**

- Save 前に `ConfigValidator` でまとめて検証
- 失敗時はエラーメッセージをダイアログ表示、該当フィールドをハイライト

**設定変更の適用ルール:**

| 設定カテゴリ | 適用方法 |
|---|---|
| SIP 接続情報 | 要再接続 (Save 時に確認ダイアログ → BYE → 再 INVITE) |
| キャプチャデバイス | 要再接続 (キャプチャストリーム再初期化) |
| Opus ビットレート | 要再接続 (SDP 再ネゴ必要) |
| 無音検出パラメータ | ホットリロード (次フレームから反映) |
| バッチパス・タイムアウト | ホットリロード |

### ConfigStore の責務

- `config.json` の読み書き
- DPAPI による password の暗号化/復号
- バリデーション (ポート範囲、ファイル存在、数値範囲)
- `ConfigChanged` イベント発火 (各モジュールが購読)
- 設定画面を開く時に再読み込み (外部からの手動編集検出)

### SIP パスワードの保護 (DPAPI)

- `DpapiProtector.Protect(plaintext)` / `Unprotect(ciphertext)` でラップ
- `ProtectionScope.CurrentUser` 使用 (同一 Windows ユーザーのみ復号可)
- `config.json` には base64 エンコードした暗号文を保存 (`"DPAPI:..."` プレフィックスで平文と識別)
- 平文モードフラグ `"passwordPlaintext": true` を設定すれば平文運用も可 (デバッグ用)

---

## 6. 設定ファイル: config.json スキーマ

`config.json` の配置場所は未決 (Section 11 TODO) だが、当面は `%ProgramData%\PcAudioSipBridge\config.json` を想定 (全ユーザー共通、MSI でインストール時配置)。

```json
{
  "sip": {
    "server": "pbx.example.local",
    "port": 5060,
    "transport": "udp",
    "username": "9001",
    "password": "DPAPI:AQAAANCMnd8BFdERjHoAwE/...",
    "passwordPlaintext": false,
    "conferenceRoom": "8000",
    "displayName": "PC-A-LoopbackSender",
    "useRegister": false,
    "localRtpPort": 16384
  },
  "audio": {
    "captureDeviceId": "default",
    "opusBitrate": 48000,
    "opusComplexity": 5,
    "frameMs": 20,
    "sampleRate": 48000,
    "channels": 1
  },
  "silenceDetection": {
    "thresholdDbfs": -50.0,
    "enterSilenceMs": 1500,
    "exitSilenceMs": 300
  },
  "batch": {
    "onPlaybackStart": "C:\\scripts\\on_start.bat",
    "onPlaybackStop": "C:\\scripts\\on_stop.bat",
    "executionTimeoutMs": 5000
  },
  "reconnect": {
    "initialDelayMs": 5000,
    "maxDelayMs": 60000,
    "backoffMultiplier": 2.0
  },
  "startup": {
    "initialJitterMs": 5000
  },
  "logging": {
    "directory": "%LOCALAPPDATA%\\PcAudioSipBridge\\logs",
    "maxFileSizeMb": 10,
    "maxFiles": 5
  }
}
```

### 拠点別 config.json 配布 (大規模展開)

- サイレントインストール後に `%ProgramData%\LoopcastUA\config.json` を PC ごとに配置する運用
- 通常の利用では設定画面 (GUI) から保存するため、config.json を手動作成する必要はない

---

## 7. 開発環境

### 必須ツール

- **VS Code** (最新版、メインエディタ)
- **Build Tools for Visual Studio 2026** (ワークロード: .NET デスクトップ ビルド ツール、約 2GB)
  - MSBuild 17、C# コンパイラ (Roslyn)、NuGet targets が含まれる
  - インストーラーのコンポーネント選択:
    - ✅ チェックする: 「.NET デスクトップ ビルド ツール」ワークロード
    - ✅ チェックする: 「.NET Framework 4.6.2 - 4.7.1 開発ツール」(参照アセンブリが含まれる)
    - ❌ 不要: .NET Framework 4.8 開発ツール
    - ❌ 不要: ClickOnce ビルドツール
    - ❌ 不要: .NET SDK
    - ❌ 不要: Windows Communication Foundation
    - ❌ 不要: ツールのコア機能のテストビルドツール
- **NuGet CLI** (`nuget.exe` を PATH に配置)
  - https://www.nuget.org/downloads から `nuget.exe` をダウンロード
  - `C:\tools\` に配置し、システム環境変数 PATH に `C:\tools` を追加
- **WiX Toolset v3.14** (MSI 作成用、Phase 7 まで不要)
  - .NET SDK 不要のスタンドアロン MSI インストーラー
  - https://github.com/wixtoolset/wix3/releases から `wix314.exe` をダウンロード・実行
  - インストール後: `C:\Program Files (x86)\WiX Toolset v3.14\bin\` (candle.exe, light.exe, heat.exe)

### VS Code 拡張

- **C# Dev Kit** (Microsoft 公式、デバッガ含む)
- **Claude Code** (実装作業の中核)
- (任意) NuGet Gallery (パッケージ追加の GUI)
- (任意) WiX Toolset 拡張 (wxs ファイルの構文ハイライト)

### 開発方針

- **Visual Studio (IDE 版) は使用しない** (VS Code のみで完結)
- **WinForms デザイナー不使用**、すべて Code-first
- ホットリロードは C# Dev Kit の機能を使用
- F5 でデバッグ実行、ブレークポイント・変数ウォッチは VS Code 内で完結

### ビルド手順

```powershell
# NuGet パッケージ復元
cd client
nuget restore PcAudioSipBridge.sln

# クライアント Release ビルド
msbuild PcAudioSipBridge.sln /p:Configuration=Release /p:Platform="Any CPU"
# 出力: client/PcAudioSipBridge/bin/Release/PcAudioSipBridge.exe + 依存 DLL

# MSI パッケージ作成 (Phase 7) — WiX v3 ビルドスクリプト
cd ../installer
.\build.ps1 -Version 1.0.0
# 出力: installer/bin/Release/PcAudioSipBridge-1.0.0.msi
```

### インストール済みツールのパス (環境確認済み: 2026-04-25)

| ツール | バージョン | パス |
|---|---|---|
| MSBuild | 18.5.4 | `C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\MSBuild.exe` |
| NuGet CLI | 7.3.1 | `C:\Tools\nuget.exe` |
| .NET Framework 参照アセンブリ | 4.6.2〜4.7.1 | `C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\` |

### デバッグ

- VS Code で F5 (C# Dev Kit デバッガ使用)
- 常駐プロセスへのアタッチ: コマンドパレット → "Debug: Attach to .NET Process"

---

## 8. 配布 / インストール

### MSI パッケージ構成 (WiX)

**インストール先:**
- `%ProgramFiles%\PcAudioSipBridge\` (64bit) または `%ProgramFiles(x86)%\PcAudioSipBridge\` (32bit)
- 配置ファイル: `PcAudioSipBridge.exe`, 依存 DLL (SIPSorcery.dll, NAudio.*.dll, Concentus.dll 等), アイコンリソース
- 設定ファイル: `%ProgramData%\PcAudioSipBridge\config.json` にテンプレートを配置

**レジストリ / スタートメニュー:**
- スタートメニューに「PC Audio SIP Bridge」と「Settings」のショートカット
- アンインストーラ登録 (標準の「プログラムと機能」から削除可能)

**スタートアップ登録:**
- カレントユーザーのスタートアップレジストリ (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`) に登録
- インストール時にチェックボックスで有効/無効選択可能

**設定ファイルの扱い:**
- アンインストール時に `%ProgramData%\PcAudioSipBridge\config.json` は**保持** (再インストール時の設定維持のため)
- 完全削除オプションも MSI UI で提供

**カスタムアクション (任意):**
- 初回インストール時に config.json の password を平文 → DPAPI 暗号化に変換
- 実現方法: MSI のカスタムアクションで exe のサブコマンド `PcAudioSipBridge.exe --encrypt-config` を呼び出す

### MSI 配布方式

**オプション A: 手動インストール (初期展開)**
- MSI を各 PC に配布し、管理者権限で実行
- サイレントインストール例:
  `msiexec /i PcAudioSipBridge-1.0.0.msi /qn SERVER=pbx.example.local EXTENSION=9001 PASSWORD=xxx`

**オプション B: GPO 経由 (大規模展開)**
- Active Directory のグループポリシーで MSI を組織ユニットに割り当て
- PC 起動時に自動インストール
- 設定値 (Extension、password) はインストール後に `%ProgramData%\PcAudioSipBridge\config.json` を拠点ごとに配布

**オプション C: SCCM / Intune**
- アプリケーションパッケージとして MSI を登録
- 配信ルール・進捗管理を中央で制御

### アップデート戦略

- MSI の UpgradeCode は固定、ProductCode はバージョンごとに変更
- メジャー/マイナーアップグレードで古いバージョンを自動アンインストール → 新バージョンインストール
- config.json はアップグレード時に保持

### 依存ランタイム

- **.NET Framework 4.7.2** 以降 (Win8.1 以降は標準搭載、Win7 SP1 は MS Update で入手可能)
- WiX の Bootstrapper (Burn) で .NET Framework 検出 → 未導入なら MS 配布 URL から自動ダウンロード・インストール
- Visual C++ ランタイム: 不要 (ネイティブ DLL 未使用)

### コード署名 (推奨)

- 40 台業務展開なら署名必須 (SmartScreen 警告回避、Defender 誤検知低減)
- 対象: `PcAudioSipBridge.exe` と MSI パッケージ
- 依存 DLL (SIPSorcery, NAudio, Concentus) は NuGet 配布時点で既に署名されているものを利用
- 署名証明書の選択肢は未決 (Section 11 TODO)

---

## 9. 管理 / モニタリング

### FreePBX Web UI での日常操作

- Reports → CDR Reports: 接続履歴、切断理由
- Reports → Asterisk Info → ConfBridge Info: 会議室参加者一覧、CPU 統計
- Applications → Conferences → (会議室 8000) → Attendees: 参加者個別の音量・mute
- UCP の Conference Pro モジュール: リアルタイム参加者ビュー

### Zabbix 等での監視項目 (推奨)

- VM の NIC bps / pps / ドロップ / エラー
- Asterisk プロセスの CPU / メモリ
- chan_pjsip アクティブチャネル数 (`pjsip show channelstats` のパース)
- ConfBridge 参加者数 (`confbridge list` のパース)
- クライアント側ログ (接続/切断イベント) を syslog 転送してもよい

---

## 10. セキュリティ方針

- **前提: 社内 LAN 限定**、インターネット公開しない
- FreePBX の Firewall モジュールで受信元 IP を社内セグメントに限定
- Extension のパスワードは 16 文字以上のランダム文字列 (Bulk Handler で生成)
- クライアント側は **DPAPI** で password を暗号化して config.json に格納
- `Allow Anonymous Inbound SIP Calls = No` (既定、維持)
- 将来的にインターネット越しから繋ぐ必要が出た場合は **TLS + SRTP + WireGuard** 経路の 3 層防御を設計し直す

---

## 11. 未決事項 / TODO

### サーバ側

- [ ] FreePBX Distro の具体的な VM プロビジョニング方法 (Proxmox で立てる想定? 既存仮想基盤?)
- [ ] IP アドレス / FQDN の決定 (pbx.example.local 部分)
- [ ] DNS / 証明書管理の扱い (社内 CA 使うか、IP 直打ちか)
- [ ] バックアップ戦略 (FreePBX 設定のエクスポート頻度、Veeam 連携するか)

### クライアント側

- [ ] タスクトレイアイコン 3 状態分 (.ico ファイル) のデザイン
- [ ] エラー時のユーザー通知方法 (トースト通知? ツールチップのみ? 設定画面の Status タブ?)
- [ ] config.json の配置場所最終決定 (ProgramData 共通 vs LocalAppData 個別)
- [ ] 初回起動時の password 平文 → DPAPI 自動変換のタイミング (起動時検出? MSI カスタムアクション?)

### 配布 / インストール

- [ ] コード署名証明書の選定・取得 (EV 証明書? 通常 OV?)
- [ ] MSI の UI カスタマイズ範囲 (標準 WixUI_Mondo で十分? 独自画面必要?)
- [ ] 40 拠点への展開手順 (手動? SCCM/Intune? GPO?)
- [ ] MSI インストール時の既定設定 (MSI Properties で投入? 後から config.json 配布?)

### 運用

- [ ] 非エンジニア向け運用手順書 (FreePBX 画面キャプチャつき) の作成
- [ ] トラブルシューティングフロー (接続できない時の切り分け)
- [ ] ログ収集の集中化 (Event Log 連携? syslog 転送?)

---

## 12. 検討したが不採用の選択肢 (再提案されないように記録)

### プロトコル

| 選択肢 | 不採用理由 |
|---|---|
| **Mumble** | 管理 UI が全て 10 年以上更新停止 (MumPI, phpMumbleAdmin, Mumble-Django)。Ice middleware 依存で運用コスト高。リスナー側ミキシング思想で集中制御に向かない |
| **Jitsi Meet** | ブラウザ完結型、常駐 SIP UA 路線と根本思想が合わない。Jitsi Admin は Docker/K8s 前提でインフラ運用負荷が高い |
| **LiveKit (WebRTC SFU)** | libwebrtc が重く、C# 常駐クライアントの低フットプリント目標に反する。クライアント実装の作り直しが必要 |
| **Icecast + Liquidsoap** | HTTP 遅延 (数秒) が会議用途に大きすぎる。Liquidsoap DSL の学習コスト |
| **RTP 直打ち (SIP なし)** | FreePBX の会議ブリッジに入れるには結局 SIP が要る |
| **WebRTC (ブラウザ完結)** | PC 音声のループバックキャプチャがブラウザから困難、常駐アプリ要件と不整合 |

### サーバ製品

| 選択肢 | 不採用理由 |
|---|---|
| **FreeSWITCH** | コア性能は上だが管理 UI が弱点。FusionPBX は過剰機能、自作 ESL UI は運用メンバー引き継ぎが重い。Asterisk + FreePBX の方が運用 UI の既成品が充実 |

### クライアント言語

| 選択肢 | 不採用理由 |
|---|---|
| **C++** | メモリ 10〜15MB、CPU 0.1〜0.4% 削減できるが、SIP スタック (PJSIP は重い、自作は 1500〜2500 行)、WASAPI 直叩き、長期保守コストが高い。C# で十分な目標値を達成可能 |

### コーデック

| 選択肢 | 不採用理由 |
|---|---|
| **G.711 (μ-law)** | 音質が電話品質止まり、音楽や通知音の用途に不足 |
| **G.722** | 16kHz 広帯域で悪くはないが、Opus 48kHz の方が明確に上。Opus は FreePBX/Asterisk で標準対応 |
| **ネイティブ libopus.dll 同梱** | 配布構成が複雑化 (MSI で扱えるが、pure managed Concentus で十分) |

### クライアント構成

| 選択肢 | 不採用理由 |
|---|---|
| **Windows サービス化** | Session 0 分離のため、ユーザーセッションで再生される音声を WASAPI ループバックで取得できない。常駐アプリ方式が必須 |
| **REGISTER 維持** | 着信不要のため不要。発信時 Digest 認証のみで十分 |
| **NLog / log4net** | 配布サイズ・メモリ増加。System.Diagnostics.Trace + 自前ロガーで十分 |
| **WinForms デザイナー使用** | 設定画面 1 つだけのため、Code-first で十分。VS 不要、差分レビュー容易 |

### 配布方式

| 選択肢 | 不採用理由 |
|---|---|
| **Costura.Fody 単一 exe 化** | 配布 1 ファイル化のメリットはあるが、DLL 二重ロードで +5〜10MB のメモリオーバーヘッド、自己解凍的挙動でウィルス誤検知リスク、40 台規模ではアップデート差分配布の利点が失われる |
| **DLL 外出し + zip 配布 (インストーラーなし)** | 40 台規模では手動展開のコスト高。GPO 配布できず、アンインストーラー未対応 |
| **Inno Setup (exe 形式インストーラー)** | 手軽だが GPO 配布非対応、MSI の方が業務 PC 運用に適合 |

### NAT 対策

| 選択肢 | 不採用理由 |
|---|---|
| **STUN / ICE / TURN** | 社内 LAN 前提なので不要。将来インターネット経由が必要になったら WireGuard 経路で解決予定 |

### 開発環境

| 選択肢 | 不採用理由 |
|---|---|
| **Visual Studio Community (IDE)** | 10〜15GB のディスク、今回は WinForms デザイナーを使わないため不要。VS Code + Build Tools で完結 |

---

## 13. 参考: 会話履歴の経緯 (要点のみ)

1. 最初は .NET Framework 4.8 + G.711 で提案
2. 「MS 再配布 DLL は同梱しない、配布サイズ最小化」要件で .NET Framework 4.6.2 に降格
3. C++ 移行の是非を検討 → メモリ 10〜15MB、CPU は両言語とも無視できる領域と判断、C# 継続
4. コーデック再評価 → FreePBX 前提なら Opus 推奨、Concentus で pure managed 実装可
5. NAT 議論 → 社内 LAN 前提で rport + Local Networks で解決
6. プロトコル再検討 → 「パケットが届けば鳴る」シンプル路線も検討したが、ミキサー要件で SIP 継続
7. 管理 UI 比較 → FreeSWITCH の管理 UI 弱点判明 → Asterisk + FreePBX に乗り換え
8. REGISTER 不要化 → 発信専用 UA 化で合意
9. 規模確定 → 20〜40 台、社内 LAN、100Mbps で設計
10. 配布方式議論 → Costura 単一 exe のメモリ/誤検知デメリット判明 → DLL 外出しへ転換
11. 開発環境議論 → VS Code のみで完結可能と判断 (WinForms デザイナー不要)
12. 設定画面要件追加 → Code-first で実装、ConfigStore + ConfigChanged イベント方式
13. 配布方式最終決定 → MSI (WiX) インストーラー採用、GPO 展開対応

---

_このドキュメントはプロジェクト設計の single source of truth として維持する。設計変更時は必ず本ファイルを更新すること。_
