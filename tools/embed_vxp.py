"""Built-in games for the CoreS3 firmware: build/games/*.VXP -> esp32/src/games_builtin.h (games uploaded over Wi-Fi
to the device's flash are added on top, and replace a built-in copy of the same game).   python3 tools/embed_vxp.py"""
import glob, os
root = os.path.join(os.path.dirname(__file__), "..")
files = sorted(glob.glob(os.path.join(root, "build/games/*.VXP")))
out = ["// made by tools/embed_vxp.py from build/games/*.VXP; don't edit", "#include <stdint.h>", "#include <stddef.h>"]
for i, f in enumerate(files):
    d = open(f, "rb").read()
    out.append(f"static const uint8_t GAME{i}[{len(d)}] = {{" + ",".join(str(b) for b in d) + "};")
out.append("static const struct { const char *name; const uint8_t *data; size_t len; } BUILTIN[] = {")
for i, f in enumerate(files): out.append(f'  {{ "{os.path.basename(f)}", GAME{i}, sizeof GAME{i} }},')
out += ["};", f"#define NBUILTIN {len(files)}", ""]
open(os.path.join(root, "esp32/src/games_builtin.h"), "w").write("\n".join(out))
print(len(files), "built-in games")
