using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// メニュー「IBProto > 武士キャラを生成」
// ・素体（StarterAssets の人型）のポリゴンを UnityMeshSimplifier で減らす
// ・兜・面頬・胴・草摺・袖・籠手・脛当・刀を、なめらかな曲面メッシュとしてコードで作り、骨に付ける
// 生成物は Assets/IBProto/Characters/Samurai に保存される（Meshes / Materials / Textures / prefab）。
public static class IBProtoSamuraiBuilder
{
    const string MODEL_PATH = "Assets/StarterAssets/ThirdPersonController/Character/Models/Armature.fbx";
    const string DIR = "Assets/IBProto/Characters/Samurai";
    const string MESH_DIR = DIR + "/Meshes", MAT_DIR = DIR + "/Materials", TEX_DIR = DIR + "/Textures";
    const float BODY_QUALITY = 0.35f;     // 素体のポリゴンを何割残すか

    static Transform root;
    static Animator anim;
    static int armorTris;

    [MenuItem("IBProto/旧・生成ツール（作り直し用）/武士キャラを生成")]
    public static void Build()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(MODEL_PATH);
        if (model == null) { Debug.LogError("[IBProto] 素体が見つかりません: " + MODEL_PATH); return; }
        EnsureDir();

        foreach (var n in new[] { "PBCharacter", "SamuraiCharacter" }) { var o = GameObject.Find(n); if (o) Object.DestroyImmediate(o); }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        go.name = "SamuraiCharacter";
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        root = go.transform;
        anim = go.GetComponent<Animator>();
        if (anim == null || !anim.isHuman) { Debug.LogError("[IBProto] 素体が Humanoid ではありません"); return; }
        armorTris = 0;

        // ---- マテリアル ----
        var kuro  = Mat("S_Kuro",  new Color(0.05f, 0.042f, 0.038f), 0.1f, 0.62f);  // 黒漆（少し茶がかった使い込んだ色）
        var aka   = Mat("S_Aka",   new Color(0.42f, 0.035f, 0.045f), 0.1f, 0.82f);   // 朱漆
        var kin   = Mat("S_Kin",   new Color(0.6f, 0.47f, 0.26f), 0.9f, 0.5f);        // くすんだ金
        var ito   = Mat("S_Ito",   new Color(0.38f, 0.05f, 0.05f), 0f, 0.3f);        // 威糸（深い赤）
        var tetsu = Mat("S_Tetsu", new Color(0.17f, 0.17f, 0.19f), 0.85f, 0.55f);    // 鉄
        var nuno  = Mat("S_Nuno",  new Color(0.075f, 0.075f, 0.085f), 0f, 0.2f);      // 布（墨色）
        var odoshi = TexMat("S_Odoshi", OdoshiTex(), 0.05f, 0.45f);                  // 毛引威（縦の威糸でびっしり綴じた板）
        var tsuka = Mat("S_Tsuka", new Color(0.12f, 0.1f, 0.09f), 0f, 0.35f);        // 柄巻

