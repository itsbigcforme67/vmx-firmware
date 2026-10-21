// TRICKY WICKS for the VMX: the owner's game (TRICKY WICKS v1.5 rules, TwCore.cs; its CPU player, TwAi.cs), as the
// web simulator runs it: microgames/ui/games/tricky_core.js (the glue + 64x64 renderer, ported here line for line)
// driven by POCKET ARCADE vmx/games.js's "tricky" entry (the rules at 60 Hz: two steps a tick, the presses on the
// first; A and B swapped as on its page; Up drops; the demo is the core's own AI). tools/parity checks it frame by frame.
using System;
using Microsoft.Vmx;

public class Tricky : Game {
  const int W = 64, H = 64, COLS = 6, ROWS = 8, TS = 16;
  const int C = 7, FX = 1, FY = 5;
  // rope tiles: which sides each connects (N 1, E 2, S 4, W 8), as tricky_core.js's CONN strings
  static readonly int[] CONN = { 10, 5, 6, 12, 9, 3, 14, 13, 11, 7, 15, 14, 13, 11, 7, 15 };
  static readonly string[] BOMB_ART = { ".....#.", "....#..", "..###..", ".#####.", "##.####", "#######", ".#####." };
  static readonly string[] FIRE_ART = { "..#....", "..#.#..", ".#...#.", ".#.#.#.", "#..#..#", "#.###.#", ".#####." };
  static readonly string[] BIG_ART = { "..#....", "..###..", ".##.##.", ".#####.", "##.#.##", "#.###.#", ".#####." };
  // the explosions' ring points: Math.round(cos / sin * r) for boom_t 0..19 and the 16 angles, worked out in JS
  // (V8's Math.cos/sin) so they match it exactly; per entry: dx+8, dy+8, half dx+8, half dy+8 in 4-bit fields.
  // Checked: the rounding does not depend on the boom's position anywhere in the field.
  static readonly int[] BOOM = {
    0x8888,0x8888,0x8888,0x8888,0x8888,0x8888,0x8888,0x8888,0x8888,0x8888,0x8888,0x8888,0x8888,0x8888,0x8888,0x8888,
    0x8888,0x8888,0x8888,0x8888,0x8888,0x8888,0x8888,0x8888,0x8888,0x8888,0x8888,0x8888,0x8888,0x8888,0x8888,0x8888,
    0x8889,0x8899,0x8898,0x8898,0x8898,0x8897,0x8887,0x8887,0x8887,0x8877,0x8878,0x8878,0x8878,0x8879,0x8889,0x8889,
    0x8999,0x9899,0x9898,0x9898,0x9897,0x8797,0x8787,0x8787,0x8777,0x7877,0x7878,0x7878,0x7879,0x8979,0x8989,0x8989,
    0x9999,0x98a9,0x98a8,0x98a7,0x9797,0x8796,0x8786,0x8776,0x7777,0x7867,0x7868,0x7869,0x7979,0x897a,0x898a,0x899a,
    0x99a9,0x98a8,0x98a7,0x97a7,0x9796,0x8786,0x8776,0x7776,0x7767,0x7868,0x7869,0x7969,0x797a,0x898a,0x899a,0x999a,
    0x99b9,0x98b8,0x97b7,0x97a6,0x9795,0x8785,0x7775,0x7766,0x7757,0x7858,0x7959,0x796a,0x797b,0x898b,0x999b,0x99aa,
    0xa8b9,0xa8b7,0x97b6,0x97a5,0x8695,0x8675,0x7765,0x7756,0x6857,0x6859,0x795a,0x796b,0x8a7b,0x8a9b,0x99ab,0x99ba,
    0xa8c8,0xa7c6,0x97b5,0x9694,0x8684,0x7664,0x7755,0x6747,0x6848,0x694a,0x795b,0x7a7c,0x8a8c,0x9aac,0x99bb,0xa9c9,
    0xa7c7,0xa7c5,0x96a4,0x8694,0x7674,0x7654,0x6746,0x6847,0x6949,0x694b,0x7a6c,0x8a7c,0x9a9c,0x9abc,0xa9ca,0xa8c9,
    0xa7d6,0xa6b4,0x96a3,0x8683,0x7663,0x6645,0x6736,0x6838,0x693a,0x6a5c,0x7a6d,0x8a8d,0x9aad,0xaacb,0xa9da,0xa8d8,
    0xa6c5,0x96b3,0x8593,0x7573,0x6654,0x6735,0x5837,0x5939,0x6a4b,0x7a5d,0x8b7d,0x9b9d,0xaabc,0xa9db,0xb8d9,0xb7d7,
    0xa6c4,0x95a2,0x8582,0x7553,0x6644,0x5726,0x5828,0x593b,0x6a4c,0x7b6e,0x8b8e,0x9bbd,0xaacc,0xb9ea,0xb8e8,0xb7d5,
    0xa5b2,0x8592,0x7562,0x6543,0x5625,0x5827,0x592a,0x5a3c,0x6b5e,0x8b7e,0x9bae,0xabcd,0xbaeb,0xb8e9,0xb7e6,0xb6d4,
    0x95a1,0x8581,0x6552,0x5633,0x5716,0x5818,0x5a2b,0x6b3d,0x7b6f,0x8b8f,0xabbe,0xbadd,0xb9fa,0xb8f8,0xb6e5,0xa5d3,
    0x8591,0x7561,0x6542,0x5624,0x5817,0x591a,0x5a2c,0x6b4e,0x8b7f,0x9baf,0xabce,0xbaec,0xb8f9,0xb7f6,0xb6e4,0xa5c2,
    0x8581,0x6552,0x5633,0x5716,0x5818,0x5a2b,0x6b3d,0x7b6f,0x8b8f,0xabbe,0xbadd,0xb9fa,0xb8f8,0xb6e5,0xa5d3,0x95a1,
    0x7561,0x6542,0x5624,0x5817,0x591a,0x5a2c,0x6b4e,0x8b7f,0x9baf,0xabce,0xbaec,0xb8f9,0xb7f6,0xb6e4,0xa5c2,0x8591,
    0x6552,0x5633,0x5716,0x5818,0x5a2b,0x6b3d,0x7b6f,0x8b8f,0xabbe,0xbadd,0xb9fa,0xb8f8,0xb6e5,0xa5d3,0x95a1,0x8581,
    0x6542,0x5625,0x5817,0x591a,0x5a2c,0x6b5e,0x8b7f,0x9baf,0xabce,0xbaeb,0xb8f9,0xb7f6,0xb6e4,0xa5b2,0x8591,0x7561
    
  };

