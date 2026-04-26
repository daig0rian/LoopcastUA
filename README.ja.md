# LoopcastUA

[English](README.md)

Windows PC で再生されている音声を SIP 経由で FreePBX の会議ブリッジに常時配信する、タスクトレイ常駐型クライアントアプリケーション。

---

## 概要

20〜40 台の Windows PC それぞれで再生されている音声 (通知音、アナウンスなど) を、Opus コーデックで圧縮しながら FreePBX の ConfBridge に集約し、拠点間で共通の音声配信を実現します。

```
[PC-A] LoopcastUA ─┐
[PC-B] LoopcastUA ─┤  SIP/RTP (Opus)  ┌─────────────────┐
[PC-C] LoopcastUA ─┼─────────────────▶│ FreePBX         │──▶ 参加者
       ...         │                  │ ConfBridge 8000 │
[PC-N] LoopcastUA ─┘                  └─────────────────┘
```

**主な特長：**

- WASAPI ループバックキャプチャで PC の音声出力をそのまま取得
- Opus 48kHz / 48kbps でエンコードして低遅延・低帯域で送信
- 無音検出 (ヒステリシス付き) により、再生開始・停止のタイミングで任意のバッチファイルを実行
- 切断時は指数バックオフで自動再接続
- タスクトレイアイコンで接続状態をひと目で確認 (黄=接続中 / 緑=接続済 / 緑+波=再生中 / 赤=エラー)
- EN / JA バイリンガル UI (システム言語に自動追従、設定画面で手動切替も可)
- MSI インストーラー対応 (GPO 一括配布に対応)

---

## システム要件

### クライアント (LoopcastUA)

| 項目 | 要件 |
|---|---|
| OS | Windows 7 SP1 以降 (64bit) |
| ランタイム | .NET Framework 4.7.2 以降 |
| 音声 | WASAPI ループバック対応オーディオデバイス |
| ネットワーク | FreePBX サーバーへの UDP 5060 (SIP) および UDP 1 万番台 (RTP) の疎通 |

### サーバー (FreePBX)

| 項目 | 推奨値 |
|---|---|
| ディストリビューション | FreePBX 17 (Debian 12 ベース) |
| Asterisk | 20 (LTS) |
| vCPU | 4 |
| RAM | 4 GB |
| NIC | 100 Mbps |

サーバーのセットアップ手順は [docs/freepbx-runbook.md](docs/freepbx-runbook.md) を参照してください。

---

## ビルド手順

### 必要ツール