        // ---- 素体：ポリゴンを減らして、布の色に ----
        int bodyBefore = 0, bodyAfter = 0;
        foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (r.sharedMesh == null) continue;
            bodyBefore += r.sharedMesh.triangles.Length / 3;
            var simp = Simplify(r.sharedMesh, BODY_QUALITY);
            if (simp != null)
            {
                simp.name = r.sharedMesh.name + "_simplified";
                string p = MESH_DIR + "/" + simp.name + ".asset";
                AssetDatabase.DeleteAsset(p);
                AssetDatabase.CreateAsset(simp, p);
                r.sharedMesh = simp;
            }
            bodyAfter += r.sharedMesh.triangles.Length / 3;
            r.sharedMaterials = r.sharedMaterials.Select(_ => nuno).ToArray();
        }

        var head = Bone(HumanBodyBones.Head);
        var neck = Bone(HumanBodyBones.Neck);
        var hips = Bone(HumanBodyBones.Hips);
        var chest = Bone(HumanBodyBones.Chest) ?? Bone(HumanBodyBones.Spine);
        Quaternion R = root.rotation;

        // ================= 兜 =================
        Vector3 hc = head.position + R * new Vector3(0, 0.085f, -0.005f);
        const float HR = 0.132f;
        // 鉢（半球の椀）
        Add("Kabuto_Hachi", Shell(48, 14, (u, v) =>
        {
            float a = u * Mathf.PI * 2f + Mathf.PI, ph = v * Mathf.Deg2Rad * 88f;
            return new Vector3(Mathf.Sin(a) * HR * Mathf.Sin(ph), HR * 0.92f * Mathf.Cos(ph), Mathf.Cos(a) * HR * 1.06f * Mathf.Sin(ph));
        }, 0.006f, true), kuro, head, hc, R);
        // 鉢の縦筋（筋兜っぽさ）
        for (int i = 0; i < 12; i++)
        {
            float a0 = i / 12f * Mathf.PI * 2f;
            Add("Kabuto_Suji" + i, Shell(2, 14, (u, v) =>
            {
                float a = a0 + (u - 0.5f) * 0.03f, ph = Mathf.Lerp(8f, 86f, v) * Mathf.Deg2Rad; float r = HR + 0.003f;
                return new Vector3(Mathf.Sin(a) * r * Mathf.Sin(ph), r * 0.92f * Mathf.Cos(ph), Mathf.Cos(a) * r * 1.06f * Mathf.Sin(ph));
            }, 0.003f), kin, head, hc, R);
        }
        // 眉庇（前のひさし）
        Add("Kabuto_Mabizashi", Shell(24, 3, (u, v) =>
        {
            float a = Mathf.Lerp(-75f, 75f, u) * Mathf.Deg2Rad; float r = HR + 0.004f + v * 0.045f;
            return new Vector3(Mathf.Sin(a) * r, -0.004f - v * 0.018f, Mathf.Cos(a) * r * 1.06f);
        }, 0.005f), kuro, head, hc, R);
        // 錣（首の後ろを守る段々の板）と威糸
        for (int i = 0; i < 4; i++)
        {
            float yT = -0.01f - i * 0.034f, h = 0.042f, rT = HR + 0.01f + i * 0.02f, rB = rT + 0.022f;
            Add("Kabuto_Shikoro" + i, Shell(30, 3, (u, v) =>
            {
                float a = Mathf.Lerp(62f, 298f, u) * Mathf.Deg2Rad; float r = Mathf.Lerp(rT, rB, v);
                return new Vector3(Mathf.Sin(a) * r, yT - v * h, Mathf.Cos(a) * r * 1.06f);
            }, 0.005f, uvTile: 0.032f), odoshi, head, hc, R);
            // （威糸はテクスチャに含めた）
        }
        // 吹返（錣の前端が外へ反り返った部分）
        foreach (int s in new[] { -1, 1 })
        {
            Add("Kabuto_Fukigaeshi" + (s > 0 ? "R" : "L"), Shell(8, 4, (u, v) =>
            {
                float a = s * Mathf.Lerp(48f, 66f, u) * Mathf.Deg2Rad; float r = HR + 0.03f + v * 0.04f + u * 0.02f;
                return new Vector3(Mathf.Sin(a) * r, -0.012f - v * 0.05f, Mathf.Cos(a) * r * 1.06f);
            }, 0.005f), kuro, head, hc, R);
            Add("Kabuto_FukiKin" + (s > 0 ? "R" : "L"), Shell(8, 1, (u, v) =>
            {
                float a = s * Mathf.Lerp(48f, 66f, u) * Mathf.Deg2Rad; float r = HR + 0.034f + u * 0.02f;
                return new Vector3(Mathf.Sin(a) * r, -0.012f - v * 0.006f, Mathf.Cos(a) * r * 1.06f);
            }, 0.004f), kin, head, hc, R);
        }
        // 鍬形（金の二本角）
        foreach (int s in new[] { -1, 1 })
        {
            Vector3 p0 = new Vector3(s * 0.012f, 0.045f, HR * 1.04f), p1 = new Vector3(s * 0.06f, 0.11f, HR * 1.12f), p2 = new Vector3(s * 0.12f, 0.24f, HR * 0.98f);
            Add("Kabuto_Kuwagata" + (s > 0 ? "R" : "L"), Shell(16, 3, (u, v) =>
            {
                Vector3 c = Bez(p0, p1, p2, u);
                Vector3 t = (Bez(p0, p1, p2, Mathf.Min(1, u + 0.01f)) - Bez(p0, p1, p2, Mathf.Max(0, u - 0.01f))).normalized;
                Vector3 side = Vector3.Cross(t, Vector3.forward).normalized;
                float w = Mathf.Lerp(0.03f, 0.006f, u * u);
                return c + side * (v - 0.5f) * w;
            }, 0.004f), kin, head, hc, R);
        }
        // 前立（正面の飾り）
        Add("Kabuto_Maedate", Shell(24, 2, (u, v) =>
        {
            float a = u * Mathf.PI * 2f, r = v * 0.026f;
            return new Vector3(Mathf.Sin(a) * r, 0.05f + Mathf.Cos(a) * r, HR * 1.09f);
        }, 0.006f, true), kin, head, hc, R);

        // ================= 面頬（顔の下半分を覆う面） =================
        Vector3 fc = head.position + R * new Vector3(0, 0.035f, 0.0f);
        Add("Menpo", Shell(26, 8, (u, v) =>
        {
            float a = Mathf.Lerp(-82f, 82f, u) * Mathf.Deg2Rad;
            float y = Mathf.Lerp(0.035f, -0.075f, v);
            float r = Mathf.Lerp(0.088f, 0.07f, v) + Mathf.Exp(-a * a * 18f) * 0.018f * Mathf.Sin(v * Mathf.PI); // 鼻と頬の張り
            return new Vector3(Mathf.Sin(a) * r, y, Mathf.Cos(a) * r * 1.1f + 0.012f);
        }, 0.005f), aka, head, fc, R);
        Add("Yodarekake", Shell(26, 3, (u, v) =>
        {
            float a = Mathf.Lerp(-85f, 85f, u) * Mathf.Deg2Rad; float r = 0.07f + v * 0.025f;
            return new Vector3(Mathf.Sin(a) * r, -0.08f - v * 0.045f, Mathf.Cos(a) * r * 1.1f + 0.012f);
        }, 0.005f), kuro, head, fc, R);

        // ================= 胴 =================
        float waistY = hips.position.y + 0.1f, chestTopY = neck.position.y - 0.06f;
        Vector3 dc = new Vector3(0, 0, 0.012f);
        Func<float, Vector2> torsoR = t => { float k = Mathf.SmoothStep(0f, 1f, t * 1.15f); return new Vector2(Mathf.Lerp(0.148f, 0.182f, k), Mathf.Lerp(0.114f, 0.135f, k)); };
        Add("Do", Shell(48, 12, (u, v) =>
        {
            float a = u * Mathf.PI * 2f + Mathf.PI, y = Mathf.Lerp(waistY, chestTopY, v); var rr = torsoR(v);
            return new Vector3(Mathf.Sin(a) * rr.x, y, Mathf.Cos(a) * rr.y) + dc;
        }, 0.009f, true), kuro, chest, Vector3.zero, Quaternion.identity, worldSpace: true);
        for (int i = 0; i < 4; i++)
        {
            float t = 0.12f + i * 0.2f;
            Add("Do_Ito" + i, Shell(48, 1, (u, v) =>
            {
                float a = u * Mathf.PI * 2f + Mathf.PI, y = Mathf.Lerp(waistY, chestTopY, t) - v * 0.012f; var rr = torsoR(t) + Vector2.one * 0.006f;
                return new Vector3(Mathf.Sin(a) * rr.x, y, Mathf.Cos(a) * rr.y) + dc;
            }, 0.003f, true), ito, chest, Vector3.zero, Quaternion.identity, worldSpace: true);
        }
        // 胸板の金の縁取り
        Add("Do_Munaita", Shell(24, 1, (u, v) =>
        {
            float a = Mathf.Lerp(-70f, 70f, u) * Mathf.Deg2Rad, y = chestTopY + 0.004f - v * 0.014f; var rr = torsoR(1f) + Vector2.one * 0.004f;
            return new Vector3(Mathf.Sin(a) * rr.x, y, Mathf.Cos(a) * rr.y) + dc;
        }, 0.004f), kin, chest, Vector3.zero, Quaternion.identity, worldSpace: true);
        // 帯
        Add("Obi", Shell(48, 2, (u, v) =>
        {
            float a = u * Mathf.PI * 2f + Mathf.PI, y = waistY + 0.01f - v * 0.045f; var rr = torsoR(0f) + new Vector2(0.008f, 0.01f);
            return new Vector3(Mathf.Sin(a) * rr.x, y, Mathf.Cos(a) * rr.y) + dc;
        }, 0.006f, true), aka, hips, Vector3.zero, Quaternion.identity, worldSpace: true);

        // ================= 草摺（腰から下がる段々の板） =================
        float[] centers = { 0f, 72f, 144f, 216f, 288f };
        for (int k = 0; k < centers.Length; k++)
        {
            float ac = centers[k];
            for (int i = 0; i < 6; i++)
            {
                float yT = waistY - 0.035f - i * 0.046f, rT = 0.163f + i * 0.016f, rB = rT + 0.022f;
                Add("Kusazuri" + k + "_" + i, Shell(10, 3, (u, v) =>
                {
                    float a = (ac + Mathf.Lerp(-31f, 31f, u)) * Mathf.Deg2Rad, r = Mathf.Lerp(rT, rB, v);
                    return new Vector3(Mathf.Sin(a) * r, yT - v * 0.05f, Mathf.Cos(a) * r * 0.84f) + dc;
                }, 0.005f, uvTile: 0.032f), odoshi, hips, Vector3.zero, Quaternion.identity, worldSpace: true);
                // （威糸はテクスチャに含めた）
            }
        }

        // ================= 袖（肩から腕の外側に下がる板） =================
        foreach (int s in new[] { -1, 1 })
        {
            var ua = Bone(s < 0 ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            var la = Bone(s < 0 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            Vector3 D = (la.position - ua.position).normalized;
            Vector3 outv = Vector3.ProjectOnPlane(root.right * s, D);
            if (outv.sqrMagnitude < 0.01f) outv = Vector3.ProjectOnPlane(root.up, D);
            outv.Normalize();
            var rot = Quaternion.LookRotation(outv, -D);
            Add("SodeCap" + (s > 0 ? "R" : "L"), Shell(14, 5, (u, v) =>
            {
                float a = Mathf.Lerp(-95f, 95f, u) * Mathf.Deg2Rad, el = Mathf.Lerp(70f, 5f, v) * Mathf.Deg2Rad, r = 0.11f;
                return new Vector3(Mathf.Sin(a) * r * Mathf.Cos(el) , -(-0.09f + Mathf.Sin(el) * -0.06f + v * 0.02f), Mathf.Cos(a) * r * Mathf.Cos(el) + 0.01f);
            }, 0.006f), kuro, ua, ua.position, rot);
            for (int i = 0; i < 6; i++)
            {
                float s0 = -0.07f + i * 0.046f, r0 = 0.1f + i * 0.009f;
                Add("Sode" + (s > 0 ? "R" : "L") + i, Shell(14, 3, (u, v) =>
                {
                    float a = Mathf.Lerp(-88f, 88f, u) * Mathf.Deg2Rad, r = r0 + v * 0.016f;
                    return new Vector3(Mathf.Sin(a) * r, -(s0 + v * 0.054f), Mathf.Cos(a) * r);
                }, 0.006f, uvTile: 0.032f), odoshi, ua, ua.position, rot);
                // （威糸はテクスチャに含めた）
            }
            // 二の腕：ふくらみのある袖（布）と肘金
            float ulen = (la.position - ua.position).magnitude;
            Add("Sleeve" + (s > 0 ? "R" : "L"), Shell(24, 8, (u, v) =>
            {
                float a = u * Mathf.PI * 2f + Mathf.PI, r = Mathf.Lerp(0.064f, 0.08f, v) + Mathf.Sin(v * Mathf.PI) * 0.008f + Mathf.Sin(a * 6f) * 0.004f;
                return new Vector3(Mathf.Sin(a) * r, -Mathf.Lerp(0.0f, ulen * 1.02f, v), Mathf.Cos(a) * r);
            }, 0.005f, true), nuno, ua, ua.position, rot);
            Add("Hijigane" + (s > 0 ? "R" : "L"), Shell(12, 4, (u, v) =>
            {
                float a = Mathf.Lerp(-80f, 80f, u) * Mathf.Deg2Rad, r = 0.066f + Mathf.Sin(v * Mathf.PI) * 0.008f;
                return new Vector3(Mathf.Sin(a) * r, -Mathf.Lerp(ulen - 0.045f, ulen + 0.035f, v), Mathf.Cos(a) * r);
            }, 0.005f), kuro, ua, ua.position, rot);

            // 籠手（前腕の筒）
            var hand = Bone(s < 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            Vector3 F = (hand.position - la.position); float flen = F.magnitude; F /= flen;
            var frot = Quaternion.LookRotation(Vector3.ProjectOnPlane(root.forward, F).sqrMagnitude > 0.01f ? Vector3.ProjectOnPlane(root.forward, F).normalized : root.up, -F);
            Add("Kote" + (s > 0 ? "R" : "L"), Shell(24, 6, (u, v) =>
            {
                float a = u * Mathf.PI * 2f + Mathf.PI, r = Mathf.Lerp(0.062f, 0.05f, v) + Mathf.Sin(v * Mathf.PI) * 0.007f;
                return new Vector3(Mathf.Sin(a) * r, -Mathf.Lerp(0.04f, flen * 0.92f, v), Mathf.Cos(a) * r);
            }, 0.004f, true), kuro, la, la.position, frot);
            Add("KoteKin" + (s > 0 ? "R" : "L"), Shell(24, 1, (u, v) =>
            {
                float a = u * Mathf.PI * 2f + Mathf.PI, r = 0.055f;
                return new Vector3(Mathf.Sin(a) * r, -(flen * 0.92f - v * 0.012f), Mathf.Cos(a) * r);
            }, 0.004f, true), kin, la, la.position, frot);
            Add("Tekko" + (s > 0 ? "R" : "L"), Shell(10, 4, (u, v) =>
            {
                float a = Mathf.Lerp(-75f, 75f, u) * Mathf.Deg2Rad, r = 0.047f;
                return new Vector3(Mathf.Sin(a) * r, -(flen * 0.95f + v * 0.075f), Mathf.Cos(a) * r);
            }, 0.004f), kuro, hand, la.position, frot);
        }

        // ================= 佩楯（太ももの前を守る板） =================
        foreach (int s in new[] { -1, 1 })
        {
            var ul = Bone(s < 0 ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
            var kn = Bone(s < 0 ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
            Vector3 T = kn.position - ul.position; float tlen = T.magnitude; T /= tlen;
            var trot = Quaternion.LookRotation(Vector3.ProjectOnPlane(root.forward, T).normalized, -T);
            Add("Hakama" + (s > 0 ? "R" : "L"), Shell(28, 8, (u, v) =>
            {
                float a = u * Mathf.PI * 2f + Mathf.PI, r = Mathf.Lerp(0.088f, 0.104f, v) + Mathf.Sin(a * 7f) * 0.004f * v;
                return new Vector3(Mathf.Sin(a) * r, -Mathf.Lerp(0.02f, tlen * 1.02f, v), Mathf.Cos(a) * r);
            }, 0.005f, true), nuno, ul, ul.position, trot);
            for (int i = 0; i < 4; i++)
            {
                float s0 = tlen * 0.22f + i * 0.055f, r0 = 0.11f + i * 0.005f;
                Add("Haidate" + (s > 0 ? "R" : "L") + i, Shell(12, 3, (u, v) =>
                {
                    float a = Mathf.Lerp(-90f, 90f, u) * Mathf.Deg2Rad, r = r0 + v * 0.012f;
                    return new Vector3(Mathf.Sin(a) * r, -(s0 + v * 0.06f), Mathf.Cos(a) * r);
                }, 0.005f, uvTile: 0.032f), odoshi, ul, ul.position, trot);
                // （威糸はテクスチャに含めた）
            }
        }

        // ================= 脛当 =================
        foreach (int s in new[] { -1, 1 })
        {
            var ll = Bone(s < 0 ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
            var ft = Bone(s < 0 ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
            Vector3 S = ft.position - ll.position; float slen = S.magnitude; S /= slen;
            var srot = Quaternion.LookRotation(Vector3.ProjectOnPlane(root.forward, S).normalized, -S);
            Add("Kyahan" + (s > 0 ? "R" : "L"), Shell(24, 6, (u, v) =>
            {
                float a = u * Mathf.PI * 2f + Mathf.PI, r = Mathf.Lerp(0.066f, 0.05f, v) + Mathf.Sin(v * Mathf.PI * 0.8f) * 0.008f;
                return new Vector3(Mathf.Sin(a) * r, -Mathf.Lerp(0.02f, slen * 0.95f, v), Mathf.Cos(a) * r);
            }, 0.004f, true), nuno, ll, ll.position, srot);
            Add("Suneate" + (s > 0 ? "R" : "L"), Shell(16, 6, (u, v) =>
            {
                float a = Mathf.Lerp(-105f, 105f, u) * Mathf.Deg2Rad, r = Mathf.Lerp(0.08f, 0.062f, v);
                return new Vector3(Mathf.Sin(a) * r, -Mathf.Lerp(0.06f, slen * 0.88f, v), Mathf.Cos(a) * r + 0.008f);
            }, 0.005f), kuro, ll, ll.position, srot);
            Add("Tateage" + (s > 0 ? "R" : "L"), Shell(12, 4, (u, v) =>
            {
                float a = Mathf.Lerp(-75f, 75f, u) * Mathf.Deg2Rad, r = 0.074f + Mathf.Sin(v * Mathf.PI) * 0.012f;
                return new Vector3(Mathf.Sin(a) * r, -Mathf.Lerp(-0.05f, 0.06f, v), Mathf.Cos(a) * r + 0.012f);
            }, 0.005f), kin, ll, ll.position, srot);
        }

        // ================= 刀（左腰に差す） =================
        Vector3 k0 = new Vector3(-0.12f, waistY + 0.07f, 0.27f);   // 柄頭
        Vector3 k1 = new Vector3(-0.19f, waistY - 0.02f, 0.0f);
        Vector3 k2 = new Vector3(-0.21f, waistY - 0.34f, -0.4f);    // 鐺
        Func<float, Vector3> kat = t => Bez(k0, k1, k2, t);
        Add("Katana_Tsuka", Tube(18, 10, t => kat(t * 0.28f), t => new Vector2(0.016f, 0.013f)), tsuka, hips, Vector3.zero, Quaternion.identity, worldSpace: true);
        Add("Katana_Saya", Tube(18, 24, t => kat(Mathf.Lerp(0.3f, 1f, t)), t => new Vector2(Mathf.Lerp(0.018f, 0.014f, t), Mathf.Lerp(0.013f, 0.01f, t))), kuro, hips, Vector3.zero, Quaternion.identity, worldSpace: true);
        Add("Katana_Tsuba", Tube(24, 1, t => kat(0.285f + t * 0.012f), t => new Vector2(0.045f, 0.04f)), kin, hips, Vector3.zero, Quaternion.identity, worldSpace: true);
        Add("Katana_Kojiri", Tube(18, 2, t => kat(0.98f + t * 0.02f), t => new Vector2(0.0155f, 0.0115f)), kin, hips, Vector3.zero, Quaternion.identity, worldSpace: true);

        // ================= 揚巻（背中の飾り結び）と帯の垂れ =================
        {
            Vector3 bc = new Vector3(0, Mathf.Lerp(waistY, chestTopY, 0.62f), -torsoR(0.62f).y - 0.02f);
            foreach (int s in new[] { -1, 1 })
            {
                Vector3 lc = bc + new Vector3(s * 0.045f, 0.02f, -0.005f);
                Add("Agemaki_Loop" + (s > 0 ? "R" : "L"), Tube(10, 20, t =>
                {
                    float a = t * Mathf.PI * 2f;
                    return lc + new Vector3(Mathf.Cos(a) * 0.04f * s, Mathf.Sin(a) * 0.028f, 0);
                }, t => new Vector2(0.007f, 0.007f)), ito, chest, Vector3.zero, Quaternion.identity, worldSpace: true);
                Add("Agemaki_Tail" + (s > 0 ? "R" : "L"), Tube(10, 12, t =>
                    bc + new Vector3(s * Mathf.Lerp(0.008f, 0.05f, t), -Mathf.Lerp(0.0f, 0.2f, t), -0.004f - t * 0.01f),
                    t => new Vector2(0.006f, 0.006f)), ito, chest, Vector3.zero, Quaternion.identity, worldSpace: true);
            }
            Add("Agemaki_Knot", Tube(12, 4, t => bc + new Vector3(0, 0, -t * 0.012f), t => new Vector2(0.014f, 0.014f)), ito, chest, Vector3.zero, Quaternion.identity, worldSpace: true);
            // 帯の垂れ（腰の後ろ）
            foreach (int s in new[] { -1, 1 })
            {
                float z0 = -torsoR(0f).y - 0.02f;
                Add("ObiTare" + (s > 0 ? "R" : "L"), Shell(3, 10, (u, v) =>
                {
                    float x = s * (0.03f + v * 0.03f) + (u - 0.5f) * 0.05f;
                    return new Vector3(x, waistY - 0.01f - v * 0.3f, z0 - Mathf.Sin(v * 2.4f) * 0.035f);
                }, 0.004f), aka, hips, Vector3.zero, Quaternion.identity, worldSpace: true);
            }
        }

        // ================= 体型（インフィニティブレードの主人公のような英雄体型） =================
        // 鎧を作り終えてから骨を拡大縮小するので、鎧も一緒に追従する
        float legBefore = (Bone(HumanBodyBones.LeftUpperLeg).position - Bone(HumanBodyBones.LeftFoot).position).magnitude;
        ScaleBone(HumanBodyBones.UpperChest, HumanBodyBones.Chest, 1.12f);   // 胸と肩幅を広く（腕も少し太く長く）
        ScaleBone(HumanBodyBones.Neck, HumanBodyBones.Neck, 0.94f);
        ScaleBone(HumanBodyBones.Head, HumanBodyBones.Head, 0.84f);         // 頭を小さく（全体で約0.88倍）
        ScaleBone(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftUpperLeg, 1.07f);   // 脚を長く
        ScaleBone(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightUpperLeg, 1.07f);
        float legAfter = (Bone(HumanBodyBones.LeftUpperLeg).position - Bone(HumanBodyBones.LeftFoot).position).magnitude;
        root.position = new Vector3(0, legAfter - legBefore, 0);            // 脚が伸びた分、地面に埋まらないよう持ち上げる
        if (GameObject.Find("EnemyKing")) root.position += new Vector3(-0.7f, 0, 0);   // 敵キャラと並べる

        // 表示用スクリプト
        var holder = GameObject.Find("Chara") ?? new GameObject("Chara");
        foreach (var mb in holder.GetComponents<MonoBehaviour>()) if (mb != null && mb.GetType().Name == "ProtoCharacter") Object.DestroyImmediate(mb);
        if (holder.GetComponent<ProtoCharaViewer>() == null) holder.AddComponent<ProtoCharaViewer>();

        // 今後のデフォルトとしてプレハブに保存
        PrefabUtility.SaveAsPrefabAssetAndConnect(go, DIR + "/SamuraiCharacter.prefab", InteractionMode.AutomatedAction);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = go;
        EditorSceneManager.MarkSceneDirty(go.scene);
        EditorSceneManager.SaveScene(go.scene);
        Debug.Log($"[IBProto] 武士キャラを生成しました。素体 {bodyBefore:N0} → {bodyAfter:N0} tris／鎧 {armorTris:N0} tris／合計 {bodyAfter + armorTris:N0} tris");
    }

    // ================= 素体の軽量化（UnityMeshSimplifier をリフレクションで呼ぶ） =================
    internal static Mesh Simplify(Mesh src, float quality)
    {
        Type t = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            t = asm.GetType("UnityMeshSimplifier.MeshSimplifier");
            if (t != null) break;
        }
        if (t == null) { Debug.LogWarning("[IBProto] UnityMeshSimplifier が見つからないので、素体の軽量化はスキップしました"); return null; }
        var s = Activator.CreateInstance(t);
        t.GetMethod("Initialize", new[] { typeof(Mesh) }).Invoke(s, new object[] { src });
        t.GetMethod("SimplifyMesh", new[] { typeof(float) }).Invoke(s, new object[] { quality });
        var dst = (Mesh)t.GetMethod("ToMesh", Type.EmptyTypes).Invoke(s, null);
        if (dst != null && (dst.bindposes == null || dst.bindposes.Length == 0)) dst.bindposes = src.bindposes;
        return dst;
    }

    // ================= 道具 =================
    static Transform Bone(HumanBodyBones b) { return anim.GetBoneTransform(b); }

    static void ScaleBone(HumanBodyBones b, HumanBodyBones fallback, float k)
    {
        var t = Bone(b) ?? Bone(fallback);
        if (t != null) t.localScale = t.localScale * k;
    }

    static Vector3 Bez(Vector3 a, Vector3 b, Vector3 c, float t) { float m = 1 - t; return m * m * a + 2 * m * t * b + t * t * c; }

    static void EnsureDir()
    {
        foreach (var d in new[] { DIR, MESH_DIR, MAT_DIR, TEX_DIR }) MakeFolder(d);
    }

    public static void MakeFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int i = path.LastIndexOf('/');
        string parent = path.Substring(0, i);
        MakeFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(i + 1));
    }

    static Material Mat(string name, Color c, float metallic, float smooth)
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
        UnityEditor.EditorUtility.SetDirty(m);
        return m;
    }

    // テクスチャ付きマテリアル
    static Material TexMat(string name, Texture2D tex, float metallic, float smooth)
    {
        var m = Mat(name, Color.white, metallic, smooth);
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
        UnityEditor.EditorUtility.SetDirty(m);
        return m;
    }

    // 毛引威の柄：黒漆の板（上端に縁）に、深い赤の威糸が縦に密に並ぶ。Uは繰り返し、Vは板1枚分
    static Texture2D OdoshiTex()
    {
        const int N = 256;
        string path = TEX_DIR + "/T_Odoshi.png";
        var t = new Texture2D(N, N, TextureFormat.RGBA32, true);
        var rnd = new System.Random(3);
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
        {
            float u = x / (float)N, v = y / (float)N;
            float n = (float)rnd.NextDouble() * 0.03f;
            // 黒漆の板（上の縁は少し明るく、使い込んだ擦れ）
            Color c = new Color(0.055f + n, 0.046f + n, 0.04f + n);
            if (v > 0.86f) c = Color.Lerp(c, new Color(0.16f, 0.13f, 0.1f), 0.6f);
            // 威糸：1タイルに1本、編み目の斜め模様
            float lu = Mathf.Repeat(u, 1f);
            if (lu > 0.18f && lu < 0.82f && v < 0.84f)
            {
                float weave = Mathf.Repeat((v * 14f + lu * 3f), 1f) < 0.5f ? 0.85f : 1f;
                float edge = Mathf.SmoothStep(0f, 1f, Mathf.Min(lu - 0.18f, 0.82f - lu) * 14f);
                Color lace = new Color(0.36f, 0.045f, 0.045f) * weave;
                c = Color.Lerp(c, lace, edge);
            }
            // 綴じ穴
            if (Mathf.Abs(lu - 0.5f) < 0.06f && Mathf.Abs(v - 0.9f) < 0.025f) c = new Color(0.02f, 0.015f, 0.012f);
            c.a = 1f;
            t.SetPixel(x, y, c);
        }
        t.Apply();
        System.IO.File.WriteAllBytes(path, t.EncodeToPNG());
        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        if (imp != null) { imp.wrapMode = TextureWrapMode.Repeat; imp.sRGBTexture = true; imp.SaveAndReimport(); }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // メッシュを置いて骨に付ける。worldSpace=true のときは、メッシュの座標をキャラ基準の位置そのものとして扱う
    static void Add(string name, Mesh mesh, Material mat, Transform parent, Vector3 origin, Quaternion rot, bool worldSpace = false)
    {
        mesh.name = name;
        string p = MESH_DIR + "/" + name + ".asset";
        AssetDatabase.DeleteAsset(p);
        AssetDatabase.CreateAsset(mesh, p);
        var g = new GameObject(name);
        g.AddComponent<MeshFilter>().sharedMesh = mesh;
        g.AddComponent<MeshRenderer>().sharedMaterial = mat;
        if (worldSpace) { g.transform.SetPositionAndRotation(root.position, root.rotation); }
        else { g.transform.SetPositionAndRotation(origin, rot); }
        g.transform.SetParent(parent, true);
        armorTris += mesh.triangles.Length / 3;
    }

    // 曲面 P(u,v) に厚みを付けた板。表裏はなめらか、縁は角を立てる
    internal static Mesh Shell(int nu, int nv, Func<float, float, Vector3> P, float thick, bool closedU = false, float uvTile = 0f)
    {
        var verts = new List<Vector3>(); var tris = new List<int>(); var uvs = new List<Vector2>();
        int W = nu + 1, Hh = nv + 1;
        var outer = new Vector3[W, Hh]; var nrm = new Vector3[W, Hh];
        for (int i = 0; i < W; i++) for (int j = 0; j < Hh; j++) outer[i, j] = P(i / (float)nu, j / (float)nv);
        float e = 0.002f;
        for (int i = 0; i < W; i++) for (int j = 0; j < Hh; j++)
        {
            float u = i / (float)nu, v = j / (float)nv;
            Vector3 du = P(Mathf.Min(1, u + e), v) - P(Mathf.Max(0, u - e), v);
            Vector3 dv = P(u, Mathf.Min(1, v + e)) - P(u, Mathf.Max(0, v - e));
            var n = Vector3.Cross(du, dv);
            nrm[i, j] = n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up;
        }
        // 外側の向きをそろえる（中心から離れる向きを外とみなす）
        Vector3 cen = Vector3.zero; foreach (var p in outer) cen += p; cen /= (W * Hh);
        float sign = 0; for (int i = 0; i < W; i++) for (int j = 0; j < Hh; j++) sign += Vector3.Dot(nrm[i, j], outer[i, j] - cen);
        if (sign < 0) for (int i = 0; i < W; i++) for (int j = 0; j < Hh; j++) nrm[i, j] = -nrm[i, j];
        var inner = new Vector3[W, Hh];
        for (int i = 0; i < W; i++) for (int j = 0; j < Hh; j++) inner[i, j] = outer[i, j] - nrm[i, j] * thick;

        // UのUV：uvTile>0 なら弧の長さに合わせて繰り返す（威糸の柄の間隔をそろえる）
        var uCoord = new float[W];
        if (uvTile > 0f)
        {
            int mid = nv / 2; float acc = 0; uCoord[0] = 0;
            for (int i = 1; i < W; i++) { acc += (outer[i, mid] - outer[i - 1, mid]).magnitude; uCoord[i] = acc / uvTile; }
        }
        else for (int i = 0; i < W; i++) uCoord[i] = i / (float)nu;

        void Grid(Vector3[,] g, bool outward)
        {
            int b = verts.Count;
            for (int i = 0; i < W; i++) for (int j = 0; j < Hh; j++) { verts.Add(g[i, j]); uvs.Add(new Vector2(uCoord[i], j / (float)nv)); }
            for (int i = 0; i < nu; i++) for (int j = 0; j < nv; j++)
            {
                int a = b + i * Hh + j, c = b + (i + 1) * Hh + j, d = b + (i + 1) * Hh + j + 1, f = b + i * Hh + j + 1;
                Vector3 want = (nrm[i, j] + nrm[i + 1, j + 1]) * (outward ? 1 : -1);
                Tri(verts, tris, a, c, d, want); Tri(verts, tris, a, d, f, want);
            }
        }
        Grid(outer, true); Grid(inner, false);

        void Rim(Func<int, Vector3> o, Func<int, Vector3> inn, int count, Vector3 dirHint, Func<int, Vector3> dirAt)
        {
            int b = verts.Count;
            for (int k = 0; k <= count; k++) { verts.Add(o(k)); verts.Add(inn(k)); uvs.Add(Vector2.zero); uvs.Add(Vector2.one); }
            for (int k = 0; k < count; k++)
            {
                int a = b + k * 2, c = b + k * 2 + 1, d = b + (k + 1) * 2 + 1, f = b + (k + 1) * 2;
                var want = dirAt(k);
                Tri(verts, tris, a, c, d, want); Tri(verts, tris, a, d, f, want);
            }
        }
        // v=0 と v=1 の縁
        Rim(k => outer[k, 0], k => inner[k, 0], nu, Vector3.zero, k => outer[k, 0] - outer[k, 1]);
        Rim(k => outer[k, nv], k => inner[k, nv], nu, Vector3.zero, k => outer[k, nv] - outer[k, nv - 1]);
        if (!closedU)
        {
            Rim(k => outer[0, k], k => inner[0, k], nv, Vector3.zero, k => outer[0, k] - outer[1, k]);
            Rim(k => outer[nu, k], k => inner[nu, k], nv, Vector3.zero, k => outer[nu, k] - outer[nu - 1, k]);
        }
        return Finish(verts, tris, uvs);
    }

    // 中心線 C(t) に沿った楕円断面の筒（両端にふた）
    internal static Mesh Tube(int around, int along, Func<float, Vector3> C, Func<float, Vector2> radius)
    {
        var verts = new List<Vector3>(); var tris = new List<int>(); var uvs = new List<Vector2>();
        var ring = new Vector3[along + 1, around + 1];
        for (int j = 0; j <= along; j++)
        {
            float t = j / (float)along;
            Vector3 c = C(t);
            Vector3 tan = (C(Mathf.Min(1, t + 0.01f)) - C(Mathf.Max(0, t - 0.01f))).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, tan).normalized; if (side.sqrMagnitude < 0.01f) side = Vector3.right;
            Vector3 up = Vector3.Cross(tan, side).normalized;
            var r = radius(t);
            for (int i = 0; i <= around; i++)
            {
                float a = i / (float)around * Mathf.PI * 2f;
                ring[j, i] = c + side * Mathf.Cos(a) * r.y + up * Mathf.Sin(a) * r.x;
            }
        }
        int b = 0;
        for (int j = 0; j <= along; j++) for (int i = 0; i <= around; i++) { verts.Add(ring[j, i]); uvs.Add(new Vector2(i / (float)around, j / (float)along)); }
        for (int j = 0; j < along; j++) for (int i = 0; i < around; i++)
        {
            int a = b + j * (around + 1) + i, c = a + around + 1, d = c + 1, f = a + 1;
            Vector3 mid = C((j + 0.5f) / along);
            Vector3 want = ring[j, i] - mid;
            Tri(verts, tris, a, c, d, want); Tri(verts, tris, a, d, f, want);
        }
        foreach (int end in new[] { 0, along })
        {
            Vector3 c = C(end / (float)along);
            Vector3 dir = (C(end == 0 ? 0 : 1) - C(end == 0 ? 0.02f : 0.98f)).normalized;
            int cb = verts.Count; verts.Add(c); uvs.Add(Vector2.zero);
            int rb = verts.Count;
            for (int i = 0; i <= around; i++) { verts.Add(ring[end, i]); uvs.Add(Vector2.one); }
            for (int i = 0; i < around; i++) Tri(verts, tris, cb, rb + i, rb + i + 1, dir);
        }
        return Finish(verts, tris, uvs);
    }

    // 三角形を、法線が want の向きになるように並べて追加
    static void Tri(List<Vector3> v, List<int> t, int a, int b, int c, Vector3 want)
    {
        var n = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
        if (Vector3.Dot(n, want) < 0) { t.Add(a); t.Add(c); t.Add(b); }
        else { t.Add(a); t.Add(b); t.Add(c); }
    }

    static Mesh Finish(List<Vector3> v, List<int> t, List<Vector2> uv)
    {
        var m = new Mesh();
        if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.SetVertices(v); m.SetTriangles(t, 0); m.SetUVs(0, uv);
        m.RecalculateNormals(); m.RecalculateBounds(); m.RecalculateTangents();
        return m;
    }
}
