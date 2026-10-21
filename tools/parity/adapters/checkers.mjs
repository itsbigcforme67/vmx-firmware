// CHECKERS as the web simulator runs it (POCKET ARCADE vmx/games.js): edges for the D-pad, A and B; in the demo the
// core's own CPU plays both sides.
export default m => ({
  start: (seed, demo) => m.newGame(seed),
  tick(g, { edges, demo }) { m.step(g, demo ? {} : edges, demo ? [1, 2] : [2]); },
  draw: g => m.draw(g),
});
