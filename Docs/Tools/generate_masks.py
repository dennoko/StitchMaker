"""StitchMaker 同梱マスクの生成スクリプト。

    python generate_masks.py [出力フォルダ]     (既定: ../../Masks)

マスクはグレースケール = 高さ（0 = 布地、255 = 糸の頂点）。U 方向（横）がパスの進行方向。
すべて 64 px/mm を基準に描いており（Twisted のみ 128 px/mm）、プリセットの length / width は
画像の横 / 縦のピクセル数と同じ比率にしてある。必要: numpy, Pillow
"""
import math
import os
import sys

import numpy as np
from PIL import Image

SS = 3  # スーパーサンプリング倍率


class Canvas:
    def __init__(self, width, height):
        self.width, self.height = width, height
        ys, xs = np.mgrid[0:height * SS, 0:width * SS]
        self.x = (xs + 0.5) / SS
        self.y = (ys + 0.5) / SS
        self.h = np.zeros_like(self.x)

    def stroke(self, points, r, hfun=None, rfun=None, profile="round", twist=None):
        """折れ線に沿った糸を描く。重なりは高い方が勝つ。

        hfun / rfun: 正規化した弧長 (0-1) を受け取り、高さ / 半径の倍率を返す関数。
        twist: (ピッチ px, 溝の深さ 0-1, 傾き) で撚りの溝を付ける。
        """
        pts = np.asarray(points, dtype=float)
        seg = np.diff(pts, axis=0)
        seg_len = np.hypot(seg[:, 0], seg[:, 1])
        start = np.concatenate([[0.0], np.cumsum(seg_len)])
        total = start[-1]

        best = np.full(self.x.shape, np.inf)   # 中心線までの距離
        arc = np.zeros_like(self.x)            # 最近傍点の弧長
        side = np.zeros_like(self.x)           # 符号付きの横ずれ
        for i in range(len(seg)):
            if seg_len[i] < 1e-9:
                continue
            dx, dy = seg[i] / seg_len[i]
            px, py = self.x - pts[i, 0], self.y - pts[i, 1]
            t = np.clip(px * dx + py * dy, 0.0, seg_len[i])
            dist = np.hypot(px - t * dx, py - t * dy)
            closer = dist < best
            best = np.where(closer, dist, best)
            arc = np.where(closer, start[i] + t, arc)
            side = np.where(closer, px * dy - py * dx, side)

        s = arc / total
        radius = r * (rfun(s) if rfun else 1.0)
        d = np.clip(best / radius, 0.0, 1.0)
        if profile == "round":      # 丸い断面
            height = np.sqrt(1.0 - d * d)
        elif profile == "full":     # ふっくらした太糸
            height = (1.0 - d * d) ** 0.35
        elif profile == "flat":     # 平紐（上面が平ら）
            height = np.minimum(1.0, 1.7 * np.sqrt(1.0 - d * d))
        else:
            raise ValueError(profile)
        if hfun:
            height = height * hfun(s)
        if twist:
            pitch, depth, slant = twist
            groove = (0.5 + 0.5 * np.cos(2.0 * math.pi * (arc + slant * side) / pitch)) ** 3
            height = height * (1.0 - depth * groove)
        self.h = np.maximum(self.h, np.where(best < radius, height, 0.0))

    def save(self, path, tileable):
        img = self.h.reshape(self.height, SS, self.width, SS).mean(axis=(1, 3))
        img = np.clip(np.rint(img * 255.0), 0, 255).astype(np.uint8)
        # タイルとして繋がらない辺は必ず黒（高さ 0）で終わらせる。
        assert img[0].max() == 0 and img[-1].max() == 0, path + ": 上下の端が黒くない"
        if not tileable:
            assert img[:, 0].max() == 0 and img[:, -1].max() == 0, path + ": 左右の端が黒くない"
        Image.fromarray(img).save(path, optimize=True)
        print("%-18s %3d x %3d%s" % (os.path.basename(path), self.width, self.height, "  (tileable)" if tileable else ""))


def smoothstep(x):
    x = np.clip(x, 0.0, 1.0)
    return x * x * (3.0 - 2.0 * x)


def taper(end=0.6, zone=0.2, at_start=True, at_end=True):
    """両端が布地に沈み込む高さの変化。end = 端での倍率、zone = 沈み込む区間の割合。"""
    def f(s):
        k = np.ones_like(s)
        if at_start:
            k = k * (end + (1.0 - end) * smoothstep(s / zone))
        if at_end:
            k = k * (end + (1.0 - end) * smoothstep((1.0 - s) / zone))
        return k
    return f


def ramp(h0, h1, base=None):
    """弧長に沿って高さを h0 → h1 に変える（重なりの上下関係を作る）。"""
    def f(s):
        k = h0 + (h1 - h0) * s
        return k * base(s) if base else k
    return f


def straight_thread(c, y, x0=32, x1=160, r=16):
    c.stroke([(x0, y), (x1, y)], r, hfun=taper(0.6, 0.2))


# ---------------------------------------------------------------- 基本

def straight():
    c = Canvas(192, 48)
    straight_thread(c, 24)
    return c, False


def double():
    c = Canvas(192, 224)
    for y in (24, 200):
        straight_thread(c, y)
    return c, False


def triple():
    c = Canvas(192, 256)
    for y in (24, 128, 232):
        straight_thread(c, y)
    return c, False


def double_narrow():
    # 糸の隙間を糸 1 本分程度まで詰めた 2 本線
    c = Canvas(192, 112)
    for y in (24, 88):
        straight_thread(c, y)
    return c, False


def triple_narrow():
    c = Canvas(192, 176)
    for y in (24, 88, 152):
        straight_thread(c, y)
    return c, False


