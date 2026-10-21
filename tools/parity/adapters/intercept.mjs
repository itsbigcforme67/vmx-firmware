// INTERCEPT as the web simulator runs it (POCKET ARCADE vmx/games.js): the held D-pad steers the crosshair, an A
// press fires "auto" (the nearest base with ammo); the demo is the core's demo player.
export default m => ({
  start: (seed, demo) => m.newGame(seed),
  tick(g, { held: h, edges: e, demo }) {
    m.step(g, demo ? m.demo(g)
      : { dx: (h.right ? 1 : 0) - (h.left ? 1 : 0), dy: (h.down ? 1 : 0) - (h.up ? 1 : 0), fire: e.a ? "auto" : null });
  },
  draw: g => m.draw(g),
});
