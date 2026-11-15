# パリィ・ハンター

敵の攻撃を「来た方向へはじき返す」パリィで体勢を崩し、部位を破壊して素材を集め、装備を作って強くなるブラウザゲーム。
2人（keitadeguchi2525 ＋ 大平君）で共同開発中。

## 遊ぶ

- ブラウザで遊ぶ：https://keitadeguchi2525.github.io/gametukuru/ （GitHub Pages をオンにすると使えます）
- 手元で遊ぶ：`index.html` をダブルクリックしてブラウザで開くだけ

### 操作

| 操作 | スマホ | PC |
| --- | --- | --- |
| パリィ | 攻撃が来た方向へスワイプ | マウスでドラッグ／矢印・WASD（斜めは Q E Z C） |
| 回避 | 右下の「回避」ボタン | Shift |
| 攻撃（体勢崩し中） | 敵の部位を押したままこする | マウスで押したままこする |

## フォルダ構成

```
gametukuru/
├─ index.html   ← ゲーム本体（HTML/CSS/JavaScript が1ファイルに入っている）
├─ docs/        ← 仕様メモ
├─ CLAUDE.md    ← Claude Code が作業前に読む説明書
└─ README.md    ← このファイル
```

## 開発のしかた

1. 作業前に GitHub Desktop で「Fetch origin」→「Pull origin」
2. 自分で直すか、このフォルダで Claude Code を起動して日本語で頼む
3. ブラウザで `index.html` を開いて遊んで確かめる
4. GitHub Desktop で何をしたか一言書いて「Commit」→「Push origin」

大きな変更（新しいボス・新システムなど）はブランチを切ってプルリクエストにし、相手に遊んで確かめてもらってから main に入れる。
やりたいこと・バグ・アイデアは Issues に書く。
