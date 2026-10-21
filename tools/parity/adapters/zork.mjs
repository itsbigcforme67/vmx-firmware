// ZORK I as the web simulator runs it (POCKET ARCADE vmx/games.js): the story from the game's folder, edges for every
// button plus up/down auto-repeat while held (the page's tap queue isn't used), the core's demo in DEMO. No saved game
// at the start (the page would load one from localStorage).
import { readFileSync } from "node:fs";
export default m => ({
  start(seed, demo) {
    const g = m.newGame(seed, readFileSync(new URL("../../../games/zork/zork1.z3", import.meta.url)));
    g.hold = {};
    return g;
  },
  tick(g, { held: h, edges: e, demo }) {
    const inp = { ...e };
    for (const k of ["up", "down"]) { g.hold[k] = h[k] ? (g.hold[k] || 0) + 1 : 0; if (g.hold[k] > 12 && g.hold[k] % 3 === 0) inp[k] = true; }
    m.step(g, demo ? m.demo(g) : inp);
  },
  draw: g => m.draw(g),
});
