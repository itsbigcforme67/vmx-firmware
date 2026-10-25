r"""FOLD ROOMS' levels (microgames/fold/levels/*.fold, in name order, as the firmware's C version had them) -> games/fold/levels.bin,
the FILE chunk "levels" in FOLDROOM.VXP. Layout: u16 count, then count + 1 u32 offsets from the start of the file
(level i is bytes off[i] .. off[i + 1]), then the level texts as they are. Run again after changing a level.
The same layout is FOLD ROOMS' DATA\LEVELS.BIN, the levels made in FOLD BUILDER (VMX World packs it on the Xbox):
  python3 games/fold/levels.py DIR OUT 'user-*.fold'
Usage: python3 games/fold/levels.py [levels dir [out [pattern]]]"""
import glob, os, struct, sys
here = os.path.dirname(os.path.abspath(__file__))
d = sys.argv[1] if len(sys.argv) > 1 else os.path.join(here, "../../../microgames/fold/levels")
out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(here, "levels.bin")
texts = [open(f, "rb").read() for f in sorted(glob.glob(os.path.join(d, sys.argv[3] if len(sys.argv) > 3 else "*.fold")))]
off = [2 + 4 * (len(texts) + 1)]
for t in texts: off.append(off[-1] + len(t))
open(out, "wb").write(struct.pack("<H", len(texts)) + b"".join(struct.pack("<I", o) for o in off) + b"".join(texts))
print(len(texts), "levels,", off[-1], "bytes")