  TwCore G = new TwCore();
  TwAi ai;
  int t, overT, seed;                            // the JS g.t, g.overT, g.seed
  bool demo; int best;
  int[] ropeArt = new int[16 * 7], bombArt = new int[7], fireArt = new int[14], bigArt = new int[14], blockArt = new int[7];

  public override bool UsesB { get { return true; } }

  static int Rows(string[] art, int[] dst, int at) {
    for (int j = 0; j < 7; j++) { int m = 0; for (int i = 0; i < 7; i++) if (art[j][i] == '#') m |= 1 << i; dst[at + j] = m; }
    return 0;
  }
  void MakeArt() {
    for (int tt = 0; tt < 16; tt++) {
      int conn = CONN[tt], len = 0;
      for (int b = 0; b < 4; b++) if ((conn & (1 << b)) != 0) len++;
      for (int j = 0; j < 7; j++) {
        int m = 0;
        for (int i = 0; i < 7; i++) {
          bool inV = i >= 2 && i <= 4, inH = j >= 2 && j <= 4;
          bool armV = inV && ((j < 2 && (conn & 1) != 0) || (j > 4 && (conn & 4) != 0));
          bool armH = inH && ((i < 2 && (conn & 8) != 0) || (i > 4 && (conn & 2) != 0));
          bool knot = inV && inH;
          bool on = knot ? !(i == 3 && j == 3) || len > 2
                  : armV ? (i != 3 || j % 2 == 0)
                  : armH ? (j != 3 || i % 2 == 0) : false;
          if (on) m |= 1 << i;
        }
        ropeArt[tt * 7 + j] = m;
      }
    }
    Rows(BOMB_ART, bombArt, 0);
    Rows(FIRE_ART, fireArt, 0); Rows(FIRE_ART, fireArt, 7); fireArt[7] = 1 << 3;    // flick: the tip moves right
    Rows(BIG_ART, bigArt, 0); Rows(BIG_ART, bigArt, 7); bigArt[7] = 1 << 3;
    for (int j = 0; j < 7; j++) { int m = 0; for (int i = 0; i < 7; i++) if (i == 0 || j == 0 || i == 6 || j == 6 || (i + j) % 3 == 0) m |= 1 << i; blockArt[j] = m; }
  }

  public override void Start(int seed, bool demo) {
    this.demo = demo;
    MakeArt();
    ai = new TwAi(G);
    NewGame(seed & 0xFFFF);
    if (demo) ai.Init(this.seed);
  }
  void NewGame(int s) {                          // tricky_core.js newGame(seed): tw_init(seed & 0xFFFF, 0); t = 0
    seed = s; t = 0;
    G.Init(s & 0xFFFF, 0);
  }
  void Step(int held, int pressed) {             // tricky_core.js step()
    G.Step(held, pressed); t++;
    if (G.over != 0) { overT++; if (overT > 90) NewGame((seed * 31 + 7) & 0xFFFF); }   // overT is never reset (as in JS)
  }
  // VMX buttons -> the core's (left 1, right 2, up 4, down 8, A 16, B 32), with A and B swapped
  static int Map(int v) {
    int m = 0;
    if ((v & Buttons.Left) != 0) m |= TwCore.LEFT;
    if ((v & Buttons.Right) != 0) m |= TwCore.RIGHT;
    if ((v & Buttons.Up) != 0) m |= TwCore.UP;
    if ((v & Buttons.Down) != 0) m |= TwCore.DOWN;
    if ((v & Buttons.B) != 0) m |= TwCore.A;
    if ((v & Buttons.A) != 0) m |= TwCore.B;
    return m;
  }

