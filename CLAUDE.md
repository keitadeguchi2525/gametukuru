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
  - `MATS`（素材）、`ENEMIES`（ボス。`makeBoss()` で既存ボスの体を借りた新ボスも作れる）、`MINIONS`（雑魚。`variant()` でボスの色違いを作る）
  - `ENEMY_HP` / `ENEMY_DMG` / `PART_RATIO`（敵の体力・攻撃力・部位体力の割合）
  - `DUNGEONS`（★1〜★5の階層構成。並び順＝解放順、idはセーブに使うので変えない）、`WEAPONS`、`ARMOR_SETS`（防具は頭・胴・手・足の4部位に自動展開）
  - ビルド：装備の `build`（balance/parry/crit/dodge）と `perk`（武器）・`setPerk`/`perk`（防具）。効果の合計は `perk('キー')`。体勢ポイントは `PT`・`parryGain()`・`dodgeGain()`
  - `WEAPON_CLEARS` / `ARMOR_SET_CLEARS` / `SLACK`（素材の必要量はドロップ期待値から自動計算）
  - `SPD` / `IDLE` / `LATE` / `PERFECT` / `DODGEWIN` / `BREATH_WINDUP` など（攻撃スピード・受付時間）
- パリィ判定：`needDir()` が「はじくべき方向」を返す。攻撃が来た方向（`OPP[a.dir]`）へスワイプで成功。呪い中は逆。
- 戦闘の流れ：`beginAttack()` → `update()` の windup → `tryParry()` / `resolveHit()` → `afterResolve()`。
- ガチャ：`rollGacha()`、排出率は `GACHA_ODDS`、討伐の証の出る確率は `PROOF_RATE`（証は `MATS` の `proof_ボスid`）。ボス武器は `WEAPONS` の `gacha:` つき、追加効果は `G_POISON_*` / `G_BOLT_*` / `G_BEAM_*` / `G_FREEZE_SEC` / `cutMul()` と `specialHit()`。
- ボスのギミックは `gimmicks()`、`enterBroken()`、`guardPhase()` あたり。鱗粉の玉は `spawnOrbs()`/`updateOrbs()`、落氷は `spawnIcicle()`/`updateIcicles()`。
- 音は `sfx('名前')`（効果音）と `bgm('曲名')`（BGM）。音の中身は `SFX` と `SONGS` にまとまっている。新しい場面を足したら、合う効果音も鳴らす。
- 背景は `drawBG()`（ダンジョンごとに `bgCave` / `bgForest` / `bgCanyon` / `bgIce` / `bgUnder`、動く部分は `drawBGAnim()`）。

## 守ること
- セーブデータ（localStorage のキー `parryHunter_v1`）の互換性を壊さない。形式を変えるときは `migrate()` に変換処理を足す。
- 変更後はブラウザで開いてエラーが出ないか確認する（`node --check` で script 部分の構文チェックもできる）。
- 仕様（数値・ルール・ギミック）を変えたら `docs/仕様メモ.md` も同じ変更で更新する。
- 大きな更新で仕様メモの番号（v16 など）を上げたら、`index.html` の `VERSION`（画面左下の表記）も同じ番号にする。
- コミットは小さく、メッセージは「何をしたか」を日本語で一言。例：`ゴルムの攻撃を少し遅くした`
- 動かない状態で main にプッシュしない。大きな変更はブランチ＋プルリクエストで。
