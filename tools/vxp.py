"""GAME.VXP: one file per VMX game (vmx-world/docs/DESIGN.md "The package"). Little-endian.
   header 32 bytes: "VXP1", u32 game ID, u16 version, u16 flags, u32 total length, u32 CRC-32 of what follows the header,
   char[12] short name; then chunks: tag[4], u32 length, data padded to 4.
   NAME: lines "title", "publisher", "card line 1|card line 2", "blurb".  CARD: 24 x 24 1-bit, rows top first, MSB left.
   CODE: the runtime image (.vxi from sdk/vxlink).  FILE (any number): u8 name length, name, data (Microsoft.Vmx.Package).
Usage:
   vxp.py pack OUT.VXP --id 0x56581001 --title BLOX --lines BLOX --publisher "VMX Studio" --card card.txt --code blox.vxi [--blurb ...]
   vxp.py list FILE.VXP"""
import argparse, struct, sys, zlib

def pack(a):
    rows = [l.rstrip("\n") for l in open(a.card)][:24]
    card = bytearray(72)
    for y, row in enumerate(rows):
        for x, ch in enumerate(row[:24]):
            if ch == "#": card[y * 3 + x // 8] |= 0x80 >> (x % 8)
    name = "\n".join([a.title, a.publisher, a.lines or a.title, a.blurb or ""]).encode()
    code = open(a.code, "rb").read()
    body = b""
    chunks = [(b"NAME", name), (b"CARD", bytes(card)), (b"CODE", code)]
    for f in a.file or []:
        nm, path = f.split("=", 1); chunks.append((b"FILE", bytes([len(nm)]) + nm.encode() + open(path, "rb").read()))
    for tag, data in chunks:
        body += tag + struct.pack("<I", len(data)) + data + b"\0" * (-len(data) % 4)
    short = a.title.upper().encode()[:12].ljust(12, b"\0")
    head = b"VXP1" + struct.pack("<IHHII", int(a.id, 0), a.version, 0, 32 + len(body), zlib.crc32(body)) + short
    open(a.out, "wb").write(head + body)
    print(f"{a.out}: {32 + len(body)} bytes ({(32 + len(body) + 16383) // 16384} block)")

def listing(path):
    d = open(path, "rb").read()
    assert d[:4] == b"VXP1", "not a VXP"
    gid, ver, flags, total, crc = struct.unpack("<IHHII", d[4:20])
    print(f"{path}: id {gid:08X} v{ver} {total} bytes, CRC {'ok' if zlib.crc32(d[32:total]) == crc else 'BAD'}, name {d[20:32].rstrip(b'\0').decode()}")
    o = 32
    while o < total:
        tag, n = d[o:o + 4].decode(), struct.unpack("<I", d[o + 4:o + 8])[0]
        print(f"  {tag} {n} bytes" + (": " + d[o + 8:o + 8 + n].decode().replace("\n", " | ") if tag == "NAME" else ""))
        o += 8 + n + (-n % 4)

p = argparse.ArgumentParser(); sp = p.add_subparsers(dest="cmd", required=True)
k = sp.add_parser("pack"); k.add_argument("out")
for opt in ("--id", "--title", "--card", "--code"): k.add_argument(opt, required=True)
k.add_argument("--lines"); k.add_argument("--file", action="append", help="NAME=PATH, a data file the game reads with Package"); k.add_argument("--publisher", default="Homebrew"); k.add_argument("--blurb"); k.add_argument("--version", type=int, default=1)
l = sp.add_parser("list"); l.add_argument("file")
a = p.parse_args()
pack(a) if a.cmd == "pack" else listing(a.file)
