# -*- coding: utf-8 -*-
# 人型の姿勢人形：体格（寸法）を渡すと関節を作り、姿勢の指定から関節を解く
import math
from mathutils import Vector, Matrix
from rigcore import *


def xf(o, M):
    for v in o.data.vertices: v.co = M @ v.co
    if M.to_3x3().determinant() < 0: o.data.flip_normals()
    return o


class Humanoid(Rig):
    def __init__(self, name, line_mat, d, line_w=0.006):
        super().__init__(name, line_mat, line_w)
        self.d = d
        DOWN, BACK, FWD = V((0, 0, -1)), V((0, 1, 0)), V((0, -1, 0))
        self.joint("Root", None, (0, 0, 0))
        self.joint("Hips", "Root", (0, 0, d["pelvis"]))
        self.joint("Chest", "Hips", (0, 0, d["waist"]))
        self.joint("Head", "Chest", (0, 0, d["neck"]))
        for s, sg in (("L", 1), ("R", -1)):
            sh = V((sg * d["shX"], 0, d["shZ"]))
            el = sh + V((sg * 0.02, 0, -d["uarm"]))
            wr = el + V((sg * 0.01, 0, -d["larm"]))
            fa = frame(DOWN, BACK)
            self.joint("UpperArm" + s, "Chest", sh, fa)
            self.joint("LowerArm" + s, "UpperArm" + s, el, fa)
            self.joint("Hand" + s, "LowerArm" + s, wr, fa)
            hp = V((sg * d["hipX"], 0, d["hipZ"]))
            kn = V((sg * d["hipX"], 0, d["hipZ"] - d["uleg"]))
            an = V((sg * d["hipX"], 0, d["hipZ"] - d["uleg"] - d["lleg"]))
            fl = frame(DOWN, FWD)
            self.joint("UpperLeg" + s, "Hips", hp, fl)
            self.joint("LowerLeg" + s, "UpperLeg" + s, kn, fl)
            self.joint("Foot" + s, "LowerLeg" + s, an)
        self.ankleZ = d["hipZ"] - d["uleg"] - d["lleg"]
        self.free = []   # 親に付いていく追加の関節（布など）

    def free_joint(self, name, parent, pos, rot=None):
        self.joint(name, parent, pos, rot)
        self.free.append(name)

    def weapon_joint(self, name, rest):
        """Root の子。姿勢ごとに置き場所を決める武器"""
        self.joints[name] = dict(parent="Root", rest=rest.copy())
        self.order.append(name)

    def rest_of(self, n): return self.joints[n]["rest"]

    # ------------------------------------------------------------ 姿勢を解く
    def solve(self, sp):
        d = self.d
        P = {"Root": Matrix.Identity(4)}
        ho = V(sp.get("hips", (0, 0, 0)))
        P["Hips"] = M4(rot_ypr(*sp.get("hips_rot", (0, 0, 0))), V((0, 0, d["pelvis"])) + ho)
        self.follow(P, "Chest", rot_ypr(*sp.get("chest", (0, 0, 0))))
        self.follow(P, "Head", rot_ypr(*sp.get("head", (0, 0, 0))))

        # 足
        for s, sg in (("L", 1), ("R", -1)):
            f = sp.get("foot" + s, {})
            fp = V(f.get("pos", (sg * d["hipX"], 0, 0)))
            yaw = f.get("yaw", 0); pitch = f.get("pitch", 0)
            ankle = fp + V((0, 0, self.ankleZ + f.get("lift", 0)))
            hipW = (P["Hips"] @ (self.rest_of("Hips").inverted() @ self.rest_of("UpperLeg" + s))).translation
            fr = rot_ypr(0, yaw, 0)
            pole = V(f.get("knee", fr @ V((sg * 0.25, -1, 0))))
            knee, end = ik2(hipW, ankle, d["uleg"], d["lleg"], pole)
            P["UpperLeg" + s] = M4(frame(knee - hipW, pole), hipW)
            P["LowerLeg" + s] = M4(frame(end - knee, pole), knee)
            P["Foot" + s] = M4(rot_ypr(pitch, yaw, 0), end)

        # 布など
        cl = sp.get("cloth", {})
        for n in self.free:
            c = cl.get(n)
            self.follow(P, n, rot_ypr(*c) if c else None)

        self.solve_weapons(P, sp)

        # 腕
        for s, sg in (("L", 1), ("R", -1)):
            h = sp.get("hand" + s)
            shW = (P["Chest"] @ (self.rest_of("Chest").inverted() @ self.rest_of("UpperArm" + s))).translation
            pole = V(sp.get("elbow" + s, (sg * 0.6, 0.8, -0.3)))
            tgt = self.hand_target(P, sp, s, h)
            if tgt is None:   # 力を抜いて下げる
                c = P["Chest"].to_3x3()
                gp = shW + c @ V((sg * 0.07, 0.02, -(d["uarm"] + d["larm"] + d["palm"]) * 0.985))
                gd = None
            else:
                gp, gd = tgt
            gp = V(gp)
            fore = (gp - shW).normalized()
            for _ in range(3):
                if gd is not None:
                    y = fore - gd * fore.dot(gd)
                    if y.length < 1e-4: y = V((0, 0, -1))
                    y.normalize()
                else:
                    y = fore
                wrist = gp - y * d["palm"]
                elbow, end = ik2(shW, wrist, d["uarm"], d["larm"], pole)
                fore = (end - elbow).normalized()
            P["UpperArm" + s] = M4(frame(elbow - shW, pole), shW)
            P["LowerArm" + s] = M4(frame(end - elbow, pole), elbow)
            if gd is not None:
                y = fore - gd * fore.dot(gd); y.normalize()
                P["Hand" + s] = M4(frame(y, -gd), end)
            else:
                P["Hand" + s] = M4(frame(fore, pole), end)
        return P

    # 既定：武器なし。キャラごとに上書きする
    def solve_weapons(self, P, sp): pass

    def hand_target(self, P, sp, s, h):
        if h is None: return None
        if isinstance(h, dict): return V(h["pos"]), (V(h["dir"]).normalized() if "dir" in h else None)
        return None
