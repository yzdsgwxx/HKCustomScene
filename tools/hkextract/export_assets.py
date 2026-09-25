# -*- coding: utf-8 -*-
"""从 HK 的 assets 里导出 Sprite / Texture2D 为 PNG。

用法：
    python export_assets.py <关键字> [关键字2 ...]      # 导出名字含任一关键字的
    python export_assets.py --all                      # 导出全部 Sprite + Texture2D（供以后用，量大）
输出：
    <本脚本目录>\export\<assets文件名>\<对象名>.png
    （Sprite 会被裁成它自己的区域；同时把每个 Sprite 的尺寸打进清单 sprites.json）
"""
import os, re, sys, glob, json

import UnityPy

DATA = r"D:\APP\steam.exe\steamapps\common\Hollow Knight\hollow_knight_Data"
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "export")


def safe(name: str) -> str:
    return re.sub(r'[<>:"/\\|?*]', "_", name).strip() or "unnamed"


def main():
    args = sys.argv[1:]
    export_all = "--all" in args
    keys = [a.lower() for a in args if not a.startswith("--")]

    manifest = []
    files = sorted(glob.glob(os.path.join(DATA, "*.assets")))
    total = 0
    for f in files:
        base = os.path.basename(f)
        try:
            env = UnityPy.load(f)
        except Exception as e:
            print("  载入失败", base, e, flush=True)
            continue

        # 先收集本文件里命中的对象（Sprite 优先：裁好的小图；同名 Texture2D 也留一份原始图集）
        picked = []
        for obj in env.objects:
            if obj.type.name not in ("Texture2D", "Sprite"):
                continue
            try:
                d = obj.read()
            except Exception:
                continue
            name = getattr(d, "m_Name", "") or ""
            if not name:
                continue
            if not export_all and not any(k in name.lower() for k in keys):
                continue
            if export_all and obj.type.name == "Texture2D":
                # --all 时只导 Sprite（Texture2D 图集可由 Sprite 的导出覆盖大部分需求，避免重复与体积爆炸）
                continue
            picked.append((obj.type.name, name, d))

        if not picked:
            continue

        dstdir = os.path.join(OUT, os.path.splitext(base)[0])
        os.makedirs(dstdir, exist_ok=True)
        for typename, name, d in picked:
            try:
                img = d.image          # PIL.Image（Sprite 会按它的区域裁好）
            except Exception as e:
                print("  跳过 {} {} ({})".format(typename, name, e), flush=True)
                continue
            path = os.path.join(dstdir, safe(name) + ".png")
            img.save(path)
            manifest.append({"file": base, "type": typename, "name": name,
                             "w": img.width, "h": img.height, "png": os.path.relpath(path, HERE)})
            total += 1
        print("  {:22s} 导出 {:4d} 个".format(base, len(picked)), flush=True)

    # --all 时把 Sprite 的原始图集信息也记下来，方便以后按需再取
    out_json = os.path.join(HERE, "sprites_all.json" if export_all else "sprites.json")
    with open(out_json, "w", encoding="utf-8") as fp:
        json.dump(manifest, fp, ensure_ascii=False, indent=1)
    print("\n共导出 {} 个图 → {}".format(total, OUT))
    print("清单 →", out_json)


if __name__ == "__main__":
    main()
