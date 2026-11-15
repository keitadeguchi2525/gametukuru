using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// 技データ（1つの技＝1ファイル）。武士の腕・刀・体の動きをキー（時間ごとの姿勢）で持つ。
// 置き場：Assets/IBProto/Motions/モンハン太刀/MH_Tachi/MT_*.asset
// インスペクターで数字を直すと、次にバトルを再生したときからその動きになる。
[CreateAssetMenu(menuName = "IBProto/技データ", fileName = "MT_New")]
public class IBProtoMove : ScriptableObject
{
    public const string DIR = "Assets/IBProto/Motions/モンハン太刀/MH_Tachi";

    [Serializable]
    public struct Key
    {
        [Tooltip("技が始まってからの時間（秒）")] public float t;
        [Tooltip("胸から見た右手の位置（右・上・前、m）")] public Vector3 grip;
        [Tooltip("刃の向き（右・上・前）")] public Vector3 blade;
        [Tooltip("胴のひねり（度）")] public float yaw;
        [Tooltip("前かがみ（度、マイナスでのけぞり）")] public float lean;
        [Tooltip("前後の移動（m、前がプラス）")] public float step;
        [Tooltip("上下の移動（m）")] public float rise;
        [Tooltip("体ごとの回転（度）")] public float turn;
    }

    [Serializable]
    public struct Hit
    {
        [Tooltip("当たる時間（秒）")] public float t;
        [Tooltip("威力（モンハンのモーション値の目安）")] public float mv;
    }

    [Header("説明")]
    public string jpName = "";
    public string kind = "攻撃";
    [Tooltip("このゲームでの入力")] public string input = "";
    [Tooltip("元にした技・メモ")] public string note = "";

    [Header("時間")]
    public float duration = 0.6f;
    [Tooltip("ここから次の技を受け付ける（秒）")] public float chainFrom = 0.35f;
    [Tooltip("無敵の始まり（-1で無し）")] public float invulnFrom = -1f;
    public float invulnTo = -1f;
    [Tooltip("ドラッグの向き（パリィの姿勢にも使う）")] public string swingDir = "";

    [Header("練気")]
    public float spiritCost;
    public float spiritGain = 6f;

    public Hit[] hits = new Hit[0];
    public Key[] keys = new Key[0];

    public char Dir => string.IsNullOrEmpty(swingDir) ? 'D' : swingDir[0];
    public float FirstHit => hits != null && hits.Length > 0 ? hits[0].t : chainFrom;
    public float WindTime => keys != null && keys.Length > 1 ? keys[1].t : 0f;

    // 時間 t の姿勢（キーの間はなめらかにつなぐ）
    public Key Sample(float t)
    {
        if (keys == null || keys.Length == 0) return new Key { blade = Vector3.forward };
        if (t <= keys[0].t) return keys[0];
        for (int i = 0; i < keys.Length - 1; i++)
        {
            var a = keys[i]; var b = keys[i + 1];
            if (t > b.t) continue;
            float k = Mathf.Clamp01((t - a.t) / Mathf.Max(b.t - a.t, 1e-4f));
            k = k * k * (3f - 2f * k);
            return new Key
            {
                t = t,
                grip = Vector3.Lerp(a.grip, b.grip, k),
                blade = Vector3.Slerp(a.blade.normalized, b.blade.normalized, k),
                yaw = Mathf.Lerp(a.yaw, b.yaw, k), lean = Mathf.Lerp(a.lean, b.lean, k),
                step = Mathf.Lerp(a.step, b.step, k), rise = Mathf.Lerp(a.rise, b.rise, k), turn = Mathf.Lerp(a.turn, b.turn, k),
            };
        }
        return keys[keys.Length - 1];
    }

    // ===== 読み込み：技データのファイルがあればそれ、無ければ下の初期値 =====
    public static Dictionary<string, IBProtoMove> LoadAll()
    {
        var d = new Dictionary<string, IBProtoMove>();
#if UNITY_EDITOR
        if (AssetDatabase.IsValidFolder("Assets/IBProto/Motions"))
            foreach (var id in AssetDatabase.FindAssets("t:IBProtoMove", new[] { "Assets/IBProto/Motions" }))
            {
                var m = AssetDatabase.LoadAssetAtPath<IBProtoMove>(AssetDatabase.GUIDToAssetPath(id));
                if (m != null) d[m.name] = m;
            }
#endif
        foreach (var m in Defaults()) if (!d.ContainsKey(m.name)) d[m.name] = m;
        return d;
    }

