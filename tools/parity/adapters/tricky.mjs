// TRICKY WICKS as the web simulator runs it (POCKET ARCADE vmx/games.js): the rules at 60 Hz, two steps a tick with
// the presses on the first, A and B swapped, the core's own AI for the demo.
export default m => ({
  start(seed, demo) { const g = m.newGame(seed & 0xFFFF); if (demo) m.aiInit(g, g.seed); return g; },
  tick(g, { held: h, edges: e, demo }) {
    for (let i = 0; i < 2; i++) {
      if (demo) m.step(g, {}, m.ai(g));
      else m.step(g, { ...h, a: h.b, b: h.a }, i ? {} : { ...e, a: e.b, b: e.a });
    }
  },
  draw: g => m.draw(g),
});
