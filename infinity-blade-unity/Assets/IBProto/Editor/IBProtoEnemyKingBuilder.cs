using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;
using K = IBProtoSamuraiBuilder;

// メニュー「IBProto > 敵キャラを生成（王）」
// 墜落した星を治める「王」型の敵。主人公よりひとまわり大きい重装の騎士。
// ・象牙色の装甲、緑青の青銅、黒曜の下地、琥珀に光る目と胸の核
// ・頭の後ろに浮く欠けた光輪の冠、横一文字に光る面、積み重なった巨大な肩当て
// ・地面に突き立てた大剣
// 生成物は Assets/IBProto/Characters/EnemyKing に保存。キャラ確認シーンで武士と並べて大きさを比べられる。
public static class IBProtoEnemyKingBuilder
{
    const string MODEL_PATH = "Assets/StarterAssets/ThirdPersonController/Character/Models/Armature.fbx";
    const string DIR = "Assets/IBProto/Characters/EnemyKing";
    const string MESH_DIR = DIR + "/Meshes", MAT_DIR = DIR + "/Materials";
    const string NAME = "EnemyKing";
    const float SIZE = 1.22f;            // 主人公に対する大きさ
    const float BODY_QUALITY = 0.3f;

    static Transform root;
    static Animator anim;
    static int armorTris;

    [MenuItem("IBProto/旧・生成ツール（作り直し用）/敵キャラを生成（王）")]
    public static void Build()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(MODEL_PATH);
        if (model == null) { Debug.LogError("[IBProto] 素体が見つかりません: " + MODEL_PATH); return; }
        foreach (var d in new[] { DIR, MESH_DIR, MAT_DIR }) K.MakeFolder(d);

        var old = GameObject.Find(NAME); if (old) Object.DestroyImmediate(old);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        go.name = NAME;
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        go.transform.localScale = Vector3.one;
        root = go.transform;
        anim = go.GetComponent<Animator>();
        if (anim == null || !anim.isHuman) { Debug.LogError("[IBProto] 素体が Humanoid ではありません"); return; }
        armorTris = 0;

        // ---- マテリアル ----
        var zouge = Mat("K_Zouge", new Color(0.58f, 0.55f, 0.48f), 0.15f, 0.42f);     // 象牙色の装甲
        var zougeD = Mat("K_ZougeKage", new Color(0.38f, 0.36f, 0.31f), 0.2f, 0.38f);   // 影側の装甲
        var kokuyo = Mat("K_Kokuyou", new Color(0.045f, 0.045f, 0.055f), 0.65f, 0.72f); // 黒曜の下地
        var roku = Mat("K_Rokushou", new Color(0.2f, 0.4f, 0.36f), 0.85f, 0.42f);      // 緑青の青銅
        var nuno = Mat("K_Nuno", new Color(0.15f, 0.045f, 0.11f), 0f, 0.25f);         // 葡萄色の布
        var suit = Mat("K_Shita", new Color(0.06f, 0.06f, 0.07f), 0.1f, 0.3f);         // 素体（下地の布）
        var hikari = Mat("K_Hikari", new Color(1f, 0.6f, 0.2f), 0f, 0.9f, new Color(1f, 0.52f, 0.12f) * 4f); // 琥珀の光