    // ===== 初期値：モンハンワールドの太刀を参考にした技 =====
    // 当たりの時間は、太刀の講座動画で測った連携のテンポ（通常技1振り約0.6秒）に合わせて決めた目安。
    // 威力（mv）はモンハンワールドのモーション値の目安。
    static Key K(float t, float gx, float gy, float gz, float bx, float by, float bz, float yaw = 0, float lean = 0, float step = 0, float rise = 0, float turn = 0)
    {
        return new Key { t = t, grip = new Vector3(gx, gy, gz), blade = new Vector3(bx, by, bz).normalized, yaw = yaw, lean = lean, step = step, rise = rise, turn = turn };
    }
    static Hit H(float t, float mv) { return new Hit { t = t, mv = mv }; }
    static Key Ready(float t) { return K(t, 0.08f, -0.25f, 0.35f, -0.05f, 0.5f, 0.86f); }

    static IBProtoMove Make(string id, string jp, string kind, string input, string note, string dir, float dur, float chain, Hit[] hits, Key[] keys, float cost = 0, float gain = 6, float invF = -1, float invT = -1)
    {
        var m = CreateInstance<IBProtoMove>();
        m.name = id; m.jpName = jp; m.kind = kind; m.input = input; m.note = note; m.swingDir = dir;
        m.duration = dur; m.chainFrom = chain; m.hits = hits; m.keys = keys;
        m.spiritCost = cost; m.spiritGain = gain; m.invulnFrom = invF; m.invulnTo = invT;
        return m;
    }

