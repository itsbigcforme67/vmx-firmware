// The JS side of the parity check: runs a microgames core through its adapter (tools/parity/adapters/NAME.mjs: the
// web simulator's rules for that game) with the same press pattern as runtime/vxrun.c; prints "tick hash" lines.
//   node js_run.mjs NAME SEED DEMO TICKS [PATTERNSEED]
const [game, seed, demo, ticks, pseed] = process.argv.slice(2);
const core = (await import(new URL(`../../../microgames/ui/games/${game}_core.js`, import.meta.url)));
const A = (await import(new URL(`./adapters/${game}.mjs`, import.meta.url))).default(core);
const isDemo = +demo === 1; const bank = { chips: +(process.env.CHIPS || 100), bonus: 0 };   // as vxrun --chips
const g = await A.start(+seed, isDemo, bank);
let ps = (+(pseed ?? 1)) >>> 0, held = 0;
const B = ["up", "down", "left", "right", "a", "b"];
const bits = v => { const o = {}; B.forEach((b, i) => o[b] = !!(v & (1 << i))); return o; };
for (let t = 1; t <= +ticks; t++) {
  ps = (Math.imul(ps, 1103515245) + 12345) >>> 0;
  const nh = ((ps >>> 16) % 8 === 0) ? ((ps >>> 20) & 63) : held;
  const pressed = nh & ~held; held = nh;
  A.tick(g, { held: bits(held), edges: bits(pressed), demo: isDemo });
  const buf = A.draw(g);
  let h = 2166136261 >>> 0; for (let i = 0; i < 4096; i++) { h ^= buf[i] ? 1 : 0; h = Math.imul(h, 16777619) >>> 0; }
  console.log(`${t} ${h.toString(16).padStart(8, "0")}`);
}
