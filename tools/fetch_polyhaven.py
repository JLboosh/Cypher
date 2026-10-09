#!/usr/bin/env python3
"""Downloads the CC0 Poly Haven models and textures used to dress the Cypher workshop.

Everything on Poly Haven is CC0 (public domain): free for any use, no attribution required
(credits are still listed in CREDITS.md). Models: FBX + 1k texture maps (JPG/PNG instead of
the much larger EXR versions). Textures: 1k diffuse / normal (GL) / ARM (AO-rough-metal) JPGs.

Usage:  python3 tools/fetch_polyhaven.py   (from ~/cypher). Already-downloaded files are skipped.
"""
import json
import os
import sys
import urllib.request

UA = {"User-Agent": "CypherWorkshop-personal-setup/1.0"}
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..",
                    "CypherWorkshop", "Assets", "Cypher", "ThirdParty", "PolyHaven")

MODELS = [
    # furniture & machines
    "steel_frame_shelves_01", "steel_frame_shelves_02", "metal_tool_chest", "WoodenTable_03",
    "metal_office_desk", "metal_stool_01", "portable_welding_cart", "drill_press_01", "power_box_01",
    # lighting
    "hanging_industrial_lamp", "mounted_fluorescent_lights", "industrial_wall_lamp",
    # bench props
    "bench_vice_01", "Drill_01", "adjustable_wrench", "combination_wrench", "pliers", "screwdrivers_02",
    "cross_pein_hammer", "measuring_tape_01", "retro_multimeter", "circuit_board", "desk_lamp_arm_01",
    "metal_toolbox", "small_oil_can_01", "spray_paint_bottles",
    # storage & clutter
    "cardboard_box_01", "plastic_crate_03", "old_military_crate", "propane_tank", "korean_fire_extinguisher_01",
    # architecture details
    "rollershutter_door", "modular_industrial_pipes_01", "modular_electric_cables",
]

TEXTURES = ["garage_floor", "concrete_block_wall", "corrugated_iron_02", "blue_metal_plate"]


def get_json(url):
    return json.load(urllib.request.urlopen(urllib.request.Request(url, headers=UA)))


def download(url, path):
    if os.path.exists(path) and os.path.getsize(path) > 0:
        return False
    os.makedirs(os.path.dirname(path), exist_ok=True)
    req = urllib.request.Request(url, headers=UA)
    with urllib.request.urlopen(req) as r, open(path + ".part", "wb") as f:
        f.write(r.read())
    os.replace(path + ".part", path)
    return True


def fetch_model(asset_id):
    files = get_json(f"https://api.polyhaven.com/files/{asset_id}")
    fbx = files["fbx"]["1k"]["fbx"]
    folder = os.path.join(ROOT, "Models", asset_id)
    download(fbx["url"], os.path.join(folder, asset_id + ".fbx"))
    for name, info in fbx.get("include", {}).items():
        url, filename = info["url"], os.path.basename(name)
        if filename.endswith(".exr"):  # use the lighter JPG version of the same map
            url = url.replace("/exr/", "/jpg/").replace(".exr", ".jpg")
            filename = filename[:-4] + ".jpg"
        download(url, os.path.join(folder, "textures", filename))


def fetch_texture(asset_id):
    files = get_json(f"https://api.polyhaven.com/files/{asset_id}")
    folder = os.path.join(ROOT, "Textures", asset_id)
    for key, suffix in (("Diffuse", "diff"), ("nor_gl", "nor_gl"), ("arm", "arm")):
        if key in files:
            download(files[key]["1k"]["jpg"]["url"], os.path.join(folder, f"{asset_id}_{suffix}_1k.jpg"))


def main():
    failures = []
    for i, m in enumerate(MODELS, 1):
        try:
            fetch_model(m)
            print(f"[{i}/{len(MODELS)}] model {m}")
        except Exception as e:  # keep going; report at the end
            failures.append((m, e))
    for t in TEXTURES:
        try:
            fetch_texture(t)
            print(f"texture {t}")
        except Exception as e:
            failures.append((t, e))
    for name, e in failures:
        print(f"FAILED {name}: {e}", file=sys.stderr)
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