    public static List<IBProtoMove> Defaults()
    {
        return new List<IBProtoMove>
        {
            Make("MT_Tate", "縦斬り", "攻撃", "ドラッグ↓", "踏み込み斬り・縦斬り", "D", 0.62f, 0.36f,
                new[] { H(0.27f, 19) },
                new[] { Ready(0), K(0.16f, 0.05f, 0.42f, 0.05f, 0, 0.35f, -0.94f, -5, -8), K(0.27f, 0.02f, 0.05f, 0.55f, 0, 0.1f, 1f, 0, 10, 0.12f),
                        K(0.34f, 0, -0.35f, 0.42f, 0, -0.75f, 0.66f, 0, 14, 0.15f), Ready(0.62f) }),

            Make("MT_Kiriage", "斬り上げ", "攻撃", "ドラッグ↑", "斬り上げ", "U", 0.58f, 0.32f,
                new[] { H(0.22f, 17) },
                new[] { Ready(0), K(0.12f, -0.12f, -0.42f, 0.25f, -0.3f, -0.75f, 0.55f, 15, 8), K(0.22f, 0.05f, 0, 0.55f, 0.1f, 0.6f, 0.8f, 0, 0, 0.08f),
                        K(0.32f, 0.12f, 0.4f, 0.3f, 0.15f, 0.85f, -0.45f, -10, -8), Ready(0.58f) }),

            Make("MT_Kesa", "袈裟斬り", "攻撃", "ドラッグ←", "右上から左下へ（踏み込み斬りの横版）", "L", 0.6f, 0.34f,
                new[] { H(0.25f, 21) },
                new[] { Ready(0), K(0.15f, 0.3f, 0.38f, 0.1f, 0.45f, 0.45f, -0.77f, 30, -5), K(0.25f, 0, 0.05f, 0.55f, -0.45f, -0.05f, 0.89f, 0, 8, 0.1f),
                        K(0.33f, -0.3f, -0.3f, 0.35f, -0.85f, -0.45f, 0.25f, -30, 12, 0.12f), Ready(0.6f) }),

            Make("MT_Gyakukesa", "逆袈裟斬り", "攻撃", "ドラッグ→", "左上から右下へ", "R", 0.6f, 0.34f,
                new[] { H(0.25f, 21) },
                new[] { Ready(0), K(0.15f, -0.25f, 0.38f, 0.1f, -0.45f, 0.45f, -0.77f, -30, -5), K(0.25f, 0, 0.05f, 0.55f, 0.45f, -0.05f, 0.89f, 0, 8, 0.1f),
                        K(0.33f, 0.32f, -0.3f, 0.35f, 0.85f, -0.45f, 0.25f, 30, 12, 0.12f), Ready(0.6f) }),

            Make("MT_Tsuki", "突き", "攻撃", "クリック", "突き（隙が小さい）", "D", 0.45f, 0.24f,
                new[] { H(0.16f, 11) },
                new[] { Ready(0), K(0.09f, 0.05f, -0.08f, 0.12f, 0, 0.05f, 1, 10, -4, -0.05f), K(0.16f, 0.02f, -0.02f, 0.62f, 0, 0.02f, 1, -5, 8, 0.2f),
                        K(0.26f, 0.02f, -0.02f, 0.6f, 0, 0.02f, 1, -5, 8, 0.18f), Ready(0.45f) }, 0, 4),

            Make("MT_Kirisagari", "斬り下がり", "攻撃", "S（どの技の後からでも）", "斬り下がり。後ろへ下がりながら斬る。下がる間は当たらない", "D", 0.7f, 0.4f,
                new[] { H(0.22f, 19) },
                new[] { Ready(0), K(0.1f, 0.05f, 0.4f, 0.1f, 0, 0.4f, -0.9f), K(0.22f, 0, -0.1f, 0.5f, 0, -0.3f, 0.95f, 0, 10, -0.3f),
                        K(0.4f, 0, -0.35f, 0.35f, 0, -0.75f, 0.65f, 0, 5, -0.9f), K(0.7f, 0.08f, -0.25f, 0.35f, -0.05f, 0.5f, 0.86f, 0, 0, -0.2f) }, 0, 6, 0.05f, 0.3f),

            Make("MT_Kijin1", "気刃斬りⅠ", "攻撃", "右クリック", "気刃斬りⅠ（練気を使う）", "L", 0.5f, 0.28f,
                new[] { H(0.18f, 23) },
                new[] { Ready(0), K(0.1f, 0.28f, 0.35f, 0.15f, 0.4f, 0.5f, -0.76f, 25), K(0.18f, 0, 0.05f, 0.58f, -0.4f, 0, 0.92f, 0, 6, 0.15f),
                        K(0.26f, -0.3f, -0.25f, 0.35f, -0.85f, -0.4f, 0.3f, -30, 10, 0.15f), Ready(0.5f) }, 12, 0),

            Make("MT_Kijin2", "気刃斬りⅡ", "攻撃", "右クリック（Ⅰの後）", "気刃斬りⅡ（2回斬る）", "R", 0.62f, 0.44f,
                new[] { H(0.16f, 12), H(0.36f, 14) },
                new[] { Ready(0), K(0.08f, -0.25f, 0.35f, 0.15f, -0.45f, 0.45f, -0.77f, -25), K(0.16f, 0, 0.05f, 0.55f, 0.45f, 0, 0.89f, 0, 6, 0.1f),
                        K(0.24f, 0.35f, 0, 0.3f, 0.9f, 0.1f, 0.35f, 35, 6, 0.1f), K(0.36f, -0.05f, 0.05f, 0.55f, -0.6f, 0.05f, 0.8f, -10, 6, 0.15f),
                        K(0.44f, -0.35f, 0, 0.3f, -0.9f, 0.05f, 0.4f, -35, 6, 0.15f), Ready(0.62f) }, 15, 0),

            Make("MT_Kijin3", "気刃斬りⅢ", "攻撃", "右クリック（Ⅱの後）", "気刃斬りⅢ（3回斬る。最後が重い）", "D", 0.85f, 0.62f,
                new[] { H(0.14f, 11), H(0.32f, 13), H(0.55f, 30) },
                new[] { Ready(0), K(0.07f, 0.3f, 0.3f, 0.15f, 0.45f, 0.45f, -0.77f, 25), K(0.14f, 0, 0, 0.55f, -0.45f, 0, 0.89f, 0, 6, 0.1f),
                        K(0.22f, -0.3f, -0.2f, 0.35f, -0.85f, -0.35f, 0.35f, -30, 8, 0.1f), K(0.32f, 0.05f, 0, 0.55f, 0.5f, 0.1f, 0.86f, 10, 6, 0.12f),
                        K(0.4f, 0.35f, 0.05f, 0.3f, 0.9f, 0.15f, 0.3f, 35, 4, 0.12f), K(0.48f, 0.05f, 0.45f, 0.1f, 0, 0.4f, -0.9f, 0, -10, 0.1f),
                        K(0.55f, 0, 0, 0.6f, 0, 0.05f, 1, 0, 12, 0.2f), K(0.63f, 0, -0.4f, 0.4f, 0, -0.8f, 0.6f, 0, 16, 0.2f), Ready(0.85f) }, 20, 0),

            Make("MT_Daikaiten", "気刃大回転斬り", "攻撃", "右クリック（Ⅲの後／見切り成功の後）", "気刃大回転斬り。当たると練気の色が1段上がる", "R", 1.15f, 0.85f,
                new[] { H(0.42f, 35) },
                new[] { Ready(0), K(0.28f, -0.3f, 0.05f, 0.25f, -0.95f, 0.1f, -0.3f, -40, 0, 0, 0, -40), K(0.42f, 0, 0.05f, 0.6f, 0.2f, 0.05f, 0.98f, 0, 6, 0.1f),
                        K(0.6f, 0.35f, 0, 0.35f, 0.9f, 0, 0.4f, 20, 6, 0.1f, 0, 180), K(0.8f, 0.3f, -0.15f, 0.35f, 0.85f, -0.3f, 0.4f, 20, 8, 0.1f, 0, 340),
                        K(1.15f, 0.08f, -0.25f, 0.35f, -0.05f, 0.5f, 0.86f, 0, 0, 0, 0, 360) }, 25, 0),

            Make("MT_Mikiri", "見切り斬り", "防御", "Space（どの技の後からでも）", "見切り斬り。練気を全部使い、下がる間に攻撃を受けると成功（練気が満タン）", "D", 0.85f, 0.6f,
                new[] { H(0.48f, 25) },
                new[] { Ready(0), K(0.1f, 0.1f, 0.1f, 0.25f, 0.1f, 0.6f, 0.79f, 0, -6, -0.6f), K(0.32f, 0.05f, 0.42f, 0.05f, 0, 0.35f, -0.94f, 0, -8, -0.55f),
                        K(0.48f, 0, 0, 0.6f, 0, 0.05f, 1, 0, 12, 0), K(0.56f, 0, -0.35f, 0.4f, 0, -0.8f, 0.6f, 0, 14, 0.05f), Ready(0.85f) }, 0, 0, 0f, 0.32f),

            Make("MT_Kabuto", "気刃兜割り", "奥義", "E（ウルト。練気の色を1段使う）", "気刃突きから跳んで真下へ斬り下ろす", "D", 1.5f, 1.2f,
                new[] { H(0.62f, 30), H(0.72f, 30), H(0.82f, 40) },
                new[] { Ready(0), K(0.12f, 0.05f, -0.1f, 0.35f, 0, 0.6f, 0.8f, 0, 6, 0, -0.12f), K(0.45f, 0.05f, 0.5f, 0, 0, 0.2f, -0.98f, 0, -12, 0.5f, 0.9f),
                        K(0.62f, 0, 0.1f, 0.55f, 0, -0.2f, 0.98f, 0, 15, 0.8f, 0.55f), K(0.82f, 0, -0.35f, 0.45f, 0, -0.85f, 0.5f, 0, 25, 0.9f, 0),
                        K(1.1f, 0, -0.35f, 0.45f, 0, -0.85f, 0.5f, 0, 22, 0.9f, -0.1f), Ready(1.5f) }, 0, 0, 0.1f, 0.85f),

            Make("MT_Kaeshi", "返し斬り", "攻撃", "受け流しの直後にドラッグ", "受け流しからの派生（試作）。敵の連撃中でも斬れる", "R", 0.5f, 0.3f,
                new[] { H(0.14f, 30) },
                new[] { Ready(0), K(0.06f, -0.25f, -0.3f, 0.3f, -0.7f, -0.6f, 0.4f, -25), K(0.14f, 0.05f, 0.05f, 0.58f, 0.3f, 0.5f, 0.81f, 0, 4, 0.2f),
                        K(0.24f, 0.3f, 0.35f, 0.3f, 0.6f, 0.75f, -0.25f, 25, -4, 0.2f), Ready(0.5f) }, 0, 10),
        };
    }
}
