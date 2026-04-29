# FreePBX セットアップ手順書

対象: FreePBX 17 (OSO) + Asterisk 22 + Debian 12

---

## 1. インストール

### VM スペック (本番)

| 項目 | 値 |
|---|---|
| vCPU | 2〜4 |
| RAM | 4 GB |
| ディスク | 100 GB |
| NIC | 100Mbps |

### インストーラー選択

- FreePBX 17 Distro ISO を起動
- インストーラーで以下を選択:
  - **OSO** (Open Source Only) — 商用 Sangoma モジュール不要のため
  - **Asterisk 22** (LTS、codec_opus.so 標準同梱)

### インストール完了後

- ブラウザで `http://<IP>/` を開く → FreePBX ダッシュボード
- 管理者アカウント (admin) の初回設定を完了させる

### 匿名ブラウザ統計の無効化 (企業内利用時は必須)

FreePBX は既定で Google Analytics による匿名統計を収集します。社内環境では無効化してください。

1. **Settings → Advanced Settings**
2. **「Browser Stats」** を **「No」** に変更
3. **「Submit」** → **「Apply Config」**

---

## 2. Opus コーデック有効化

1. **Settings → Asterisk SIP Settings**
2. **「General SIP Settings」タブ** を選択
3. コーデック一覧から **「opus」** にチェックを入れ、リストの**一番上**に移動
4. **「Submit」** → 右上の **「Apply Config」** (オレンジ色のボタン)

---

## 3. ConfBridge 会議室 8000 の設定

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

`default_bridge` の実測値 (Asterisk CLI `confbridge show profile bridge default_bridge`):

```
Name:                 default_bridge
Internal Sample Rate: auto
Mixing Interval:      20        ← LoopcastUA の 20ms フレームと一致 ✅
Record Conference:    no
Max Members:          No Limit
Video Mode:           no video  ✅
```

- **Mixing Interval: 20** — LoopcastUA のフレーム長と一致しており変更不要
- **Internal Sample Rate: auto** — 全クライアントが Opus 48kHz で接続するため実質 48kHz で動作、変更不要
- **Video Mode: no video** — 音声のみのため変更不要

### Internal Sample Rate を明示的に 48000 に固定する場合 (任意)

**Admin → Config Edit → confbridge.conf** を開き、`[default_bridge]` セクションに以下を追加:

```ini
internal_sample_rate=48000
```

追加後、**Admin → Asterisk CLI** から反映:

```
module reload app_confbridge.so
```

---

## 4. Extension 作成

### 単体登録

1. **Connectivity → Extensions → Add Extension → Add New SIP [chan_pjsip] Extension**
2. 設定値:
   - User Extension: `9001` (9001〜9040)
   - Display Name: `NMS-PC-A` (PC 識別名)
   - Secret: ランダム 16 文字以上のパスワード
3. **Submit → Apply Config**

### 一括登録 (40台展開時)

1. **Admin → Bulk Handler**
2. **Import → Extensions** で CSV アップロード
3. CSV フォーマット:

```
extension,name,secret,tech
9001,NMS-PC-A,<password>,pjsip
9002,NMS-PC-B,<password>,pjsip
...
```

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

## 7. 長期運用設定

### Asterisk ログレベルの絞り込み

デフォルトでは `full` ログに DEBUG・VERBOSE が含まれ、常時接続環境では大量のログが蓄積します。

1. **Settings → Asterisk Log File Settings**
2. `full` の **Debug** と **Verbose** のチェックを外す
3. **Save → Apply Config**

確認 (**Admin → Asterisk CLI**):

```
logger show channels
```

`/var/log/asterisk/full` が `NOTICE WARNING ERROR` のみになっていること。

> **注意:** FreePBX アップデート後にログレベルが元に戻る場合があります。アップデート後は上記コマンドで確認してください。

### MariaDB バイナリログの保持期間設定

バイナリログが無期限に蓄積するのを防ぎます。

```bash
sudo mysql -u root -e "SHOW VARIABLES LIKE 'expire_logs_days'; SHOW VARIABLES LIKE 'binlog_expire_logs_seconds';"
```

FreePBX 17 のインストーラーは既定で **10 日 (864000 秒)** を設定します。値が 0 の場合のみ `/etc/mysql/mariadb.conf.d/50-server.cnf` の `[mysqld]` セクションに追加して `sudo systemctl restart mariadb` で反映してください:

```ini
expire_logs_days = 7
```

### ディスク使用量の定期確認

Zabbix agent 等の監視ツールで以下のパスを対象に定期監視することを推奨します。

| 監視対象 | パス |
|---|---|
| ルートファイルシステム使用率 | `/` |
| Asterisk ログ | `/var/log/asterisk/` |
| MariaDB データ | `/var/lib/mysql/` |

---

## 動作確認コマンド (Admin → Asterisk CLI)

```
# アクティブチャネル確認
pjsip show channels

# 会議室参加者確認
confbridge list

# 会議室参加者の詳細 (発言状態含む)
confbridge list 8000

# コーデック確認
core show codecs

# Extension 登録状態確認
pjsip show endpoints

# Bridge Profile 確認
confbridge show profile bridge default_bridge
```

---

## トラブルシューティング

| 症状 | 確認箇所 |
|---|---|
| クライアントが接続できない | Firewall → SIP ポート開放確認 |
| Opus でネゴシエーションされない | General SIP Settings → Codec 順序確認 |
| 会議室に入れない | ConfBridge 設定 → 会議室番号確認 |
| 音が聞こえない | NAT 設定 → Localnet セグメント確認、RTP ポート確認 |