  public override void Tick(int held, int pressed) {
    int h = Map(held), p = Map(pressed);
    for (int i = 0; i < 2; i++) {
      if (demo) Step(0, ai.Think());
      else Step(h, i != 0 ? 0 : p);
    }
    if (G.score > best) { best = G.score; Scores.Report(G.score, 0, 0, 0); }
  }

  // ---- drawing
  static int Round(double v) { return (int)Math.Floor(v + 0.5); }    // JS Math.round
  static void Art(Screen s, int x, int y, int[] art, int at, int skipRows) {
    for (int j = skipRows; j < 7; j++) { int m = art[at + j]; if (m == 0) continue; for (int i = 0; i < 7; i++) if ((m & (1 << i)) != 0) s.Set(x + i, y + j, true); }
  }
  void Tile(Screen s, int x, int y, int k, int tt, int timer, int frame) {   // timer < 0: none
    bool blink = timer >= 0 && ((frame >> 2) & 1) != 0;
    if (k == TwCore.ROPE) { if (blink) return; Art(s, x, y, ropeArt, tt * 7, 0); }
    else if (k == TwCore.BOMB || k == TwCore.CANNON) { if (blink) return; Art(s, x, y, bombArt, 0, 0); }
    else if (k == TwCore.FIRE || k == TwCore.BIGFIRE) {
      int flick = (frame >> 3) & 1;
      Art(s, x, y, k == TwCore.FIRE ? fireArt : bigArt, flick * 7, blink ? 2 : 0);
    }
    else if (k == TwCore.BLOCK) { if (blink) return; Art(s, x, y, blockArt, 0, 0); }
  }
  static string Pad(int v, int n) { return v.ToString().PadLeft(n, '0'); }

  public override void Draw(Screen s) {
    s.Clear();
    for (int y = FY - 1; y <= FY + ROWS * C; y++) { s.Set(FX - 1, y, true); s.Set(FX + COLS * C, y, true); }
    for (int x = FX - 1; x <= FX + COLS * C; x++) s.Set(x, FY + ROWS * C, true);
    for (int r = 0, i = 0; r < ROWS; r++) for (int c = 0; c < COLS; c++, i++) {
      int k = G.kind[i]; if (k == 0) continue;
      int tm = G.tm[i];
      Tile(s, FX + c * C, FY + r * C + Round(G.off[i] * C / (double)TS), k, G.t[i], tm != 255 ? tm : -1, t);
    }
    if (G.p_kind != 0) Tile(s, FX + G.p_col * C, FY + Round((G.p_y - TS) * C / (double)TS), G.p_kind, G.p_t, -1, t);
    for (int i = 0; i < G.n_booms; i++) {
      int cx = FX + G.boom_c[i] * C + 3, cy = FY + G.boom_r[i] * C + 3, bt = G.boom_t[i];
      for (int a = 0; a < 16; a++) {
        int e = BOOM[bt * 16 + a];
        s.Set(cx + (e & 15) - 8, cy + ((e >> 4) & 15) - 8, true);
        if (a % 2 != 0) s.Set(cx + ((e >> 8) & 15) - 8, cy + ((e >> 12) & 15) - 8, true);
      }
    }
    // side panel
    s.Text(45, 0, "NXT", true);
    for (int y = 6; y <= 14; y++) { s.Set(46, y, true); s.Set(54, y, true); } for (int x = 46; x <= 54; x++) { s.Set(x, 6, true); s.Set(x, 14, true); }
    if (G.n_kind != 0) Tile(s, 47, 7, G.n_kind, G.n_t, -1, t);
    string sc = Pad(G.score % 1000000, 6);
    s.Text(45, 18, sc.Substring(0, 3), true); s.Text(45, 24, sc.Substring(3), true);
    s.Text(45, 32, "LV", true); s.Text(45, 38, Pad(G.level, 2), true);
    s.Text(45, 46, "WK", true); s.Text(45, 52, "*" + Pad(G.wick, 2), true);        // '*' is the VMX font's small x
    for (int i = 0; i < G.n_pops; i++) {
      int a = G.pop_a[i], bb = G.pop_b[i], type = G.pop_type[i];
      string str = type == 0 || type == 1 ? "*" + bb.ToString() : type == 2 ? "CHAIN" : type == 4 ? "+" + a.ToString() : "";
      if (str.Length > 0 && G.pop_t[i] >= 8) {
        int w = str.Length * 4 - 1;
        int x = Math.Max(1, Math.Min(42 - w, Round(FX + (G.pop_x[i] - 8) * C / (double)TS - w / 2.0)));
        for (int yy = -1; yy < 6; yy++) for (int xx = -1; xx <= w; xx++) s.Set(x + xx, 20 + yy, false);
        s.Text(x, 20, str, true);
      }
    }
    if (G.over != 0) {
      for (int y = 22; y < 37; y++) for (int x = 0; x < 43; x++) s.Set(x, y, y == 22 || y == 36);
      s.Text(8, 24, "GAME", true); s.Text(8, 30, "OVER", true);
    }
  }
}
