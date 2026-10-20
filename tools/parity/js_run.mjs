// The JS side of the parity check: runs a microgames core with the simulator adapter's rules and the same press
// pattern as runtime/vxrun.c, printing "tick hash" lines.   node js_run.mjs blox SEED DEMO TICKS [PATTERNSEED]
const [game, seed, demo, ticks, pseed] = process.argv.slice(2);
const m = await import(new URL(`../../../microgames/ui/games/${game}_core.js`, import.meta.url));
const ADAPT = {
  blox: { start: s => m.newGame(s), tick(g, e, d) { m.step(g, d ? m.demo(g) : { left: e.left, right: e.right, a: e.a }); } },
};
const A = ADAPT[game]; const g = A.start(+seed); const isDemo = +demo === 1;
let ps = (+(pseed ?? 1)) >>> 0, held = 0;
const B = ["up", "down", "left", "right", "a", "b"];
for (let t = 1; t <= +ticks; t++) {
  ps = (Math.imul(ps, 1103515245) + 12345) >>> 0;
  const nh = ((ps >>> 16) % 8 === 0) ? ((ps >>> 20) & 63) : held;
  const pressed = nh & ~held; held = nh;
  const e = {}; B.forEach((b, i) => e[b] = !!(pressed & (1 << i)));
  A.tick(g, e, isDemo);
  const buf = m.draw(g);
  let h = 2166136261 >>> 0; for (let i = 0; i < 4096; i++) { h ^= buf[i] ? 1 : 0; h = Math.imul(h, 16777619) >>> 0; }
  console.log(`${t} ${h.toString(16).padStart(8, "0")}`);
}
