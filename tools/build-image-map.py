#!/usr/bin/env python3
"""Regenerate CP2077SaveEditor/Resources/ImageMap.json from the Cyberpunk wiki.

Run this when the game (or the wiki) changes; the editor ships the generated map
and never scrapes at runtime -- it only fetches the image bytes a user actually
looks at, and caches those on disk.

    python tools/build-image-map.py

How the two halves are matched:

  Weapons   The wiki infobox carries `|baseid = Preset_Masamune_Arasaka_2077`,
            which is the TweakDBID minus its `Items.` prefix. That is an exact
            key -- no guessing. A page may list several ids separated by <br>.

  Vehicles  Vehicle infoboxes have no baseid, so these are matched on words:
            `Vehicle.v_sport1_rayfield_caliburn_player` -> {rayfield, caliburn}
            against the page title. The score (shared words / id words) is kept
            in the map so a bad match can be spotted rather than silently shown.
            Variants legitimately collapse onto their base model's page, and a
            few manufacturers differ internally from their in-game name
            (ids say "chevalier", the game says "Chevillon").
"""
import json
import os
import re
import sys
import time
import urllib.parse
import urllib.request

API = "https://cyberpunk.fandom.com/api.php"
UA = ("cp2077-saveeditor-imagemap/1.0 "
      "(+https://github.com/exexexexe/CyberCAT-SimpleGUI-2.31)")

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
VEHICLES_JSON = os.path.join(ROOT, "CP2077SaveEditor", "Resources", "Vehicles.json")
OUT_JSON = os.path.join(ROOT, "CP2077SaveEditor", "Resources", "ImageMap.json")

WEAPON_CATEGORIES = (
    "Category:Cyberpunk 2077 Iconic weapons",
    "Category:Cyberpunk 2077 Weapons",
)

VEHICLE_CATEGORIES = (
    "Category:Cyberpunk 2077 Vehicles",
    "Category:Cyberpunk 2077 Cars",
    "Category:Cyberpunk 2077 Motorcycles",
    "Category:Cyberpunk 2077 Trucks",
    "Category:Cyberpunk 2077 Heavy Vehicles",
)

# Words in an id that say nothing about which vehicle it is.
STOP = {"player", "nomad", "heist", "mrhands", "netrunner", "tyger", "basic",
        "default", "v", "02", "03", "04", "missiles", "tuned"}

MIN_SCORE = 0.5


def api(params):
    params["format"] = "json"
    url = API + "?" + urllib.parse.urlencode(params)
    req = urllib.request.Request(url, headers={"User-Agent": UA})
    for attempt in range(4):
        try:
            with urllib.request.urlopen(req, timeout=45) as r:
                d = json.load(r)
            if "error" in d:
                raise RuntimeError("API error: %s" % d["error"].get("info", d["error"]))
            return d
        except Exception:
            if attempt == 3:
                raise
            time.sleep(1.5 * (attempt + 1))


def pages_with_text(category):
    out, cont = {}, {}
    while True:
        p = {
            "action": "query",
            "generator": "categorymembers",
            "gcmtitle": category,
            "gcmtype": "page",
            "gcmlimit": "50",
            "prop": "revisions",
            "rvprop": "content",
            "rvslots": "main",
        }
        p.update(cont)
        d = api(p)
        for page in d.get("query", {}).get("pages", {}).values():
            revs = page.get("revisions")
            if revs:
                out[page["title"]] = revs[0]["slots"]["main"]["*"]
        if "continue" not in d:
            return out
        cont = d["continue"]


def image_urls(file_titles):
    """File title -> direct CDN url, 50 at a time."""
    urls, titles = {}, sorted(t for t in file_titles if "|" not in t)
    for i in range(0, len(titles), 50):
        chunk = titles[i:i + 50]
        d = api({
            "action": "query",
            "titles": "|".join(chunk),
            "prop": "imageinfo",
            "iiprop": "url",
        })
        pages = d.get("query", {}).get("pages", {})
        norm = {n["to"]: n["from"] for n in d.get("query", {}).get("normalized", [])}
        for page in pages.values():
            info = page.get("imageinfo")
            if not info:
                continue
            title = page["title"]
            url = info[0]["url"].split("/revision/")[0]
            urls[title] = url
            if title in norm:
                urls[norm[title]] = url
    return urls


def clean_image(value):
    """An infobox image value can be bare, wrapped, or carry display options:
    `X.png`, `[[File:X.png|250px]]`, `X.png|thumb`. Reduce it to the file name.
    The pipe matters: left in, it injects extra values into a batched API query
    and the whole request is rejected."""
    if not value:
        return None
    value = re.sub(r"^\[\[\s*(?:File|Image)\s*:", "", value, flags=re.I)
    value = value.split("|")[0].replace("]]", "").strip()
    return value or None


