# FreePBX セットアップ手順書

対象: FreePBX 17 (OSO) + Asterisk 20 + Rocky Linux

---

## 1. インストール

### VM スペック (本番)
| 項目 | 値 |
|---|---|
| vCPU | 4 |
| RAM | 4 GB |
| ディスク | 100 GB |
| NIC | 100Mbps |

### インストーラー選択
- FreePBX Distro 17 ISO をダウンロード
- 起動後の Spice インストーラーで以下を選択:
  - **OSO** (Open Source Only) — 商用 Sangoma モジュール不要のため
  - **Asterisk 20** (LTS、codec_opus.so 標準同梱)

### インストール完了後
- TTY に sangoma ユーザーでログインし IP アドレスを確認
- ブラウザで `http://<IP>/` を開く → FreePBX ダッシュボード

---

## 2. Opus コーデック有効化

1. **Settings → Asterisk SIP Settings**
2. **「General SIP Settings」タブ** を選択
3. コーデック一覧から **「opus」** にチェックを入れ、リストの**一番上**に移動
4. **「Submit」** → 右上の **「Apply Config」** (オレンジ色のボタン)

---

## 3. Extension 作成 (一括登録)

### 単体登録
1. **Connectivity → Extensions → Add Extension → Add New SIP [chan_pjsip] Extension**
2. 設定値:
   - User Extension: `9001` (9001〜9040)
   - Display Name: `PC-XXXXX-Loopback` (PC 識別名)
   - Secret: ランダム 16 文字以上のパスワード
3. **Submit → Apply Config**

### 一括登録 (40台展開時)
1. **Admin → Bulk Handler**
2. **Import → Extensions** で CSV アップロード
3. CSV フォーマット:
   ```
   extension,name,secret,tech
   9001,PC-A-Loopback,<password>,pjsip
   9002,PC-B-Loopback,<password>,pjsip
   ...
   ```
4. `tools/generate_extensions.py` で CSV 生成 (予定)

---

## 4. ConfBridge 会議室 8000 の設定

### 会議室作成
1. **Applications → Conferences → Add Conference**
2. 設定値:
   - Conference Number: **8000**
   - Conference Name: **Loopcast Bridge**
   - User PIN: 空欄
   - Admin PIN: 空欄
   - Join Message: **None**
   - Quiet Mode: **Yes**
3. **Submit → Apply Config**

### Bridge Profile (default_bridge) の確認
GUI に Internal Sample Rate の設定項目なし。Asterisk CLI で確認:
```
confbridge show profile bridge default_bridge
```
確認済みデフォルト値:
- **Mixing Interval: 20** ✅ (変更不要)
- **Internal Sample Rate: auto** — 全クライアントが Opus 48kHz の場合は実質 48000 で動作するためテスト環境では許容
- **Video Mode: no video** ✅

### Internal Sample Rate を本番で明示的に 48000 に設定する場合
**Admin → Config Edit → confbridge.conf** を開き、`default_bridge` セクションに以下を追加:
```
internal_sample_rate=48000
```
追加後、Asterisk CLI から `module reload app_confbridge.so` を実行して反映。

---

## 5. chan_pjsip NAT 設定

1. **Settings → Asterisk SIP Settings → Chan PJSIP Settings タブ**
2. **「Localnet」** に社内 LAN セグメントを追加 (例: `192.168.11.0/255.255.255.0`)
3. **External Address** は空欄 (社内 LAN 前提)
4. **Submit → Apply Config**

---

## 6. Firewall 設定

1. **Admin → Firewall**
2. 受信許可する送信元を社内セグメントに限定
3. SIP (UDP 5060) および RTP ポート範囲 (UDP 10000-20000) を開放

---

## 動作確認コマンド (SSH / TTY)

```bash
# アクティブチャネル確認
asterisk -rx "pjsip show channels"

# 会議室参加者確認
asterisk -rx "confbridge list"

# コーデック確認
asterisk -rx "core show codecs"

# Extension 登録状態確認
asterisk -rx "pjsip show endpoints"
```

---

## トラブルシューティング

| 症状 | 確認箇所 |
|---|---|
| クライアントが接続できない | Firewall → SIP ポート開放確認 |
| Opus でネゴシエーションされない | General SIP Settings → Codec 順序確認 |
| 会議室に入れない | ConfBridge 設定 → 会議室番号確認 |
| 音が聞こえない | NAT 設定 → Localnet セグメント確認、RTP ポート確認 |