        // ---- 素体 ----
        int bodyBefore = 0, bodyAfter = 0;
        foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (r.sharedMesh == null) continue;
            bodyBefore += r.sharedMesh.triangles.Length / 3;
            var simp = K.Simplify(r.sharedMesh, BODY_QUALITY);
            if (simp != null)
            {
                simp.name = "King_Body_simplified";
                string p = MESH_DIR + "/" + simp.name + ".asset";
                AssetDatabase.DeleteAsset(p); AssetDatabase.CreateAsset(simp, p);
                r.sharedMesh = simp;
            }
            bodyAfter += r.sharedMesh.triangles.Length / 3;
            r.sharedMaterials = r.sharedMaterials.Select(_ => suit).ToArray();
        }

        var head = Bone(HumanBodyBones.Head);
        var neck = Bone(HumanBodyBones.Neck);
        var hips = Bone(HumanBodyBones.Hips);
        var chest = Bone(HumanBodyBones.UpperChest) ?? Bone(HumanBodyBones.Chest) ?? Bone(HumanBodyBones.Spine);
        Quaternion R = Quaternion.identity;
        float D2R = Mathf.Deg2Rad;

        // ================= 兜：縦に長い卵形の面なし兜 =================
        Vector3 hc = head.position + new Vector3(0, 0.055f, 0.005f);
        Func<float, float, Vector3> helm = (a, ph) =>
        {
            float y = -Mathf.Cos(ph), r = Mathf.Sin(ph);
            y = y < 0 ? y * 0.115f : y * 0.17f;                      // 上に少し長く
            float rr = 0.122f * r * (1f + 0.08f * Mathf.Max(0, Mathf.Cos(a)));  // 前を少し張らせる
            return new Vector3(Mathf.Sin(a) * rr, y, Mathf.Cos(a) * rr * 1.12f);
        };
        Add("Helm", K.Shell(48, 16, (u, v) => helm(u * Mathf.PI * 2f + Mathf.PI, Mathf.Lerp(32f, 179f, v) * D2R), 0.007f, true), kokuyo, head, hc, R);
        // 面：骨のような仮面。目の切れ目から下へ尖っていく
        Add("Helm_Mask", K.Shell(28, 12, (u, v) =>
        {
            float x = Mathf.Lerp(-1f, 1f, u);
            float top = 118f, bottom = Mathf.Lerp(52f, 34f, 1f - Mathf.Abs(x));        // 中央ほど下へ伸びて尖る
            float phd = Mathf.Lerp(bottom, top, v);
            float a = x * Mathf.Lerp(75f, 30f, Mathf.InverseLerp(top, 34f, phd)) * D2R;
            float ph = phd * D2R;
            return helm(a, ph) * 1.045f + new Vector3(0, 0, 0.004f);
        }, 0.008f), zouge, head, hc, R);
        // 頬から顎へ下がる牙状の板
        foreach (int s in new[] { -1, 1 })
            Add("Helm_Fang" + (s > 0 ? "R" : "L"), K.Shell(4, 8, (u, v) =>
            {
                float a = s * Mathf.Lerp(48f, 64f, u) * D2R, ph = Mathf.Lerp(80f, 30f, v) * D2R;
                return helm(a, ph) * (1.08f + v * 0.06f) + new Vector3(0, -v * 0.03f, v * 0.01f);
            }, 0.007f), zougeD, head, hc, R);
        // 目：横一文字の光る切れ目
        Add("Helm_Eye", K.Shell(16, 1, (u, v) =>
        {
            float a = Mathf.Lerp(-42f, 42f, u) * D2R, ph = Mathf.Lerp(97f, 101f, v) * D2R;
            return helm(a, ph) * 1.075f;
        }, 0.004f), hikari, head, hc, R);
        // 目の上の黒い庇
        Add("Helm_Brow", K.Shell(16, 2, (u, v) =>
        {
            float a = Mathf.Lerp(-58f, 58f, u) * D2R, ph = Mathf.Lerp(103f, 114f, v) * D2R;
            return helm(a, ph) * (1.07f + 0.035f * v);
        }, 0.006f), kokuyo, head, hc, R);
        // 頭頂から後ろへ流れる鰭（ひれ）
        Add("Helm_Fin", K.Shell(20, 2, (u, v) =>
        {
            float ph = Mathf.Lerp(150f, 179f, Mathf.Min(1, u * 1.6f)) * D2R;
            float a = u < 0.62f ? 0f : Mathf.PI;
            if (u >= 0.62f) ph = Mathf.Lerp(179f, 120f, (u - 0.62f) / 0.38f) * D2R;
            Vector3 p = helm(a, ph);
            return new Vector3(0, p.y, p.z) * (1f + v * (0.18f + Mathf.Sin(u * Mathf.PI) * 0.22f));
        }, 0.008f), roku, head, hc, R);

        // ---- 光輪の冠：頭の後ろに浮く、下の欠けた輪と放射状の棘 ----
        Vector3 haloC = hc + new Vector3(0, 0.11f, -0.17f);
        const float HALO = 0.24f;
        Func<float, Vector3> ring = t => { float a = Mathf.Lerp(28f, 332f, t) * D2R; return haloC + new Vector3(Mathf.Sin(a) * HALO, Mathf.Cos(a) * HALO, 0); };
        Add("Halo", K.Tube(10, 64, ring, t => new Vector2(0.011f, Mathf.Lerp(0.008f, 0.02f, Mathf.Sin(t * Mathf.PI)))), roku, head, Vector3.zero, R, worldSpace: true);
        Add("Halo_Inner", K.Tube(8, 64, t => { float a = Mathf.Lerp(40f, 320f, t) * D2R; return haloC + new Vector3(Mathf.Sin(a) * HALO * 0.86f, Mathf.Cos(a) * HALO * 0.86f, 0.005f); },
            t => new Vector2(0.004f, 0.004f)), hikari, head, Vector3.zero, R, worldSpace: true);
        for (int i = 0; i < 9; i++)
        {
            float a = Mathf.Lerp(60f, 300f, i / 8f) * D2R;
            float len = 0.07f + 0.08f * Mathf.Pow(Mathf.Cos((a - Mathf.PI) * 0.5f), 2f);   // 真上ほど長い
            Vector3 dir = new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0);
            Vector3 b0 = haloC + dir * (HALO + 0.008f);
            Add("Halo_Spike" + i, K.Tube(6, 4, t => b0 + dir * (t * len), t => Vector2.one * Mathf.Lerp(0.011f, 0.0015f, t)), zouge, head, Vector3.zero, R, worldSpace: true);
        }
        foreach (int s in new[] { -1, 1 })
        {
            Vector3 a0 = hc + new Vector3(s * 0.05f, 0.06f, -0.12f), a1 = haloC + new Vector3(s * HALO * 0.62f, -HALO * 0.62f, 0);
            Add("Halo_Strut" + (s > 0 ? "R" : "L"), K.Tube(6, 6, t => Vector3.Lerp(a0, a1, t) + new Vector3(0, Mathf.Sin(t * Mathf.PI) * 0.02f, 0), t => Vector2.one * 0.007f), roku, head, Vector3.zero, R, worldSpace: true);
        }

        // ================= 首：高い立襟 =================
        Vector3 nc = neck.position;
        Add("Gorget", K.Shell(36, 4, (u, v) =>
        {
            float a = u * Mathf.PI * 2f + Mathf.PI, r = Mathf.Lerp(0.105f, 0.085f, v) + (Mathf.Cos(a) < 0 ? 0.015f * v : 0f);
            return new Vector3(Mathf.Sin(a) * r, Mathf.Lerp(-0.07f, 0.06f, v) + Mathf.Max(0, -Mathf.Cos(a)) * 0.04f * v, Mathf.Cos(a) * r * 1.05f);
        }, 0.007f, true), kokuyo, neck, nc, R);

        // ================= 胴：竜骨のある樽型の胸甲 =================
        float waistY = hips.position.y + 0.11f, chestTopY = neck.position.y - 0.05f;
        Func<float, float, Vector2> torso = (t, a) =>
        {
            float k = Mathf.SmoothStep(0f, 1f, t * 1.1f);
            float keel = Mathf.Exp(-a * a * 6f) * 0.025f * Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI * 0.9f);   // 正面の稜線
            return new Vector2(Mathf.Lerp(0.155f, 0.205f, k), Mathf.Lerp(0.12f, 0.15f, k) + keel);
        };
        Vector3 dc = new Vector3(0, 0, 0.012f);
        Add("Cuirass", K.Shell(56, 14, (u, v) =>
        {
            float a = u * Mathf.PI * 2f + Mathf.PI, aa = Mathf.DeltaAngle(0, a * Mathf.Rad2Deg) * D2R;
            var rr = torso(v, aa);
            return new Vector3(Mathf.Sin(a) * rr.x, Mathf.Lerp(waistY, chestTopY, v), Mathf.Cos(a) * rr.y) + dc;
        }, 0.01f, true), zouge, chest, Vector3.zero, R, worldSpace: true);
        // 胸の上縁と、脇の黒い切り返し
        Add("Cuirass_Rim", K.Shell(56, 1, (u, v) =>
        {
            float a = u * Mathf.PI * 2f + Mathf.PI, aa = Mathf.DeltaAngle(0, a * Mathf.Rad2Deg) * D2R;
            var rr = torso(1f, aa) + Vector2.one * 0.006f;
            return new Vector3(Mathf.Sin(a) * rr.x, chestTopY + 0.005f - v * 0.02f, Mathf.Cos(a) * rr.y) + dc;
        }, 0.005f, true), roku, chest, Vector3.zero, R, worldSpace: true);
        foreach (int s in new[] { -1, 1 })
        {
            Add("Cuirass_Side" + (s > 0 ? "R" : "L"), K.Shell(6, 10, (u, v) =>
            {
                float a = (s * 90f + Mathf.Lerp(-16f, 16f, u)) * D2R;
                var rr = torso(v * 0.9f, a) + Vector2.one * 0.004f;
                return new Vector3(Mathf.Sin(a) * rr.x, Mathf.Lerp(waistY + 0.02f, chestTopY - 0.06f, v), Mathf.Cos(a) * rr.y) + dc;
            }, 0.004f), kokuyo, chest, Vector3.zero, R, worldSpace: true);
        }
        // 胸の核（光る宝玉と、それを囲む青銅の枠）
        float coreY = Mathf.Lerp(waistY, chestTopY, 0.62f);
        Vector3 coreP = new Vector3(0, coreY, torso(0.62f, 0f).y + 0.012f) + dc;
        Add("Core", K.Tube(16, 3, t => coreP + new Vector3(0, 0, t * 0.022f), t => Vector2.one * Mathf.Lerp(0.03f, 0.022f, t)), hikari, chest, Vector3.zero, R, worldSpace: true);
        Add("Core_Frame", K.Tube(8, 40, t => { float a = t * Mathf.PI * 2f; return coreP + new Vector3(Mathf.Sin(a) * 0.042f, Mathf.Cos(a) * 0.05f, 0.012f); }, t => Vector2.one * 0.008f), roku, chest, Vector3.zero, R, worldSpace: true);
        // 背中の背骨状の板
        for (int i = 0; i < 5; i++)
        {
            float v0 = 0.12f + i * 0.17f;
            Add("Spine" + i, K.Shell(8, 2, (u, v) =>
            {
                float a = (180f + Mathf.Lerp(-22f, 22f, u)) * D2R;
                var rr = torso(v0 + v * 0.1f, a) + Vector2.one * (0.008f + v * 0.012f);
                return new Vector3(Mathf.Sin(a) * rr.x, Mathf.Lerp(waistY, chestTopY, v0) + v * 0.07f, Mathf.Cos(a) * rr.y) + dc;
            }, 0.006f), i % 2 == 0 ? roku : zougeD, chest, Vector3.zero, R, worldSpace: true);
        }

        // ================= 腰：段の帯と、前後に垂れる布 =================
        for (int i = 0; i < 3; i++)
        {
            float yT = waistY + 0.01f - i * 0.05f, r0 = 0.16f + i * 0.012f;
            Add("Fauld" + i, K.Shell(48, 2, (u, v) =>
            {
                float a = u * Mathf.PI * 2f + Mathf.PI, r = r0 + v * 0.012f;
                return new Vector3(Mathf.Sin(a) * r, yT - v * 0.055f, Mathf.Cos(a) * r * 0.8f) + dc;
            }, 0.007f, true), i == 0 ? roku : kokuyo, hips, Vector3.zero, R, worldSpace: true);
        }
        float kneeY = Bone(HumanBodyBones.LeftLowerLeg).position.y;
        foreach (int f in new[] { 1, -1 })
        {
            float z0 = f > 0 ? 0.15f : -0.18f;
            Add("Tabard" + (f > 0 ? "F" : "B"), K.Shell(6, 14, (u, v) =>
            {
                float x = (u - 0.5f) * Mathf.Lerp(0.2f, 0.25f, v);
                float y = Mathf.Lerp(waistY - 0.02f, kneeY - 0.08f, v);
                return new Vector3(x, y, z0 + f * (0.02f * v + Mathf.Sin(v * 2.5f) * 0.02f) - f * x * x * 0.8f) + dc;
            }, 0.004f), nuno, hips, Vector3.zero, R, worldSpace: true);
            foreach (int s in new[] { -1, 1 })
                Add("TabardTrim" + (f > 0 ? "F" : "B") + (s > 0 ? "R" : "L"), K.Shell(1, 14, (u, v) =>
                {
                    float w = Mathf.Lerp(0.2f, 0.25f, v); float x = s * (w * 0.5f - 0.012f + u * 0.012f);
                    float y = Mathf.Lerp(waistY - 0.02f, kneeY - 0.08f, v);
                    return new Vector3(x, y, z0 + f * (0.02f * v + Mathf.Sin(v * 2.5f) * 0.02f + 0.003f) - f * x * x * 0.8f) + dc;
                }, 0.005f), roku, hips, Vector3.zero, R, worldSpace: true);
        }

        // ================= 腕 =================
        foreach (int s in new[] { -1, 1 })
        {
            string side = s > 0 ? "R" : "L";
            var ua = Bone(s < 0 ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            var la = Bone(s < 0 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            var hand = Bone(s < 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            Vector3 Dd = (la.position - ua.position).normalized;
            Vector3 outv = Vector3.ProjectOnPlane(root.right * s, Dd);
            if (outv.sqrMagnitude < 0.01f) outv = Vector3.ProjectOnPlane(root.up, Dd);
            outv.Normalize();
            var rot = Quaternion.LookRotation(outv, -Dd);   // +Z が腕の外、-Y が肘へ

            // 肩当て：大きなドームの上に、外へ張り出す板を4段
            Add("Pauldron" + side, K.Shell(20, 7, (u, v) =>
            {
                float a = Mathf.Lerp(-115f, 115f, u) * D2R, el = Mathf.Lerp(82f, 0f, v) * D2R, r = 0.17f;
                return new Vector3(Mathf.Sin(a) * r * Mathf.Cos(el), 0.07f + Mathf.Sin(el) * 0.08f - v * 0.03f, Mathf.Cos(a) * r * Mathf.Cos(el) - 0.02f);
            }, 0.009f), zouge, ua, ua.position, rot);
            for (int i = 0; i < 3; i++)
            {
                float y0 = 0.03f - i * 0.06f, r0 = 0.17f + i * 0.018f;
                Add("PauldronLame" + side + i, K.Shell(18, 3, (u, v) =>
                {
                    float a = Mathf.Lerp(-100f, 100f, u) * D2R, r = r0 + v * 0.03f;
                    float flare = Mathf.Pow(Mathf.Abs(Mathf.Sin(a)), 3f) * 0.02f;   // 前後の端を跳ね上げる
                    return new Vector3(Mathf.Sin(a) * r, y0 - v * 0.065f + flare, Mathf.Cos(a) * r - 0.02f);
                }, 0.007f), i == 2 ? roku : (i == 0 ? zougeD : kokuyo), ua, ua.position, rot);
            }
            // 肩の上に立つ2本の角
            for (int h = 0; h < 2; h++)
            {
                Vector3 b0 = ua.position + rot * new Vector3((h == 0 ? -0.05f : 0.06f), 0.06f, 0.15f);
                Vector3 dir = (root.up * 1f + root.right * s * 0.55f + root.forward * (h == 0 ? 0.25f : -0.3f)).normalized;
                float len = h == 0 ? 0.2f : 0.15f;
                Add("PauldronHorn" + side + h, K.Tube(8, 8, t => b0 + dir * (t * len) + root.right * s * (t * t * 0.04f), t => Vector2.one * Mathf.Lerp(0.024f, 0.002f, t)), zouge, ua, Vector3.zero, R, worldSpace: true);
            }

            // 二の腕（黒曜の筒）
            float ulen = (la.position - ua.position).magnitude;
            Add("UpperArm" + side, K.Shell(24, 6, (u, v) =>
            {
                float a = u * Mathf.PI * 2f + Mathf.PI, r = Mathf.Lerp(0.07f, 0.064f, v) + Mathf.Sin(v * Mathf.PI) * 0.008f;
                return new Vector3(Mathf.Sin(a) * r, -Mathf.Lerp(0.02f, ulen, v), Mathf.Cos(a) * r);
            }, 0.006f, true), kokuyo, ua, ua.position, rot);
            // 肘当て
            Add("Elbow" + side, K.Shell(14, 5, (u, v) =>
            {
                float a = Mathf.Lerp(-90f, 90f, u) * D2R, r = 0.078f + Mathf.Sin(v * Mathf.PI) * 0.016f;
                return new Vector3(Mathf.Sin(a) * r, -Mathf.Lerp(ulen - 0.06f, ulen + 0.05f, v), Mathf.Cos(a) * r);
            }, 0.007f), zouge, ua, ua.position, Quaternion.LookRotation(-Vector3.ProjectOnPlane(root.forward, Dd).normalized, -Dd));

            // 前腕：太い籠手と緑青の袖口
            Vector3 Fd = hand.position - la.position; float flen = Fd.magnitude; Fd /= flen;
            var fp = Vector3.ProjectOnPlane(root.forward, Fd);
            var frot = Quaternion.LookRotation(fp.sqrMagnitude > 0.01f ? fp.normalized : root.up, -Fd);
            Add("Vambrace" + side, K.Shell(28, 6, (u, v) =>
            {
                float a = u * Mathf.PI * 2f + Mathf.PI, r = Mathf.Lerp(0.058f, 0.082f, v) + Mathf.Max(0, -Mathf.Cos(a)) * 0.012f;
                return new Vector3(Mathf.Sin(a) * r, -Mathf.Lerp(0.03f, flen * 0.9f, v), Mathf.Cos(a) * r);
            }, 0.007f, true), zouge, la, la.position, frot);
            Add("VambraceCuff" + side, K.Shell(28, 1, (u, v) =>
            {
                float a = u * Mathf.PI * 2f + Mathf.PI, r = 0.088f;
                return new Vector3(Mathf.Sin(a) * r, -(flen * 0.9f - v * 0.025f), Mathf.Cos(a) * r);
            }, 0.006f, true), roku, la, la.position, frot);
            // 手甲（手の甲と指を覆う板）
            Add("Gauntlet" + side, K.Shell(12, 5, (u, v) =>
            {
                float a = Mathf.Lerp(-80f, 80f, u) * D2R, r = Mathf.Lerp(0.055f, 0.045f, v);
                return new Vector3(Mathf.Sin(a) * r, -(flen * 0.95f + v * 0.12f), Mathf.Cos(a) * r);
            }, 0.006f), zouge, hand, la.position, frot);
        }

        // ================= 脚 =================
        foreach (int s in new[] { -1, 1 })
        {
            string side = s > 0 ? "R" : "L";
            var ul = Bone(s < 0 ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
            var ll = Bone(s < 0 ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
            var ft = Bone(s < 0 ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
            Vector3 T = ll.position - ul.position; float tlen = T.magnitude; T /= tlen;
            var trot = Quaternion.LookRotation(Vector3.ProjectOnPlane(root.forward, T).normalized, -T);
            Add("Thigh" + side, K.Shell(24, 6, (u, v) =>
            {
                float a = u * Mathf.PI * 2f + Mathf.PI, r = Mathf.Lerp(0.095f, 0.078f, v);
                return new Vector3(Mathf.Sin(a) * r, -Mathf.Lerp(0.02f, tlen, v), Mathf.Cos(a) * r);
            }, 0.006f, true), kokuyo, ul, ul.position, trot);
            Add("Cuisse" + side, K.Shell(16, 6, (u, v) =>
            {
                float a = Mathf.Lerp(-95f, 95f, u) * D2R, r = Mathf.Lerp(0.108f, 0.09f, v) + 0.006f;
                return new Vector3(Mathf.Sin(a) * r, -Mathf.Lerp(0.1f, tlen * 0.93f, v), Mathf.Cos(a) * r);
            }, 0.008f), zouge, ul, ul.position, trot);
            // 膝当て（前へ突き出る棘つき）
            Vector3 kp = ll.position;
            Add("KneeCop" + side, K.Shell(16, 6, (u, v) =>
            {
                float a = Mathf.Lerp(-95f, 95f, u) * D2R, el = Mathf.Lerp(-60f, 70f, v) * D2R, r = 0.085f;
                return new Vector3(Mathf.Sin(a) * r * Mathf.Cos(el), Mathf.Sin(el) * r * 1.1f, Mathf.Cos(a) * r * Mathf.Cos(el) + 0.02f);
            }, 0.008f), zouge, ll, kp, R);
            Add("KneeSpike" + side, K.Tube(8, 6, t => kp + new Vector3(0, 0.02f + t * 0.05f, 0.1f + t * 0.09f), t => Vector2.one * Mathf.Lerp(0.03f, 0.002f, t)), roku, ll, Vector3.zero, R, worldSpace: true);

            // すね当て
            Vector3 S = ft.position - ll.position; float slen = S.magnitude; S /= slen;
            var srot = Quaternion.LookRotation(Vector3.ProjectOnPlane(root.forward, S).normalized, -S);
            Add("Greave" + side, K.Shell(28, 8, (u, v) =>
            {
                float a = u * Mathf.PI * 2f + Mathf.PI, r = Mathf.Lerp(0.07f, 0.064f, v) + Mathf.Sin(v * Mathf.PI) * 0.01f + Mathf.Max(0, Mathf.Cos(a)) * 0.012f;
                return new Vector3(Mathf.Sin(a) * r, -Mathf.Lerp(0.06f, slen * 0.98f, v), Mathf.Cos(a) * r);
            }, 0.007f, true), zouge, ll, ll.position, srot);
            Add("GreaveBand" + side, K.Shell(28, 1, (u, v) =>
            {
                float a = u * Mathf.PI * 2f + Mathf.PI, r = 0.08f;
                return new Vector3(Mathf.Sin(a) * r, -(slen * 0.45f - v * 0.02f), Mathf.Cos(a) * r);
            }, 0.005f, true), roku, ll, ll.position, srot);
            // 鉄靴（足の甲）
            Vector3 fp0 = ft.position;
            Add("Sabaton" + side, K.Shell(16, 6, (u, v) =>
            {
                float a = Mathf.Lerp(-100f, 100f, u) * D2R, len = Mathf.Lerp(-0.06f, 0.2f, v), r = Mathf.Lerp(0.07f, 0.04f, v);
                return new Vector3(Mathf.Sin(a) * r, Mathf.Cos(a) * r * 0.65f - 0.02f - v * 0.04f, len);
            }, 0.006f), zougeD, ft, fp0, R);
        }

        // ================= 体型：胸と肩を大きく、頭は小さく =================
        float legBefore = (Bone(HumanBodyBones.LeftUpperLeg).position - Bone(HumanBodyBones.LeftFoot).position).magnitude;
        ScaleBone(chest, 1.16f);
        ScaleBone(Bone(HumanBodyBones.Neck), 0.92f);
        ScaleBone(head, 0.86f);
        ScaleBone(Bone(HumanBodyBones.LeftUpperLeg), 1.05f);
        ScaleBone(Bone(HumanBodyBones.RightUpperLeg), 1.05f);
        float legAfter = (Bone(HumanBodyBones.LeftUpperLeg).position - Bone(HumanBodyBones.LeftFoot).position).magnitude;

        // ================= 大剣：地面に突き立てる（体とは別、根元に置く） =================
        var sword = new GameObject("Greatsword").transform; sword.SetParent(root, false);
        BuildSword(sword, kokuyo, roku, zouge, hikari);
        sword.localPosition = new Vector3(0.42f, 0f, 0.28f);
        sword.localRotation = Quaternion.Euler(-8f, -20f, 8f);

        // 全体の大きさと位置
        root.localScale = Vector3.one * SIZE;
        root.position = new Vector3(0.8f, (legAfter - legBefore) * SIZE, 0);
        sword.position = new Vector3(sword.position.x, 0f, sword.position.z) + new Vector3(0, -0.12f, 0);   // 切っ先を地面に埋める

        // 武士と並べる
        var samurai = GameObject.Find("SamuraiCharacter");
        if (samurai) samurai.transform.position = new Vector3(-0.7f, samurai.transform.position.y, 0);

        PrefabUtility.SaveAsPrefabAssetAndConnect(go, DIR + "/" + NAME + ".prefab", InteractionMode.AutomatedAction);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = go;
        EditorSceneManager.MarkSceneDirty(go.scene);
        EditorSceneManager.SaveScene(go.scene);
        Debug.Log($"[IBProto] 敵キャラ（王）を生成しました。素体 {bodyBefore:N0} → {bodyAfter:N0} tris／鎧と剣 {armorTris:N0} tris／合計 {bodyAfter + armorTris:N0} tris");
    }

    // 刃は +Y 方向に立てて作る（原点＝切っ先）
    static void BuildSword(Transform parent, Material blade, Material bronze, Material bone, Material glow)
    {
        const float L = 1.25f;   // 刃の長さ
        Func<float, Vector3> line = t => new Vector3(0, t * L, 0);
        // 刃：先が尖り、根元に向かって広がる。断面は平たい楕円
        AddLocal("Sword_Blade", K.Tube(14, 24, line, t =>
        {
            float w = t < 0.12f ? Mathf.Lerp(0.004f, 0.075f, Mathf.Sqrt(t / 0.12f)) : Mathf.Lerp(0.075f, 0.095f, (t - 0.12f) / 0.88f);
            return new Vector2(0.014f, w);
        }), blade, parent);
        // 樋（光る溝）
        AddLocal("Sword_Fuller", K.Tube(6, 12, t => new Vector3(0, Mathf.Lerp(0.2f, 0.95f, t) * L, 0), t => new Vector2(0.017f, 0.007f)), glow, parent);
        // 鍔：両端が上へ反る
        AddLocal("Sword_Guard", K.Tube(10, 24, t =>
        {
            float x = Mathf.Lerp(-0.26f, 0.26f, t);
            return new Vector3(x, L + 0.02f + x * x * 0.9f, 0);
        }, t => new Vector2(0.03f, Mathf.Lerp(0.02f, 0.034f, Mathf.Sin(t * Mathf.PI)))), bronze, parent);
        // 柄・柄頭の輪
        AddLocal("Sword_Grip", K.Tube(10, 8, t => new Vector3(0, L + 0.05f + t * 0.36f, 0), t => Vector2.one * 0.022f), blade, parent);
        AddLocal("Sword_GripBand", K.Tube(10, 2, t => new Vector3(0, L + 0.2f + t * 0.03f, 0), t => Vector2.one * 0.027f), bone, parent);
        AddLocal("Sword_Pommel", K.Tube(8, 32, t => { float a = t * Mathf.PI * 2f; return new Vector3(Mathf.Sin(a) * 0.05f, L + 0.47f + Mathf.Cos(a) * 0.05f, 0); }, t => Vector2.one * 0.012f), bronze, parent);
        AddLocal("Sword_PommelGem", K.Tube(10, 2, t => new Vector3(0, L + 0.47f, -0.01f + t * 0.02f), t => Vector2.one * 0.022f), glow, parent);
    }

    // ================= 道具 =================
    static Transform Bone(HumanBodyBones b) { return anim.GetBoneTransform(b); }
    static void ScaleBone(Transform t, float k) { if (t != null) t.localScale = t.localScale * k; }

    static Material Mat(string name, Color c, float metallic, float smooth, Color? emission = null)
    {
        string path = MAT_DIR + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            m = new Material(sh);
            AssetDatabase.CreateAsset(m, path);
        }
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (emission.HasValue && m.HasProperty("_EmissionColor"))
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission.Value);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        UnityEditor.EditorUtility.SetDirty(m);
        return m;
    }

    static GameObject SaveMesh(string name, Mesh mesh, Material mat)
    {
        mesh.name = name;
        string p = MESH_DIR + "/" + name + ".asset";
        AssetDatabase.DeleteAsset(p);
        AssetDatabase.CreateAsset(mesh, p);
        var g = new GameObject(name);
        g.AddComponent<MeshFilter>().sharedMesh = mesh;
        g.AddComponent<MeshRenderer>().sharedMaterial = mat;
        armorTris += mesh.triangles.Length / 3;
        return g;
    }

    // worldSpace=true：メッシュの座標をキャラ基準の位置として扱う。false：origin/rot を原点にした座標
    static void Add(string name, Mesh mesh, Material mat, Transform parent, Vector3 origin, Quaternion rot, bool worldSpace = false)
    {
        var g = SaveMesh(name, mesh, mat);
        if (worldSpace) g.transform.SetPositionAndRotation(root.position, root.rotation);
        else g.transform.SetPositionAndRotation(origin, rot);
        g.transform.SetParent(parent, true);
    }

    static void AddLocal(string name, Mesh mesh, Material mat, Transform parent)
    {
        var g = SaveMesh(name, mesh, mat);
        g.transform.SetParent(parent, false);
    }
}