def field(text, name):
    m = re.search(r"^\s*\|\s*%s\s*=\s*(.+?)\s*$" % name, text, re.M | re.I)
    return m.group(1).strip() if m else None


def norm_words(s):
    s = (s.replace("é", "e").replace("ü", "u")
          .replace("ä", "a").replace("ö", "o"))
    s = re.sub(r"[^a-z0-9]+", " ", s.lower())
    # "911turbo" -> "911 turbo", "type66" -> "type 66"
    return re.sub(r"(?<=[a-z])(?=\d)|(?<=\d)(?=[a-z])", " ", s)


def toks(s):
    return {t for t in norm_words(s).split() if t and t not in STOP}


def vehicle_tokens(vid):
    body = vid.split(".", 1)[1] if "." in vid else vid
    return toks(re.sub(r"^v_[a-z]*\d*_", "", body))


def main():
    entries = {}

    print("weapons:")
    weapon_pages = {}
    for cat in WEAPON_CATEGORIES:
        got = pages_with_text(cat)
        print("  %-44s %3d pages" % (cat.replace("Category:", ""), len(got)))
        for k, v in got.items():
            weapon_pages.setdefault(k, v)

    weapon_files = set()
    pending = []
    for title, text in weapon_pages.items():
        img, base = clean_image(field(text, "image")), field(text, "baseid")
        if not img or not base:
            continue
        for bid in re.split(r"<br\s*/?>|,", base):
            bid = bid.strip()
            if not bid or " " in bid:
                continue
            pending.append(("Items." + bid, title, img))
            weapon_files.add("File:" + img)

    print("vehicles:")
    vehicle_pages = {}
    for cat in VEHICLE_CATEGORIES:
        got = pages_with_text(cat)
        print("  %-44s %3d pages" % (cat.replace("Category:", ""), len(got)))
        for k, v in got.items():
            vehicle_pages.setdefault(k, v)

    vehicle_imgs = {t: clean_image(field(x, "image")) for t, x in vehicle_pages.items()}
    vehicle_imgs = {t: i for t, i in vehicle_imgs.items() if i}

    vids = sorted(set(json.load(open(VEHICLES_JSON, encoding="utf-8"))))
    cand = {t: toks(t) for t in vehicle_imgs}

    vehicle_hits, unmatched = [], []
    for vid in vids:
        vt = vehicle_tokens(vid)
        best, score = None, 0.0
        for title, tt in cand.items():
            if not tt or not vt:
                continue
            shared = len(vt & tt)
            if not shared:
                continue
            s = shared / len(vt)
            if s > score:
                best, score = title, s
        if score >= MIN_SCORE:
            vehicle_hits.append((vid, best, vehicle_imgs[best], round(score, 2)))
            weapon_files.add("File:" + vehicle_imgs[best])
        else:
            unmatched.append(vid)

    print("\nresolving image urls...")
    urls = image_urls(weapon_files)

    missing = 0
    missed_examples = []
    for key, title, img in pending:
        url = urls.get("File:" + img)
        if not url:
            missing += 1
            if len(missed_examples) < 6:
                missed_examples.append((key, img))
            continue
        entries[key] = {"n": title, "u": url}

    for vid, title, img, score in vehicle_hits:
        url = urls.get("File:" + img)
        if not url:
            missing += 1
            continue
        e = {"n": title, "u": url}
        if score < 1.0:
            e["s"] = score
        entries[vid] = e

    out = {
        "note": "Generated by tools/build-image-map.py. Do not edit by hand.",
        "source": "cyberpunk.fandom.com",
        "generated": time.strftime("%Y-%m-%d"),
        "entries": entries,
    }
    with open(OUT_JSON, "w", encoding="utf-8") as f:
        json.dump(out, f, indent=1, ensure_ascii=False, sort_keys=True)

    weapons = sum(1 for k in entries if k.startswith("Items."))
    vehicles = sum(1 for k in entries if k.startswith("Vehicle."))
    print("\nweapon ids : %d" % weapons)
    print("vehicle ids: %d of %d (%.0f%%)"
          % (vehicles, len(vids), 100.0 * vehicles / len(vids)))
    if unmatched:
        print("unmatched  : %s" % ", ".join(v.replace("Vehicle.", "") for v in unmatched))
    if missing:
        print("dropped (no image url): %d" % missing)
        for k, i in missed_examples:
            print("    %-46s image=%r" % (k, i))
        print("    urls dict holds %d keys" % len(urls))
    print("wrote %s" % os.path.relpath(OUT_JSON, ROOT))


if __name__ == "__main__":
    sys.exit(main())
