#!/usr/bin/env python3
"""Fetches Piper's catalogue (voices.json) and every voice's MODEL_CARD at the collection version
Modern Voices installs from, and prints, between two marker lines, one JSON object: for each voice
key, its language, quality, number of speakers, and the card's dataset, licence and training lines
as written. Run by .github/workflows/licences.yml (this container can't reach Hugging Face); the
result is classified by hand into tools/voice-licences.json (see tools/dev/make_licences.py)."""
import json, sys, time, urllib.request

BASE = "https://huggingface.co/rhasspy/piper-voices/resolve/v1.0.0/"

def get(url):
    for attempt in range(4):
        try:
            with urllib.request.urlopen(url, timeout=60) as r:
                return r.read().decode("utf-8", "replace")
        except Exception as e:
            if attempt == 3:
                return "FETCH FAILED: %s" % e
            time.sleep(2 + attempt * 3)

catalogue = json.loads(get(BASE + "voices.json"))
out = {}
for key, v in sorted(catalogue.items()):
    files = [f for f in v.get("files", {}) if f.endswith("MODEL_CARD")]
    card = get(BASE + files[0]) if files else "NO MODEL_CARD"
    lines = [l.strip() for l in card.splitlines() if l.strip()]
    keep = [l for l in lines if any(w in l.lower() for w in ("licen", "url", "train", "fine", "dataset", "from"))]
    out[key] = {"language": v.get("language", {}).get("code"), "quality": v.get("quality"),
                "speakers": v.get("num_speakers"), "card": keep, "whole": card if len(keep) < 2 else ""}
    print("fetched", key, file=sys.stderr)
print("=====LICENCES-BEGIN=====")
print(json.dumps(out, ensure_ascii=True))
print("=====LICENCES-END=====")
