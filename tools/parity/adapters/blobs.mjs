// BLOBS as the web simulator runs it (POCKET ARCADE vmx/games.js): edges for left/right, A turns left, B or Up turns
// right, Down held drops faster; the core's demo player.
export default m => ({
  start: (seed, demo) => m.newGame(seed),
  tick(g, { held, edges, demo }) {
    m.step(g, demo ? m.demo(g) : { left: edges.left, right: edges.right, rotL: edges.a, rotR: edges.b || edges.up, down: held.down });
  },
  draw: g => m.draw(g),
});
