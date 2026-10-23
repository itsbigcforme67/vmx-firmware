// SIC BO as its page runs it (microgames/ui/games/sicbo.html): the core takes HELD buttons; demo = { auto: true }.
export default m => ({
  start: (seed, demo, bank) => m.newGame(seed, bank),
  tick(g, { held, demo }) { m.step(g, demo ? m.demo(g) : held); },
  draw: g => m.draw(g),
});
