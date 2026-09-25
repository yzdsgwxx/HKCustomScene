# -*- coding: utf-8 -*-
"""扫 HK 的 assets，找名字含关键字的 Texture2D / Sprite（给 Gizmo 图标找原版长椅图用）。
用法： python find_assets.py <关键字> [关键字2 ...]
"""
import os, re, sys, glob, json
import UnityPy

DATA = r"D:\APP\steam.exe\steamapps\common\Hollow Knight\hollow_knight_Data"
KEYS = [k.lower() for k in (sys.argv[1:] or ["bench"])]

def main():
    hits = []
    files = sorted(glob.glob(os.path.join(DATA, "*.assets")))
    print("assets 文件:", [os.path.basename(f) for f in files], flush=True)
    for f in files:
        base = os.path.basename(f)
        try:
            env = UnityPy.load(f)
        except Exception as e:
            print("  载入失败", base, e, flush=True)
            continue
        counts = {}
        for obj in env.objects:
            counts[obj.type.name] = counts.get(obj.type.name, 0) + 1
            if obj.type.name not in ("Texture2D", "Sprite"):
                continue
            try:
                d = obj.read()
            except Exception:
                continue
            name = getattr(d, "m_Name", "") or ""
            if not name:
                continue
            low = name.lower()
            if any(k in low for k in KEYS):
                hits.append({"file": base, "type": obj.type.name, "name": name, "path_id": obj.path_id})
        print("  {:22s} 对象数 {:6d}  Sprite={:5d} Texture2D={:5d}".format(
            base, sum(counts.values()), counts.get("Sprite", 0), counts.get("Texture2D", 0)), flush=True)
    print("\n=== 命中 {} 条 ===".format(len(hits)))
    for h in hits[:200]:
        print("  {file:22s} {type:10s} {name}".format(**h))
    out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "hits.json")
    with open(out, "w", encoding="utf-8") as fp:
        json.dump(hits, fp, ensure_ascii=False, indent=1)
    print("明细写到", out)

if __name__ == "__main__":
    main()
