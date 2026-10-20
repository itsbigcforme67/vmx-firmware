// A game's 24x24 card picture from the web simulator's adapter (POCKET ARCADE vmx/games.js), as 24 lines of # and .
//   node tools/card_from_js.mjs blox > games/blox/card.txt
const id = process.argv[2];
const { GAMES } = await import(new URL("../../POCKET ARCADE/pocket-arcade/vmx/games.js", import.meta.url));
const a = GAMES.find(g => g.id === id); if (!a) { console.error("no game " + id); process.exit(1); }
const d = new Uint8Array(576);
const px = (x, y, c = 1) => { x = Math.floor(x); y = Math.floor(y); if (x >= 0 && y >= 0 && x < 24 && y < 24) d[y * 24 + x] = c; };
const s = {
  px, fill(x, y, w, h, c = 1) { for (let j = 0; j < h; j++) for (let i = 0; i < w; i++) px(x + i, y + j, c); },
  rect(x, y, w, h, c = 1) { for (let i = 0; i < w; i++) { px(x + i, y, c); px(x + i, y + h - 1, c); } for (let j = 0; j < h; j++) { px(x, y + j, c); px(x + w - 1, y + j, c); } },
  rrect(x, y, w, h, c = 1) { for (let i = 1; i < w - 1; i++) { px(x + i, y, c); px(x + i, y + h - 1, c); } for (let j = 1; j < h - 1; j++) { px(x, y + j, c); px(x + w - 1, y + j, c); } },
  line(x0, y0, x1, y1, c = 1) { const n = Math.max(Math.abs(x1 - x0), Math.abs(y1 - y0), 1); for (let i = 0; i <= n; i++) px(Math.round(x0 + (x1 - x0) * i / n), Math.round(y0 + (y1 - y0) * i / n), c); },
  disc(cx, cy, r, c = 1) { for (let y = -r - 1; y <= r + 1; y++) for (let x = -r - 1; x <= r + 1; x++) if (x * x + y * y <= r * r + r * 0.6) px(cx + x, cy + y, c); },
  ring(cx, cy, r, c = 1) { for (let y = -r - 1; y <= r + 1; y++) for (let x = -r - 1; x <= r + 1; x++) { const q = x * x + y * y; if (q <= r * r + r * 0.6 && q > (r - 1) * (r - 1) + (r - 1) * 0.6) px(cx + x, cy + y, c); } },
  text() { console.error("card uses text; not supported here"); },
};
a.art(s);
for (let y = 0; y < 24; y++) console.log(Array.from(d.slice(y * 24, y * 24 + 24), v => v ? "#" : ".").join(""));
console.error(JSON.stringify({ name: a.name, title: a.title || a.name.join("") }));
