"""FOLD ROOMS levels (microgames/fold/levels/*.fold) -> core/fold_levels.h, so the firmware carries them.
Usage: python3 tools/embed_levels.py [levels dir]"""
import glob, json, os, sys
d = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), "../../microgames/fold/levels")
files = sorted(glob.glob(os.path.join(d, "*.fold")))
out = ["// made by tools/embed_levels.py from microgames/fold/levels; don't edit", "static const char *const FOLD_LEVELS[] = {"]
for f in files:
    out.append("  " + json.dumps(open(f).read()) + ",")
out += ["};", "#define FOLD_NLEVELS %d" % len(files), ""]
open(os.path.join(os.path.dirname(__file__), "../core/fold_levels.h"), "w").write("\n".join(out))
print(len(files), "levels")
