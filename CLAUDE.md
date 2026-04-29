# LoopcastUA

NMS などの監視アプリケーションが再生するアラート音を監視 PC から収集し、FreePBX の会議ブリッジへ配信するタスクトレイ常駐型クライアント。監視担当者がどこにいても警告音を聞き取れるようにし、音声検出をトリガーに SNMP トラップなどで上位 NMS に自動通知することを主目的とする。

---

## 1. プロジェクト目的と利用シナリオ

- NMS や監視ツールが再生するアラート音を 20〜40 台の監視 PC から収集し、1 つの会議ブリッジに集約
- 監視担当者は任意のエンドポイントから聴取し、何のアラートが鳴っているかをリアルタイムに把握できる
- どの PC で鳴っているかの特定はバッチ実行による SNMP トラップ等が主手段。視覚的特定が必要な場合は FreePBX Conference Pro (有償) または Asterisk AMI の `ConfbridgeTalking` イベントを活用する
- 音声検出時にバッチを実行して SNMP トラップ送信・チケット起票・オンコール通知など上位システムへ連携 (担当者不在でも機能する)
- 各拠点の参加者は音を聞くだけで発話はしない (片方向音声配信)
- 音量バランス調整や mute などのミキサー機能はサーバ側管理 UI で集中制御
- クライアントは各 PC に常駐させ、起動と同時に自動接続
- 業務 PC への展開は MSI パッケージ経由、GPO 一括配布対応

---

## 2. 確定した技術選定

### サーバ側

| 項目 | 選定 | 理由 |
|---|---|---|
| ディストリビューション | **FreePBX Distro (Debian 12 ベース)** | 一括インストール、Web UI 標準 |
| SIP エンジン | **Asterisk 22 LTS (FreePBX 同梱)** | 成熟した Web 管理 UI が使える、codec_opus.so 標準同梱 |
| SIP ドライバ | **chan_pjsip** | Opus 対応、現行標準 |
| 会議ブリッジ | **ConfBridge** | Applications → Conferences で管理 |
| 管理 UI | **FreePBX Web UI + UCP** | 参加者一覧、音量調整、mute、kick が GUI で可能 |
| デプロイ | **VM (vCPU 4, RAM 4GB, 100Mbps NIC)** | 40 台規模に十分 |

### クライアント側 (Windows 常駐アプリ)