def thick():
    c = Canvas(320, 96)
    c.stroke([(52, 48), (268, 48)], 38, hfun=taper(0.6, 0.18), profile="full", twist=(52, 0.14, 0.9))
    return c, False


# ---------------------------------------------------------------- 糸の質感

def twisted():
    c = Canvas(384, 96)  # 128 px/mm
    c.stroke([(46, 48), (338, 48)], 34, hfun=taper(0.6, 0.18), twist=(46, 0.45, 0.9))
    return c, False


def flat_waxed():
    c = Canvas(256, 80)
    c.stroke([(40, 40), (216, 40)], 30, hfun=taper(0.75, 0.3), profile="flat")
    return c, False


# ---------------------------------------------------------------- かがり・端処理

def zigzag():
    c = Canvas(192, 192)
    dip = taper(0.7, 0.2)
    c.stroke([(0, 160), (96, 32)], 13, hfun=dip)
    c.stroke([(96, 32), (192, 160)], 13, hfun=dip)
    return c, True


def overlock():
    c = Canvas(96, 288)
    dip = taper(0.75, 0.15)
    # ルーパー糸（布端と針糸の間を往復する）
    c.stroke([(0, 22), (48, 266)], 10, hfun=ramp(0.8, 0.8, dip))
    c.stroke([(48, 266), (96, 22)], 10, hfun=ramp(0.8, 0.8, dip))
    # 布端で絡み合う糸と、内側を走る針糸
    c.stroke([(0, 22), (96, 22)], 12, hfun=lambda s: 0.85 + 0.15 * np.cos(2.0 * math.pi * s))
    c.stroke([(0, 266), (96, 266)], 12, hfun=lambda s: 0.85 - 0.15 * np.cos(2.0 * math.pi * s))
    return c, True


def blanket():
    c = Canvas(256, 320)
    c.stroke([(128, 34), (128, 290)], 15, hfun=ramp(0.9, 0.9, taper(0.55, 0.15, at_start=False)))
    c.stroke([(0, 34), (256, 34)], 15)
    return c, True


def whip():
    c = Canvas(192, 320)
    c.stroke([(36, 294), (156, 26)], 17, hfun=taper(0.45, 0.18))
    return c, False


# ---------------------------------------------------------------- 装飾

def cross():
    c = Canvas(224, 224)
    c.stroke([(34, 190), (190, 34)], 15, hfun=ramp(0.8, 0.8, taper(0.6, 0.18)))
    c.stroke([(34, 34), (190, 190)], 15, hfun=taper(0.6, 0.18))
    return c, False


def double_cross():
    c = Canvas(288, 288)
    dip = taper(0.6, 0.15)
    c.stroke([(44, 244), (244, 44)], 13, hfun=ramp(0.7, 0.7, dip))
    c.stroke([(44, 44), (244, 244)], 13, hfun=ramp(0.8, 0.8, dip))
    c.stroke([(144, 24), (144, 264)], 13, hfun=ramp(0.9, 0.9, dip))
    c.stroke([(24, 144), (264, 144)], 13, hfun=dip)
    return c, False


def chain():
    c = Canvas(230, 116)
    # しずく形の輪。左端が尖り、右端が丸い。次の輪の先端が上に重なるよう先端側を高くする。
    theta = np.linspace(0.0, 2.0 * math.pi, 97)
    xs = 115.0 - 98.0 * np.cos(theta)
    ys = 58.0 + 50.0 * np.sin(theta) * np.sin(theta / 2.0) ** 1.5
    c.stroke(list(zip(xs, ys)), 11, hfun=lambda s: 1.0 - 0.22 * np.sin(math.pi * s))
    return c, False


def baseball():
    c = Canvas(192, 320)
    dip = taper(0.5, 0.2)
    # 中央の合わせ目から左右へ交互に出る斜めの糸
    c.stroke([(22, 148), (104, 26)], 16, hfun=dip)
    c.stroke([(88, 172), (170, 294)], 16, hfun=dip)
    return c, False


def herringbone():
    c = Canvas(320, 256)
    # 間隔を length の 0.68 倍にすると、上下の交点が等間隔に並ぶ。
    over_under = ramp(1.0, 0.78, taper(0.7, 0.12))
    c.stroke([(26, 230), (186, 26)], 13, hfun=over_under)
    c.stroke([(134, 26), (294, 230)], 13, hfun=over_under)
    return c, False


# ---------------------------------------------------------------- 特殊

def rough():
    c = Canvas(256, 96)
    # 太さが不揃いで、少しうねった手縫いの太糸
    lumpy = lambda s: 0.84 + 0.13 * np.sin(2.0 * math.pi * (1.6 * s + 0.15)) + 0.06 * np.sin(2.0 * math.pi * (4.3 * s + 0.4))
    c.stroke([(44, 50), (86, 44), (130, 51), (176, 45), (212, 49)], 31,
             hfun=taper(0.5, 0.2), rfun=lumpy, profile="full", twist=(64, 0.32, 0.8))
    return c, False


MASKS = {
    "Straight": straight,
    "Double": double,
    "Triple": triple,
    "DoubleNarrow": double_narrow,
    "TripleNarrow": triple_narrow,
    "Thick": thick,
    "Twisted": twisted,
    "FlatWaxed": flat_waxed,
    "Zigzag": zigzag,
    "Overlock": overlock,
    "Blanket": blanket,
    "Whip": whip,
    "Cross": cross,
    "DoubleCross": double_cross,
    "Chain": chain,
    "Baseball": baseball,
    "Herringbone": herringbone,
    "Rough": rough,
}


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(here, "..", "..", "Masks")
    os.makedirs(out, exist_ok=True)
    for name, build in MASKS.items():
        canvas, tileable = build()
        canvas.save(os.path.join(out, name + ".png"), tileable)


if __name__ == "__main__":
    main()
