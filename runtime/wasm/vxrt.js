// The VMX runtime in the browser (build/vxrt.wasm, from runtime/wasm/build.sh): runs a GAME.VXP as the VMX does.
// The VMX simulator (POCKET ARCADE vmx/) has a copy, made by its tools/vmx-sync.sh; tools/parity/wasm_run.mjs
// checks it against the PC build.
//   const rt = await vxRuntime(fetch("vxrt.wasm"))     the compiled runtime (one for every game)
//   const info = readVxp(bytes)                        { id, casino, wld, title, publisher, lines, blurb, card } or null
//   const vm = vxGame(rt, bytes, host)                 host: tone(hz, ms, vol) report(score, f1, f2, f3)
//                                                      load(id) -> Uint8Array | null   save(bytes)   chips(op, n)
//                                                      data(name) -> Uint8Array | null (a file the Xbox put in the
//                                                      game's save folder, Storage.ReadData)
//   vm.start(seed, demo); vm.tick(held, pressed); vm.draw() -> the 64x64 screen (1 = lit); vm.usesB(); vm.error()
// Buttons are the VMX's bits: up 1, down 2, left 4, right 8, A 16, B 32. A game error stops the game; draw() then
// shows the GAME ERROR screen, as the VMX's.

const rd32 = (d, o) => (d[o] | d[o + 1] << 8 | d[o + 2] << 16 | d[o + 3] << 24) >>> 0;
let crcTable;
function crc32(d) {
  if (!crcTable) crcTable = Array.from({ length: 256 }, (_, n) => { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xEDB88320 ^ c >>> 1 : c >>> 1; return c >>> 0; });
  let c = 0xFFFFFFFF; for (let i = 0; i < d.length; i++) c = crcTable[(c ^ d[i]) & 255] ^ c >>> 8;
  return (c ^ 0xFFFFFFFF) >>> 0;
}

export async function vxRuntime(source) {
  const r = await source;
  if (r instanceof WebAssembly.Module) return r;
  if (typeof Response !== "undefined" && r instanceof Response) return WebAssembly.compile(await r.arrayBuffer());
  return WebAssembly.compile(r);                                   // bytes
}

// the card, as core/game_vx.c vmx_add_game reads it (null if it isn't a good GAME.VXP)
export function readVxp(d) {
  if (d.length < 32 || String.fromCharCode(...d.subarray(0, 4)) !== "VXP1") return null;
  const total = rd32(d, 12);
  if (total > d.length || crc32(d.subarray(32, total)) !== rd32(d, 16)) return null;
  const fl = d[10] | d[11] << 8;                                   // flags bit 3: casino; 5: won/lost/drawn (6: won/lost tally)
  const g = { id: rd32(d, 4), casino: !!(fl & 8), wld: fl & 0x20 ? fl & 0x40 ? 2 : 1 : 0, title: "", publisher: "", lines: [], blurb: "", card: null, code: false };
  for (let o = 32; o + 8 <= total; ) {
    const tag = String.fromCharCode(...d.subarray(o, o + 4)), n = rd32(d, o + 4), p = d.subarray(o + 8, o + 8 + n);
    if (o + 8 + n > total) return null;
    if (tag === "CODE") g.code = true;
    else if (tag === "CARD" && n >= 72) g.card = p.slice(0, 72);
    else if (tag === "NAME") {                                     // title \n publisher \n line1|line2 \n blurb
      const [title = "", pub = "", lines, blurb = ""] = new TextDecoder().decode(p).split("\n");
      g.title = title.slice(0, 15); g.publisher = pub; g.blurb = blurb;
      g.lines = lines ? lines.split("|").slice(0, 2).map(l => l.slice(0, 7)) : [title.slice(0, 6)];
    }
    o += 8 + ((n + 3) & ~3);
  }
  return g.code && g.title ? g : null;
}

// synchronous (a compiled module instantiates at once), so a game opens in the tick it is asked for
export function vxGame(rt, bytes, host = {}) {
  let ex;
  const u8 = () => new Uint8Array(ex.memory.buffer);
  const cstr = p => { const m = u8(); let e = p; while (m[e]) e++; return String.fromCharCode(...m.subarray(p, e)); };
  const env = {
    sin: Math.sin, cos: Math.cos, atan2: Math.atan2, pow: Math.pow, fmod: (a, b) => a % b,
    tone: (hz, ms, vol) => host.tone?.(hz, ms, vol),
    report: (s, a, b, c) => host.report?.(s, a, b, c),
    load(id, buf, max) {
      const d = host.load?.(id); if (!d) return -1;
      const n = Math.min(d.length, max); u8().set(d.subarray(0, n), buf); return n;
    },
    save: (buf, n) => host.save?.(u8().slice(buf, buf + n)),
    chips: (op, n) => host.chips ? host.chips(op, n) | 0 : 0,
    data(name, off, dst, n) {                                       // as the file call: dst 0 asks the length
      const d = host.data?.(cstr(name)); if (!d) return dst ? 0 : -1;
      if (!dst) return d.length;
      if (off < 0 || off >= d.length) return 0;
      n = Math.min(n, d.length - off); u8().set(d.subarray(off, off + n), dst); return n;
    },
  };
  ex = new WebAssembly.Instance(rt, { env }).exports;
  if (bytes.length > ex.vxw_vxp_max()) throw new Error("GAME.VXP too big");
  u8().set(bytes, ex.vxw_vxp());
  if (ex.vxw_open(bytes.length)) throw new Error("not a VMX game");
  const scr = ex.vxw_screen();
  return {
    start: (seed, demo) => ex.vxw_start(seed | 0, demo ? 1 : 0),
    tick: (held, pressed) => ex.vxw_tick(held | 0, pressed | 0),
    draw() { ex.vxw_draw(); return u8().subarray(scr, scr + 4096); },
    usesB: () => !!ex.vxw_uses_b(),
    error: () => cstr(ex.vxw_error()),
    stats: () => [...new Uint32Array(ex.memory.buffer, ex.vxw_stats(), 3)],     // instructions, heap in use, collections
  };
}
