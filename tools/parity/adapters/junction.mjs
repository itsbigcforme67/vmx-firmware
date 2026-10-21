// JUNCTION as the web simulator runs it (POCKET ARCADE vmx/games.js): the core takes HELD buttons, the core's demo player.
export default m => ({
  start: (seed, demo) => m.newGame(seed),
  tick(g, { held, demo }) { m.step(g, demo ? m.demo(g) : held); },
  draw: g => m.draw(g),
});
