// 9-BALL as its page runs it (microgames/ui/games/pool.html): the core takes HELD buttons; demo = { auto: true }.
export default m => ({
  start: (seed, demo) => m.newGame(seed),
  tick(g, { held, demo }) { m.step(g, demo ? m.demo(g) : held); },
  draw: g => m.draw(g),
});