| ツール | 入手先 |
|---|---|
| Build Tools for Visual Studio 2022 以降 | [visualstudio.microsoft.com](https://visualstudio.microsoft.com/downloads/) ワークロード「.NET デスクトップ ビルド ツール」を選択 |
| NuGet CLI (`nuget.exe`) | [nuget.org/downloads](https://www.nuget.org/downloads) → `C:\tools\` に配置して PATH に追加 |
| WiX Toolset v3.14 (MSI 作成時のみ) | [github.com/wixtoolset/wix3/releases](https://github.com/wixtoolset/wix3/releases) |

### EXE ビルド

```powershell
cd client
nuget restore LoopcastUA.sln
msbuild LoopcastUA.sln /p:Configuration=Release "/p:Platform=Any CPU"
# 出力: client/LoopcastUA/bin/Release/LoopcastUA.exe
```

### MSI ビルド

```powershell
# EXE ビルド後に実行
cd installer
powershell -ExecutionPolicy Bypass -File build.ps1 -Version 1.0.0
# 出力: installer/bin/Release/LoopcastUA-1.0.0.msi
```

---

## インストール

### 通常インストール (GUI)

`LoopcastUA-x.y.z.msi` をダブルクリックしてウィザードに従います。

インストール後、初回起動時に設定画面が自動的に開きます。SIP サーバー情報を入力して保存すると接続を開始します。

### サイレントインストール (大規模展開)

```cmd
msiexec /i LoopcastUA-1.0.0.msi /qn
```

インストール後、各 PC の設定ファイルを配置します：

```
%ProgramData%\LoopcastUA\config.json
```

### アップグレード

新しいバージョンの MSI をそのまま実行すると、旧バージョンが自動的にアンインストールされてから新バージョンがインストールされます (`config.json` は保持されます)。

---

## 設定

**通常の使い方では設定ファイルを直接編集する必要はありません。**  
MSI インストール後に初回起動すると設定画面が自動で開くので、SIP サーバー情報を入力して保存するだけで接続が始まります。設定内容はタスクトレイアイコンの右クリックメニュー「設定...」からいつでも変更できます。

設定は `%ProgramData%\LoopcastUA\config.json` に自動保存されます。

---

### 設定ファイルのリファレンス (上級者・大規模展開向け)

サイレントインストール後に config.json を事前配置する場合や、スクリプトで複数台分を生成する場合の参考として記載します。雛形は [installer/config.template.json](installer/config.template.json) を参照してください。

### 主な設定項目

```jsonc
{
  "sip": {
    "server": "192.168.11.29",       // FreePBX サーバーの IP またはホスト名
    "port": 5060,
    "username": "9001",              // Extension 番号
    "password": "DPAPI:AQAAn...",    // DPAPI 暗号化済みパスワード (平文可、下記参照)
    "conferenceRoom": "8000",        // ConfBridge の番号
    "displayName": "PC-A-Loopback"
  },
  "audio": {
    "opusBitrate": 48000             // bps (8000〜128000)
  },
  "silenceDetection": {
    "thresholdDbfs": -50.0,          // 無音判定閾値
    "enterSilenceMs": 1500,          // 無音と判定するまでの継続時間
    "exitSilenceMs": 300             // 再生と判定するまでの継続時間
  },
  "batch": {
    "onPlaybackStart": "C:\\scripts\\on_start.bat",
    "onPlaybackStop":  "C:\\scripts\\on_stop.bat",
    "executionTimeoutMs": 5000
  }
}
```

### パスワードの暗号化

設定画面から保存すると自動的に DPAPI (Windows Data Protection API) で暗号化されます。
コマンドラインから手動で暗号化する場合：

```cmd
"C:\Program Files\LoopcastUA\LoopcastUA.exe" --encrypt-config
```

平文で運用する場合は `"passwordPlaintext": true` を設定してください (テスト・デバッグ用途のみ推奨)。

### 複数台への config.json 一括配布

サイレントインストール (`/qn`) を使う大規模展開では、事前に PC ごとの `config.json` を用意して `%ProgramData%\LoopcastUA\` に配置します。FreePBX の Bulk Handler で Extension を一括登録した際の CSV を元に、スクリプトで台数分の config.json を生成する運用が想定されます。

---

## 無音検出とバッチ実行

音声の再生状態が変化したタイミングで任意のスクリプトを実行できます。

| イベント | 設定キー | 用途例 |
|---|---|---|
| 再生開始 (無音 → 有音) | `batch.onPlaybackStart` | 照明点灯、スクリーン ON、通知送信 |
| 再生停止 (有音 → 無音) | `batch.onPlaybackStop` | 照明消灯、スクリーン OFF |

`.bat` / `.cmd` / `.exe` / `.ps1` に対応。`executionTimeoutMs` で実行上限時間を設定します。

---

## プロジェクト構成

```
loopcast/
├── client/
│   ├── LoopcastUA.sln
│   └── LoopcastUA/
│       ├── LoopcastUA.csproj
│       ├── config.sample.json
│       └── src/
│           ├── Program.cs              # エントリポイント
│           ├── TrayContext.cs          # タスクトレイ・ライフサイクル管理
│           ├── Forms/
│           │   └── SettingsForm.cs     # 設定画面 (Code-first WinForms)
│           ├── Audio/
│           │   ├── LoopbackCapturer.cs # WASAPI ループバック
│           │   ├── AudioMixer.cs       # ステレオ → モノラル
│           │   ├── OpusEncoder.cs      # Concentus (pure C# Opus)
│           │   └── SilenceDetector.cs  # ヒステリシス付き無音検出
│           ├── Sip/
│           │   ├── SipClient.cs        # SIPSorcery 制御・再接続
│           │   └── RtpSender.cs        # RTP 送信
│           ├── Config/
│           │   ├── AppConfig.cs        # 設定 POCO
│           │   ├── ConfigStore.cs      # 読み書き・ConfigChanged イベント
│           │   ├── ConfigValidator.cs  # バリデーション
│           │   └── DpapiProtector.cs   # パスワード暗号化
│           ├── Batch/
│           │   └── BatchRunner.cs      # バッチ実行
│           └── Infrastructure/
│               ├── Logger.cs           # ローリングファイルロガー
│               ├── BufferPool.cs       # byte[] プール
│               └── Strings.cs          # i18n (EN / JA)
├── installer/
│   ├── Product.wxs                     # WiX v3 MSI 定義
│   ├── build.ps1                       # ビルドスクリプト
│   └── config.template.json            # 設定ファイル雛形
├── docs/
│   └── freepbx-runbook.md              # FreePBX セットアップ手順書
└── tools/
    └── generate_extensions.py          # FreePBX Extension CSV 生成 (予定)
```

---

## 使用ライブラリ

| ライブラリ | バージョン | ライセンス | 用途 |
|---|---|---|---|
| [SIPSorcery](https://github.com/sipsorcery-org/sipsorcery) | 10.0.3 | BSD-3-Clause + 追加条項 ([詳細](THIRD_PARTY_NOTICES.md)) | SIP / RTP スタック |
| [NAudio](https://github.com/naudio/NAudio) | 2.3.0 | MIT | WASAPI ループバックキャプチャ |
| [Concentus](https://github.com/lostromb/concentus) | 2.2.2 | BSD-3-Clause | Opus エンコーダー (pure C#) |
| [Newtonsoft.Json](https://www.newtonsoft.com/json) | 13.0.3 | MIT | JSON 設定ファイル読み書き |

---

## ライセンス

[MIT License](LICENSE)

依存ライブラリのライセンスおよび著作権表示については [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) を参照してください。
