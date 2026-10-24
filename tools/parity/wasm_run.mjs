// The browser build of the runtime (build/vxrt.wasm + runtime/wasm/vxrt.js) on a GAME.VXP, with the same press
// pattern, HOLD, PRESSES, CLEARED and "tick hash" output as build/vxrun and build/fold_run, so their lines can be
// compared (tools/parity/wasm_check.sh does it for every game):
//   node tools/parity/wasm_run.mjs build/games/FOLDROOM.VXP SEED DEMO TICKS [PATTERNSEED] [--chips N] [--data NAME=PATH]
import { readFileSync } from "node:fs";
import { vxRuntime, readVxp, vxGame } from "../../runtime/wasm/vxrt.js";

const [vxpPath, seed, demo, ticks, pat = "1"] = process.argv.slice(2);
if (!ticks) { console.error("usage: wasm_run.mjs GAME.VXP SEED DEMO TICKS [PATTERNSEED] [--chips N]"); process.exit(2); }
const ci = process.argv.indexOf("--chips"); let bank = ci > 0 ? +process.argv[ci + 1] : 100;
const bytes = new Uint8Array(readFileSync(vxpPath));
if (!readVxp(bytes)) { console.error(vxpPath + ": not a good GAME.VXP"); process.exit(1); }
const rt = await vxRuntime(readFileSync(new URL("../../build/vxrt.wasm", import.meta.url)));
const cleared = process.env.CLEARED, data = {};
process.argv.forEach((v, i) => { if (v === "--data") { const [k, p] = process.argv[i + 1].split("="); data[k] = new Uint8Array(readFileSync(p)); } });
const vm = vxGame(rt, bytes, {
  data: name => data[name] || null,
  load: () => cleared ? new Uint8Array(new Int32Array([+cleared]).buffer) : null,
  chips(op, n) {                                       // the test bank, as runtime/vxrun.c's
    if (op === 0) return bank;
    if (op === 1) { if (n > bank) return 0; bank -= n; return 1; }
    if (op === 2) bank += n;
    return 0;
  },
});
if (vm.start(+seed, +demo)) { console.error("start: " + vm.error()); process.exit(1); }
const presses = process.env.PRESSES ? readFileSync(process.env.PRESSES, "utf8").split(/\s+/).filter(Boolean).map(Number) : null;
let ps = +pat >>> 0, held = 0, out = [];
for (let t = 1; t <= +ticks; t++) {
  ps = (Math.imul(ps, 1103515245) + 12345) >>> 0;
  let nh = (ps >>> 16) % 8 === 0 ? (ps >>> 20) & 63 : held;
  if (process.env.HOLD) nh = +process.env.HOLD;
  if (presses) nh = presses[t - 1] ?? 0;
  const pressed = nh & ~held; held = nh;
  if (vm.tick(held, pressed)) { console.error(`tick ${t}: ${vm.error()}`); process.exit(1); }
  const scr = vm.draw();
  if (vm.error()) { console.error(`tick ${t}: ${vm.error()}`); process.exit(1); }
  let h = 2166136261; for (let i = 0; i < 4096; i++) { h ^= scr[i]; h = Math.imul(h, 16777619) >>> 0; }
  out.push(`${t} ${h.toString(16).padStart(8, "0")}`);
}
console.log(out.join("\n"));
