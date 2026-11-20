"""Read pw-dump JSON on stdin; print "id serial" of the node whose media.name is argv[1]."""
import json
import sys

name = sys.argv[1]
for o in json.load(sys.stdin):
    p = (o.get("info") or {}).get("props") or {}
    if o.get("type", "").endswith("Node") and p.get("media.name") == name:
        print(o["id"], p.get("object.serial", ""))
        break
