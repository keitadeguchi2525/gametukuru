IB風ゲーム 試作01（Unity版）

■ 入れ方
1. このフォルダ「IBProto」ごと、Unityプロジェクトの Assets フォルダに入れる
2. File > New Scene で新しいシーンを作る（Main Camera と Directional Light が入った状態でOK）
3. Hierarchy で右クリック > Create Empty。名前は何でもいい（例：Game）
4. その空のオブジェクトを選んで、Inspector の Add Component から「ProtoGame」を追加
5. Play を押す

モデルや音の素材は不要。地形・敵・剣・効果音はすべてスクリプトが作る。

■ 操作
・マウスでドラッグ：斬る。敵の攻撃中は画面中央の矢印の方向へドラッグでパリィ
・矢印が白く光った瞬間がパリィのタイミング
・A / D：回避　S か右クリック長押し：ガード（盾は4回まで）

■ 調整（ProtoGame の Inspector）
・Parry Window：パリィの受付時間（秒）。大きいほど簡単
・Enemy Max Hp / Player Max Hp / Max Shield
・Rare Drop Rate：紅玉が落ちる確率

■ うまくいかないとき
・日本語が□になる → 日本語フォント（.ttf / .otf）を Assets に入れて、ProtoGame の「Ui Font」に入れる
・何も映らない → シーンに Main Camera があるか確認（なければ自動で作る）
・旧Input Manager／新Input System どちらでも動く（Project Settings > Player > Active Input Handling）
・Built-in／URP どちらでも動く
