# -*- coding: utf-8 -*-
"""把导出的原版 sprite 缩成 Gizmo 图标（保持比例、保留透明通道）。"""
import os
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
REAL = r"D:\HKModding\hkmod-custom-scene\UnityProject\HKModCustomScene\Assets\Gizmos"
SKEL = r"D:\HKModding\hkmod-custom-scene\UnityProject\Assets\Gizmos"

JOBS = [
    (os.path.join(HERE, "export", "sharedassets76", "bone_bench.png"), "HKCS_Bench.png"),
    (os.path.join(HERE, "export", "sharedassets389", "fly0000.png"),  "HKCS_Enemy.png"),
]

for src, name in JOBS:
    if not os.path.exists(src):
        print("缺文件:", src); continue
    im = Image.open(src).convert("RGBA")
    w = 64
    h = max(1, round(im.height * w / im.width))
    small = im.resize((w, h), Image.LANCZOS)
    for d in (REAL, SKEL):
        os.makedirs(d, exist_ok=True)
        small.save(os.path.join(d, name))
    print("{}  <- {}  源 {}  -> 图标 {}   alpha 范围 {}".format(
        name, os.path.basename(src), im.size, small.size, small.getchannel("A").getextrema()))
