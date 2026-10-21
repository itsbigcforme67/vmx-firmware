// BLOX as the web simulator runs it (POCKET ARCADE vmx/games.js): edges for left/right/A, the core's demo player.
export default m => ({
  start: (seed, demo) => m.newGame(seed),
  tick(g, { held, edges, demo }) { m.step(g, demo ? m.demo(g) : { left: edges.left, right: edges.right, a: edges.a }); },
  draw: g => m.draw(g),
});