| 項目 | 選定 | 理由 |
|---|---|---|
| 言語 / ランタイム | **C# / .NET Framework 4.7.2** | Win7 SP1 対応、NAudio 2.x の最低要件 |
| SIP スタック | **SIPSorcery** | Pure managed、ネイティブ依存なし |
| 音声キャプチャ (Direct・既定) | **WASAPI Process Loopback (`ActivateAudioInterfaceAsync` P/Invoke)** | OS ミックス段階で取得、マスターボリューム非依存。Win10 20H2 (build 19042)+ 必須 |
| 音声キャプチャ (Rendered・代替) | **NAudio (WASAPI ループバック)** | 標準 API、Vista 以降対応。ボリューム依存。`audio.captureMode = "rendered"` で選択 |
| コーデック | **Concentus (Opus pure C#)** | ネイティブ DLL 不要、managed only |
| リサンプラ | **NAudio WdlResamplingSampleProvider** | pure managed、Media Foundation 非依存 |
| UI フレームワーク | **WinForms (.NET Framework)** | 標準、軽量、タスクトレイ対応 |
| UI 実装方針 | **Code-first (デザイナー不使用)** | VS Code のみで開発可、差分が読みやすい |
| 配布形態 | **MSI インストーラー (WiX Toolset v3)** | GPO 配布対応、40 台運用に適合 |
| DLL 配置 | **外出し (インストールディレクトリに配置)** | メモリ効率、ウィルス誤検知回避 |
| 常駐方式 | **常駐アプリ (Windows サービスではない)** | WASAPI ループバックはユーザーセッション必須 |
| パスワード保護 | **DPAPI (Windows 標準暗号化)** | 同一ユーザーのみ復号可、追加ライブラリ不要 |

### 通信

| 項目 | 選定 | 理由 |
|---|---|---|
| コーデック | **Opus 48kbps / 48kHz / モノラル** | 音質良、帯域軽量、FreePBX 推奨 |
| フレーム長 | **20ms** | SIP 標準、pps 50 |
| トランスポート | **UDP (平文 RTP、平文 SIP)** | 社内 LAN 前提、TLS/SRTP 不要 |
| REGISTER | **不要 (発信専用 UA)** | 着信なし、Digest 認証のみ使用 |
| 接続方式 | 起動時に会議室番号へ INVITE、切断時は指数バックオフで再接続 | |
| NAT | 社内 LAN 同一セグメント前提、PBX の Local Networks 設定のみ | |

---

## 3. 主要な数値パラメータ

### 帯域計算 (40 台同時接続)

- 1 セッション実効: 約 64kbps (Opus 48kbps + RTP/UDP/IP ヘッダ)
- 40 台合計 (上り + 下り): 約 5.1 Mbps
- 100Mbps NIC に対して約 5%、余裕あり

### 想定リソース (クライアント側)

- 定常メモリ: 15〜25 MB (CLR 含む、DLL 外出し)
- CPU 使用率: 0.5〜2% (Concentus Opus エンコード含む)
- インストール後のディスク使用量: 約 5〜10 MB (exe + DLL 数個)

### SIP / Extension 発番ルール (推奨)

- 常駐クライアント用 Extension: 9001〜9040 (40 個)
- 会議室番号: 8000
- Extension のパスワードはランダム生成 (Bulk Handler CSV 経由で一括登録)

### アラート検出 (ヒステリシス付き無音検出、設定画面から変更可)

- 閾値 (既定): -50 dBFS (RMS)
- アラート終了判定ガード時間 (既定): -50dBFS 未満が 1500ms 継続
- アラート開始判定ガード時間 (既定): -50dBFS 以上が 300ms 継続
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

**User Profile:**
- Announce User Count: No
- Announce Join/Leave: No
- Quiet Mode: Yes
- Music on Hold When Empty: No

**Conference Profile:**
- Max Participants: 50
- Record Conference: No

### Extension 一括作成

- FreePBX Admin → Bulk Handler で CSV インポート
- 列: `extension,name,secret,...`
- 40 行分の CSV を生成するスクリプトをプロジェクトで用意予定 (`tools/generate_extensions.py`)

### NAT 設定 (社内 LAN)

- Settings → Asterisk SIP Settings → Local Networks に社内セグメント追加 (例: `192.168.0.0/16`)
- External IP は空欄

### ネットワーク / QoS (任意)

- VM NIC は VirtIO (KVM) / VMXNET3 (VMware) を使用
- ToS for Audio = `ef` (DSCP 46) で優先制御用マーキング

---

## 5. クライアント側の設計

### プロジェクト構成

```
loopcast/
├── CLAUDE.md
├── README.md
├── README.ja.md
├── LICENSE
├── THIRD_PARTY_NOTICES.md
├── client/
│   ├── LoopcastUA.sln
│   └── LoopcastUA/
│       ├── LoopcastUA.csproj
│       ├── app.config               # GC モード設定
│       ├── config.sample.json
│       ├── Resources/
│       │   └── app.ico              # GDI+ で生成したスピーカーアイコン (16/32/48/256px)
│       └── src/
│           ├── Program.cs
│           ├── TrayContext.cs       # タスクトレイ・ライフサイクル管理
│           ├── Forms/
│           │   └── SettingsForm.cs  # 設定画面 (Code-first)
│           ├── Config/
│           │   ├── AppConfig.cs
│           │   ├── ConfigStore.cs
│           │   ├── ConfigValidator.cs
│           │   └── DpapiProtector.cs
│           ├── Audio/
│           │   ├── ILoopbackCapturer.cs         # キャプチャーインターフェース
│           │   ├── LoopbackCapturerFactory.cs   # captureMode に応じて実装を選択
│           │   ├── ProcessLoopbackCapturer.cs   # Direct: ActivateAudioInterfaceAsync P/Invoke
│           │   ├── LoopbackCapturer.cs          # Rendered: NAudio WASAPI ループバック
│           │   ├── AudioMixer.cs
│           │   ├── OpusEncoder.cs
│           │   └── SilenceDetector.cs
│           ├── Sip/
│           │   ├── SipClient.cs
│           │   └── RtpSender.cs
│           ├── Batch/
│           │   └── BatchRunner.cs
│           └── Infrastructure/
│               ├── BufferPool.cs
│               ├── Logger.cs
│               └── Strings.cs       # i18n (EN / JA)
├── installer/
│   ├── Product.wxs
│   ├── build.ps1
│   ├── config.template.json
│   └── Resources/
│       └── LICENSE.rtf
├── docs/
│   └── freepbx-runbook.md
└── tools/
    ├── make_icon.ps1                # app.ico 生成スクリプト
    └── generate_extensions.py      # FreePBX Extension CSV 生成 (予定)
```

### ランタイム動作フロー

```
起動
 ↓
config.json 読み込み (DPAPI で password 復号)
 ↓
起動ジッタ (0〜5 秒ランダム待機、40 台一斉起動時の INVITE 集中回避)
 ↓
キャプチャ開始 (captureMode に応じて選択)
  - direct (既定): ActivateAudioInterfaceAsync → Process Loopback、ボリューム非依存
  - rendered: NAudio WASAPI ループバック、ボリューム依存
 ↓
FreePBX ConfBridge へ INVITE (To: sip:8000@pbx, From: sip:9001@pbx)
  - 401 Digest Auth 応答 → 認証
  - 200 OK → ACK → SDP ネゴ (Opus 48kHz mono)
 ↓
RTP 送信開始
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

### アラート検出とバッチ実行

音声検出時のバッチ実行は担当者不在時の自動通知を主目的とする。

| イベント | 設定キー | 主な用途 |
|---|---|---|
| アラート開始 (無音 → 有音) | `batch.onPlaybackStart` | SNMP トラップ送信、チケット起票、オンコール通知 |
| アラート終了 (有音 → 無音) | `batch.onPlaybackStop` | アラート解除トラップ、状態クリア |

### メモリ / CPU 最適化ポイント

1. **GC モード**: app.config で Workstation GC + Concurrent GC
2. **バッファプール**: 20ms フレームの byte[] を毎回 new しない
3. **受信 RTP 即破棄**: ジッタバッファ・デコーダを立てない
4. **起動直後に `Environment.SetProcessWorkingSetSize`** で WS トリム
5. **ログは Trace + 自前 RollingFileWriter**、NLog 等の重いライブラリ不使用
6. **LINQ をホットパスで使わない** (隠れアロケーション回避)

### 再接続戦略

- 初期遅延 5000ms、バックオフ倍率 2.0、上限 60000ms
- SIP 接続確立 (200 OK への ACK 送信後) で初めて接続済み状態に遷移
- 再接続中はタスクトレイアイコンで状態表示 (黄: 接続中、緑: 接続済、緑+波: アラート検出中、赤: エラー)
- `volatile bool _sipConnected` で SIP 接続状態を管理し、未接続時はアラート検出イベントを抑制

### タスクトレイ UI

- アイコン: GDI+ で描画したスピーカー形状 (波線あり/なし)、4 状態
- 右クリックメニュー: Status / Settings... / Open log folder / Reload config / Exit

### 設定画面 (Settings)

- WinForms、Code-first、タブ構成: General / SIP / Audio / Detection / Batch
- General タブ: UI 言語選択 (Auto / English / 日本語)
- 設定変更の適用: SIP 接続情報・デバイス・ビットレート変更は要再接続、検出パラメータ・バッチパスはホットリロード

### i18n

- `Infrastructure/Strings.cs` に EN/JA 文字列を static プロパティで定義
- `Strings.SetLang(Lang)` で切り替え、`TrayContext.RefreshMenuStrings()` でトレイメニューに即時反映
- `AppConfig.UiConfig.Language` ("auto" / "en" / "ja") で永続化

### ConfigStore の責務

- `config.json` の読み書き (`%ProgramData%\LoopcastUA\config.json`)
- DPAPI による password の暗号化/復号 (`"DPAPI:..."` プレフィックスで平文と識別)
- `ConfigChanged` イベント発火 (各モジュールが購読)

---

## 6. 設定ファイル: config.json スキーマ

```json
{
  "sip": {
    "server": "192.168.11.29",
    "port": 5060,
    "transport": "udp",
    "username": "9001",
    "password": "DPAPI:AQAAANCMnd8BFdERjHoAwE/...",
    "passwordPlaintext": false,
    "conferenceRoom": "8000",
    "displayName": "NMS-PC-A",
    "useRegister": false,
    "localRtpPort": 16384
  },
  "audio": {
    "captureMode": "direct",
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
    "onPlaybackStart": "C:\\scripts\\alert_start.bat",
    "onPlaybackStop": "C:\\scripts\\alert_stop.bat",
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
  "ui": {
    "language": "auto"
  },
  "logging": {
    "directory": "%LOCALAPPDATA%\\LoopcastUA\\logs",
    "maxFileSizeMb": 10,
    "maxFiles": 5
  }
}
```

---

## 7. 開発環境

### 必須ツール

| ツール | バージョン | パス |
|---|---|---|
| MSBuild | 18.5.4 | `C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\MSBuild.exe` |
| NuGet CLI | 7.3.1 | `C:\Tools\nuget.exe` |
| WiX Toolset v3.14 | 3.14 | `C:\Program Files (x86)\WiX Toolset v3.14\bin\` |
| GitHub CLI | 2.91.0 | `C:\Program Files\GitHub CLI\gh.exe` |
| .NET Framework 参照アセンブリ | 4.6.2〜4.7.1 | `C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\` |

### VS Code 拡張

- **C# Dev Kit** (Microsoft 公式、デバッガ含む)
- **Claude Code**

### 開発方針

- **Visual Studio (IDE 版) は使用しない** (VS Code のみで完結)
- **WinForms デザイナー不使用**、すべて Code-first

### ビルド手順

```powershell
cd client
nuget restore LoopcastUA.sln
msbuild LoopcastUA.sln /p:Configuration=Release "/p:Platform=Any CPU"
# 出力: client/LoopcastUA/bin/Release/LoopcastUA.exe

cd ../installer
.\build.ps1 -Version 1.0.2
# 出力: installer/bin/Release/LoopcastUA-1.0.2.msi
```

---

## 8. 配布 / インストール

**インストール先:** `%ProgramFiles%\LoopcastUA\`  
**設定ファイル:** `%ProgramData%\LoopcastUA\config.json` (アンインストール時も保持)  
**スタートアップ:** `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` に登録

### MSI 配布方式

- **手動**: `msiexec /i LoopcastUA-x.y.z.msi /qn`
- **GPO**: AD グループポリシーで MSI を割り当て、PC 起動時に自動インストール
- **SCCM / Intune**: アプリケーションパッケージとして登録

### リリース

- GitHub Releases に MSI を添付: `gh release create vX.Y.Z installer/bin/Release/LoopcastUA-X.Y.Z.msi`
- UpgradeCode 固定、ProductCode はバージョンごとに変更 (MajorUpgrade で旧版自動削除)

### コード署名 (推奨)

- SmartScreen 警告回避・Defender 誤検知低減のため署名を推奨
- 対象: `LoopcastUA.exe` と MSI パッケージ

---

## 9. 管理 / モニタリング

### FreePBX Web UI での日常操作

- Reports → CDR Reports: 接続履歴、切断理由
- Applications → Conferences → 会議室 8000 → Attendees: 参加者個別の音量・mute
- UCP の Conference Pro モジュール: リアルタイム参加者ビュー

### Zabbix 等での監視項目 (推奨)

- chan_pjsip アクティブチャネル数 (`pjsip show channelstats`)
- ConfBridge 参加者数 (`confbridge list`)
- VM の NIC bps / pps / ドロップ

---

## 10. セキュリティ方針

- **前提: 社内 LAN 限定**、インターネット公開しない
- FreePBX の Firewall モジュールで受信元 IP を社内セグメントに限定
- Extension のパスワードは 16 文字以上のランダム文字列
- クライアント側は **DPAPI** で password を暗号化して config.json に格納
- `Allow Anonymous Inbound SIP Calls = No` (既定、維持)
- 将来インターネット経由が必要になった場合は **TLS + SRTP + WireGuard** の 3 層防御で設計し直す

---

## 11. 未決事項 / TODO

### サーバ側

- [ ] FreePBX の VM プロビジョニング方法 (Proxmox? 既存仮想基盤?)
- [ ] IP アドレス / FQDN の決定
- [ ] バックアップ戦略 (FreePBX 設定のエクスポート頻度)

### 配布 / インストール

- [ ] コード署名証明書の選定・取得
- [ ] 40 拠点への展開手順 (手動? SCCM/Intune? GPO?)

### 運用

- [ ] 非エンジニア向け運用手順書の作成
- [ ] トラブルシューティングフロー (接続できない時の切り分け)

---

## 12. 検討したが不採用の選択肢 (再提案されないように記録)

### プロトコル

| 選択肢 | 不採用理由 |
|---|---|
| **Mumble** | 管理 UI が全て 10 年以上更新停止。Ice middleware 依存で運用コスト高。リスナー側ミキシング思想で集中制御に向かない |
| **Jitsi Meet** | ブラウザ完結型、常駐 SIP UA 路線と根本思想が合わない。Docker/K8s 前提でインフラ運用負荷が高い |
| **LiveKit (WebRTC SFU)** | libwebrtc が重く、低フットプリント目標に反する |
| **Icecast + Liquidsoap** | HTTP 遅延 (数秒) が監視用途に大きすぎる |
| **RTP 直打ち (SIP なし)** | FreePBX の会議ブリッジに入れるには SIP が必要 |

### サーバ製品

| 選択肢 | 不採用理由 |
|---|---|
| **FreeSWITCH** | 管理 UI が弱点。Asterisk + FreePBX の方が運用 UI の既成品が充実 |

### クライアント言語

| 選択肢 | 不採用理由 |
|---|---|
| **C++** | SIP スタック・WASAPI 直叩きの実装コストと長期保守コストが高い。C# で十分な目標値を達成可能 |

### コーデック

| 選択肢 | 不採用理由 |
|---|---|
| **G.711 (μ-law)** | 電話品質止まり。監視アラート音の判別精度が低下するリスク |
| **G.722** | Opus 48kHz の方が明確に上。Opus は FreePBX/Asterisk で標準対応 |
| **ネイティブ libopus.dll 同梱** | 配布構成が複雑化。pure managed Concentus で十分 |

### クライアント構成

| 選択肢 | 不採用理由 |
|---|---|
| **Windows サービス化** | Session 0 分離のため WASAPI ループバックが取得できない |
| **REGISTER 維持** | 着信不要。Digest 認証のみで十分 |
| **NLog / log4net** | 配布サイズ・メモリ増加。自前ロガーで十分 |
| **Costura.Fody 単一 exe 化** | DLL 二重ロードで +5〜10MB、ウィルス誤検知リスク、差分配布の利点消失 |
| **Inno Setup** | GPO 配布非対応。MSI の方が業務 PC 運用に適合 |

---

_このドキュメントはプロジェクト設計の single source of truth として維持する。設計変更時は必ず本ファイルを更新すること。_
