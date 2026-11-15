# CLAUDE.md — パリィ・ハンター

このリポジトリは2人（keitadeguchi2525 と 大平君）で作っているブラウザゲーム。どちらの Claude Code もこのファイルに従って作業する。

## 基本
- ユーザーへの返答・コメント・コミットメッセージはすべて日本語。
- 相手は開発初心者のこともある。専門用語は短く言い換えて説明する。
- 変更する前に「何をどう変えるか」を一言伝えてから作業する。

## コードの構成
- ゲームは `index.html` の1ファイル（HTML/CSS/JavaScript）。ビルド不要、ブラウザで直接開いて動く。
- 外部ライブラリは使わない（Google Fonts のみ）。Canvas 2D で描画している。
- 数値・データは `<script>` の前半にまとまっている。バランス調整はまずここを直す。
  - `MATS`（素材）、`ENEMIES`（ボス）、`MINIONS`（雑魚。`variant()` でボスの色違いを作る）
  - `ENEMY_HP` / `ENEMY_DMG` / `PART_RATIO`（敵の体力・攻撃力・部位体力の割合）
  - `DUNGEONS`（階層構成）、`WEAPONS`、`ARMOR_SETS`（防具は頭・胴・手・足の4部位に自動展開）
  - `WEAPON_CLEARS` / `ARMOR_SET_CLEARS` / `SLACK`（素材の必要量はドロップ期待値から自動計算）
  - `SPD` / `IDLE` / `LATE` / `PERFECT` / `DODGEWIN` / `BREATH_WINDUP` など（攻撃スピード・受付時間）
- パリィ判定：`needDir()` が「はじくべき方向」を返す。攻撃が来た方向（`OPP[a.dir]`）へスワイプで成功。呪い中は逆。
- 戦闘の流れ：`beginAttack()` → `update()` の windup → `tryParry()` / `resolveHit()` → `afterResolve()`。
- ボスのギミックは `gimmicks()`、`enterBroken()`、`guardPhase()` あたり。
- 音は `sfx('名前')`（効果音）と `bgm('曲名')`（BGM）。音の中身は `SFX` と `SONGS` にまとまっている。新しい場面を足したら、合う効果音も鳴らす。
- 背景は `drawBG()`（ダンジョンごとに `bgCave` / `bgCanyon` / `bgUnder`、動く部分は `drawBGAnim()`）。

## 守ること
- セーブデータ（localStorage のキー `parryHunter_v1`）の互換性を壊さない。形式を変えるときは `migrate()` に変換処理を足す。
- 変更後はブラウザで開いてエラーが出ないか確認する（`node --check` で script 部分の構文チェックもできる）。
- 仕様（数値・ルール・ギミック）を変えたら `docs/仕様メモ.md` も同じ変更で更新する。
- コミットは小さく、メッセージは「何をしたか」を日本語で一言。例：`ゴルムの攻撃を少し遅くした`
- 動かない状態で main にプッシュしない。大きな変更はブランチ＋プルリクエストで。
