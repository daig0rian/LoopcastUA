# LoopcastUA

[English](README.md)

NMS などの監視アプリケーションが再生するアラート音を監視 PC から収集し、FreePBX の会議ブリッジへ配信するタスクトレイ常駐型クライアント。監視担当者がどこにいても警告音を聞き取れるようにします。

---

## 解決する課題

NMS や監視ツールは、インストールされた PC 上で音声アラートを再生します。担当者がその PC から離席していたり、アラートが 20〜40 台の監視 PC に分散していたりすると、警告に気づけないことがあります。

LoopcastUA は各監視 PC の音声出力をキャプチャし、1 つの会議ブリッジに集約します。担当者は任意のエンドポイントから聴取でき、どの PC で何のアラートが鳴っているかをリアルタイムに判別できます。

音声を検出したタイミングでスクリプトを実行する機能も備えており、SNMP トラップを上位 NMS に送信するなど、担当者が不在でも既存の監視インフラを通じてアラートを転送できます。

```
[NMS-PC-A] LoopcastUA ─┐
[NMS-PC-B] LoopcastUA ─┤  SIP/RTP (Opus)  ┌─────────────────┐
[NMS-PC-C] LoopcastUA ─┼─────────────────▶│ FreePBX         │──▶ 監視担当者
           ...         │                  │ ConfBridge 8000 │
[NMS-PC-N] LoopcastUA ─┘                  └─────────────────┘
                                                   │
                              アラート検出時        ▼
                                       [SNMP トラップ / 上位 NMS]
```

**主な特長：**

- **ダイレクトキャプチャ (既定)** — WASAPI Process Loopback (`ActivateAudioInterfaceAsync`) を使用し、OS のミックス段階で音声を取得するためマスターボリュームの影響を受けない。Windows 10 20H2 (ビルド 19042) 以降が必要。
- **レンダードキャプチャ (フォールバック)** — NAudio 経由の標準 WASAPI ループバック。Windows 7 SP1 以降で動作するが、音量はマスターボリュームに依存する。
- ヒステリシス付き無音検出 — アラート開始・終了のタイミングで任意のスクリプトを実行
- Opus 48kHz / 48kbps でエンコードして低遅延・低帯域で送信
- 切断時は指数バックオフで自動再接続
- タスクトレイアイコンで接続状態をひと目で確認 (黄=接続中 / 緑=接続済 / 緑+波=アラート検出中 / 赤=エラー)
- EN / JA バイリンガル UI (システム言語に自動追従、設定画面で手動切替も可)
- MSI インストーラー対応 (GPO 一括配布に対応)

---

## システム要件

### クライアント (LoopcastUA)

| 項目 | 要件 |
|---|---|
| OS | ダイレクトキャプチャ (既定): Windows 10 20H2 (ビルド 19042) 以降 / レンダードキャプチャ: Windows 7 SP1 以降 (いずれも 64bit) |
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
powershell -ExecutionPolicy Bypass -File build.ps1 -Version 1.1.0
# 出力: installer/bin/Release/LoopcastUA-1.1.0.msi
```

---

## インストール

### 通常インストール (GUI)

`LoopcastUA-x.y.z.msi` をダブルクリックしてウィザードに従います。

インストール後、初回起動時に設定画面が自動的に開きます。SIP サーバー情報を入力して保存すると接続を開始します。

### サイレントインストール (大規模展開)

```cmd
msiexec /i LoopcastUA-1.1.0.msi /qn
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
    "displayName": "NMS-PC-A"
  },
  "audio": {
    "captureMode": "direct",         // "direct" (Process Loopback, Win10 20H2+, ボリューム非依存) | "rendered" (標準 WASAPI, Win7+)
    "opusBitrate": 48000             // bps (8000〜128000)
  },
  "silenceDetection": {
    "thresholdDbfs": -50.0,          // アラート検出閾値
    "enterSilenceMs": 1500,          // アラート終了と判定するまでの無音継続時間
    "exitSilenceMs": 300             // アラート開始と判定するまでの有音継続時間
  },
  "batch": {
    "onPlaybackStart": "C:\\scripts\\alert_start.bat",
    "onPlaybackStop":  "C:\\scripts\\alert_stop.bat",
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

## アラート検出とスクリプト実行

LoopcastUA は監視 PC が音声を出力し始めた / 止めたタイミングを検出し、設定したスクリプトを実行します。担当者が不在でもアラートを上位システムに転送することを主な用途として設計されています。

| イベント | 設定キー | 主な用途 |
|---|---|---|
| アラート開始 (無音 → 有音) | `batch.onPlaybackStart` | 上位 NMS へ SNMP トラップ送信、チケット起票、オンコール通知 |
| アラート終了 (有音 → 無音) | `batch.onPlaybackStop` | アラート解除トラップ送信、状態クリア |

`.bat` / `.cmd` / `.exe` / `.ps1` に対応。`executionTimeoutMs` で実行上限時間を設定します。

**例: アラート開始時に SNMP トラップを送信する**

```bat
@echo off
snmptrap -v 2c -c public 192.168.1.10 "" 1.3.6.1.4.1.99999.1 1.3.6.1.4.1.99999.1.1 s "%COMPUTERNAME% audio alert detected"
```

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
│           │   ├── ILoopbackCapturer.cs         # キャプチャーインターフェース
│           │   ├── LoopbackCapturerFactory.cs   # 設定に応じて Direct / Rendered を選択
│           │   ├── ProcessLoopbackCapturer.cs   # Direct: WASAPI Process Loopback (Win10 20H2+)
│           │   ├── LoopbackCapturer.cs          # Rendered: 標準 WASAPI ループバック
│           │   ├── AudioMixer.cs                # ステレオ → モノラル
│           │   ├── OpusEncoder.cs               # Concentus (pure C# Opus)
│           │   └── SilenceDetector.cs           # ヒステリシス付き無音検出
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
