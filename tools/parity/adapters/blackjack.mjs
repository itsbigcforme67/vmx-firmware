// BLACKJACK AND HOOKERS as the web simulator runs it (POCKET ARCADE vmx/games.js): a title screen that waits for A
// (the demo skips it), then edges for A hit / B stand / Up double / left-right bet, or the core's demo player.
export default m => ({
  start: (seed, demo, bank) => Object.assign(m.newGame(seed, bank), { title: true, titleT: 0 }),
  tick(g, { edges: e, demo }) {
    if (g.title) { g.titleT++; if (e.a || demo) g.title = false; return; }
    m.step(g, demo ? m.demo(g) : e);
  },
  draw: g => g.title ? m.drawTitle(g.titleT) : m.draw(g),
});
