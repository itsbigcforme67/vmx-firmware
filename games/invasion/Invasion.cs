// INVASION for the VMX: a Space Invaders-style shooter (working name). A line-for-line port of
// microgames/ui/games/invasion_core.js to C# 1.0, so it plays exactly the same (same random numbers, same order of
// events); tools/parity checks that frame by frame. Left/right move the cannon (held), A fires (one shot at a time).
// The JS core's own font matches the VMX font for every glyph it uses (digits, "WAVE", "GAME OVER"), so Screen.Text.
using System;
using Microsoft.Vmx;

public class Invasion : Game {
  const int W = 64, H = 64;
  const int COLS = 7, ROWS = 5, DX = 7, DY = 6, AW = 5, AH = 4, TOP = 13, PY = 57, SHIELD_Y = 48;
  const int PER_TICK = 2, DROP = 3, LIVES = 3, NA = COLS * ROWS;
  static readonly int[] KIND = { 0, 1, 1, 2, 2 };
  static readonly int[] POINTS = { 30, 20, 10 };
  static readonly int[] UFO_PTS = { 50, 100, 150, 300 };
  static readonly int[] SHIELD_X = { 6, 27, 48 };

  // sprites as rows of bits (bit i = column i), made once from the JS core's pictures
  static int[] Bits(string[] rows) {
    int[] r = new int[rows.Length];
    for (int j = 0; j < rows.Length; j++) for (int i = 0; i < rows[j].Length; i++) if (rows[j][i] == '#') r[j] |= 1 << i;
    return r;
  }
  int[][] alien;                                 // alien[kind * 2 + frame]
  int[] cannon, ufoSpr, boom, shieldSpr;
  public Invasion() {
    alien = new int[6][];
    alien[0] = Bits(new string[] { "..#..", ".###.", "#.#.#", ".#.#." });
    alien[1] = Bits(new string[] { "..#..", ".###.", "#.#.#", "#...#" });
    alien[2] = Bits(new string[] { "#...#", ".###.", "##.##", ".#.#." });
    alien[3] = Bits(new string[] { ".#.#.", ".###.", "##.##", "#...#" });
    alien[4] = Bits(new string[] { ".###.", "#####", "#.#.#", "#...#" });
    alien[5] = Bits(new string[] { ".###.", "#####", "#.#.#", ".#.#." });
    cannon = Bits(new string[] { "..#..", ".###.", "#####" });
    ufoSpr = Bits(new string[] { ".#####.", "##.#.##", ".#####." });
    boom = Bits(new string[] { "#.#.#", ".#.#.", "#.#.#", ".#.#." });
    shieldSpr = Bits(new string[] { ".#######.", "#########", "#########", "##.....##" });
  }

  uint rs;                                       // the JS core's rng(): an LCG, divided down to [0, 1)
  double Rnd() { rs = rs * 1664525u + 1013904223u; return rs / 4294967296.0; }

  int t, score, lives, wave, over; bool prevA;
  // the aliens, in the JS array's order (bottom-left first)
  int[] aR = new int[NA], aC = new int[NA], aX = new int[NA], aY = new int[NA], aF = new int[NA]; bool[] aAlive = new bool[NA];
  int dir, next; bool dropNext;
  int px; bool shotOn; int shotX, shotY;
  int[] bX = new int[16], bY = new int[16], bKind = new int[16]; bool[] bGone = new bool[16]; int nBombs;
  int[] oX = new int[64], oY = new int[64], oT = new int[64]; int nBooms;
  int dead; bool ufoOn; int ufoX, ufoD, ufoT, clear, fireT;
  bool ufoPtsOn; int ufoPtsX, ufoPtsPts, ufoPtsT;   // not reset by a new game or wave, as in the JS core
  bool[] shield = new bool[W * H];
  int[] live = new int[NA]; int nLive;           // the step's alive(g) snapshot (holds an alien shot this tick)
  int[] cols = new int[COLS], overC = new int[COLS];
  bool demo; int best;

  public override bool UsesB { get { return false; } }

  public override void Start(int seed, bool demo) {
    this.demo = demo;
    NewGame((uint)seed);
  }
  void NewGame(uint seed) {
    rs = seed; t = 0; score = 0; lives = LIVES; wave = 0; over = 0; prevA = false;
    NewWave(true);
  }
  void NewWave(bool first) {
    if (!first) wave++;
    int y0 = TOP + DROP * Math.Min(wave, 4);
    int n = 0;
    for (int r = ROWS - 1; r >= 0; r--) for (int c = 0; c < COLS; c++) {
      aR[n] = r; aC[n] = c; aX[n] = 8 + c * DX; aY[n] = y0 + r * DY; aF[n] = 0; aAlive[n] = true; n++;
    }
    dir = 1; next = 0; dropNext = false;
    px = 30; shotOn = false; nBombs = 0; nBooms = 0; dead = 0; ufoOn = false; ufoT = 600; clear = 0;
    fireT = 40;
    for (int i = 0; i < W * H; i++) shield[i] = false;
    for (int k = 0; k < 3; k++) for (int j = 0; j < 4; j++) for (int i = 0; i < 9; i++)
      if ((shieldSpr[j] >> i & 1) != 0) shield[(SHIELD_Y + j) * W + SHIELD_X[k] + i] = true;
  }
  void Alive() { nLive = 0; for (int i = 0; i < NA; i++) if (aAlive[i]) live[nLive++] = i; }
  void Erode(int x, int y, bool up) {
    for (int j = -2; j <= 2; j++) for (int i = -1; i <= 1; i++) {
      int xx = x + i, yy = y + j * (up ? -1 : 1);
      if (xx < 0 || yy < 0 || xx >= W || yy >= H) continue;
      int aj = Math.Abs(j);
      if (aj + Math.Abs(i) <= 2 && Rnd() < 0.8 - aj * 0.15) shield[yy * W + xx] = false;
    }
  }
  bool ShieldAt(int x, int y) { return x >= 0 && y >= 0 && x < W && y < H && shield[y * W + x]; }
  void AddBoom(int x, int y) { if (nBooms < oX.Length) { oX[nBooms] = x; oY[nBooms] = y; oT[nBooms] = 0; nBooms++; } }

  public override void Tick(int held, int pressed) {
    int b = demo ? Demo() : held;
    Step((b & Buttons.Left) != 0, (b & Buttons.Right) != 0, (b & Buttons.A) != 0);
    if (score > best) { best = score; Scores.Report(score, 0, 0, 0); }
  }

  void Step(bool left, bool right, bool a) {
    t++;
    bool fire = a && !prevA; prevA = a;
    int k2 = 0;
    for (int i = 0; i < nBooms; i++) { oT[i]++; if (oT[i] < 8) { oX[k2] = oX[i]; oY[k2] = oY[i]; oT[k2] = oT[i]; k2++; } }
    nBooms = k2;
    if (over > 0) { if (--over == 0) NewGame((uint)Math.Floor(Rnd() * 1e9)); return; }
    if (clear > 0) { if (--clear == 0) NewWave(false); return; }
    if (dead > 0) {
      if (--dead == 0) { if (lives <= 0) { over = 150; return; } px = 30; }
      return;
    }
    // cannon
    if (left) px = Math.Max(1, px - 1);
    if (right) px = Math.Min(W - 6, px + 1);
    if (fire && !shotOn) { shotOn = true; shotX = px + 2; shotY = PY - 2; }
    // the ranks
    Alive();
    if (nLive == 0) { clear = 60; return; }
    for (int k = 0; k < PER_TICK; k++) {
      while (next < NA && !aAlive[next]) next++;
      if (next >= NA) {
        next = 0;
        int lx = 99999, rx = -99999;
        for (int q = 0; q < nLive; q++) { int x = aX[live[q]]; if (x < lx) lx = x; if (x + AW - 1 > rx) rx = x + AW - 1; }
        if (dropNext) { dropNext = false; dir = -dir; }
        else if ((dir > 0 && rx >= W - 2) || (dir < 0 && lx <= 1)) dropNext = true;
        break;
      }
      int ai = next++;
      if (dropNext) aY[ai] += DROP; else aX[ai] += dir;
      aF[ai] ^= 1;
      for (int j = 0; j < AH; j++) for (int i = 0; i < AW; i++) if (aY[ai] + j < H) {
        int idx = (aY[ai] + j) * W + aX[ai] + i;
        if (idx >= 0 && idx < W * H) shield[idx] = false;
      }
      if (aY[ai] + AH >= PY) { lives = 0; dead = 45; AddBoom(px, PY); return; }
    }
    // player shot
    if (shotOn) {
      for (int s = 0; s < 3 && shotOn; s++) {
        shotY--;
        int x = shotX, y = shotY;
        if (y < 6) { shotOn = false; break; }
        if (ShieldAt(x, y)) { Erode(x, y, true); shotOn = false; break; }
        int hit = -1;
        for (int q = 0; q < nLive; q++) { int h = live[q]; if (x >= aX[h] && x < aX[h] + AW && y >= aY[h] && y < aY[h] + AH) { hit = h; break; } }
        if (hit >= 0) { aAlive[hit] = false; score += POINTS[KIND[aR[hit]]]; AddBoom(aX[hit], aY[hit]); shotOn = false; break; }
        if (ufoOn && x >= ufoX && x < ufoX + 7 && y >= 6 && y < 9) {
          int pts = UFO_PTS[(int)Math.Floor(Rnd() * 4)];
          score += pts; ufoPtsOn = true; ufoPtsX = ufoX; ufoPtsPts = pts; ufoPtsT = 45; ufoOn = false; shotOn = false; break;
        }
        int bi = -1;
        for (int q = 0; q < nBombs; q++) if (Math.Abs(bX[q] - x) <= 1 && y >= bY[q] - 1 && y <= bY[q] + 2) { bi = q; break; }
        if (bi >= 0) {
          for (int q = bi; q < nBombs - 1; q++) { bX[q] = bX[q + 1]; bY[q] = bY[q + 1]; bKind[q] = bKind[q + 1]; bGone[q] = bGone[q + 1]; }
          nBombs--; shotOn = false; break;
        }
      }
    }
    // alien bombs
    if (--fireT <= 0 && nBombs < 2 + Math.Min(wave, 2)) {
      fireT = 18 + (int)Math.Floor(Rnd() * 30) - Math.Min(wave * 3, 10);
      int nCols = 0;
      for (int q = 0; q < nLive; q++) {
        int c = aC[live[q]]; bool seen = false;
        for (int u = 0; u < nCols; u++) if (cols[u] == c) { seen = true; break; }
        if (!seen) cols[nCols++] = c;
      }
      int nOver = 0;
      for (int u = 0; u < nCols; u++) {
        int f = -1;
        for (int q = 0; q < nLive; q++) if (aC[live[q]] == cols[u]) { f = live[q]; break; }
        if (Math.Abs(aX[f] + 2 - (px + 2)) <= 4) overC[nOver++] = cols[u];
      }
      int cc;
      if (nOver > 0 && Rnd() < 0.5) cc = overC[0]; else cc = cols[(int)Math.Floor(Rnd() * nCols)];
      int low = -1;
      for (int q = 0; q < nLive; q++) { int h = live[q]; if (aC[h] != cc) continue; if (low < 0 || aY[h] > aY[low]) low = h; }
      if (nBombs < bX.Length) {
        bX[nBombs] = aX[low] + 2; bY[nBombs] = aY[low] + AH; bKind[nBombs] = (int)Math.Floor(Rnd() * 2); bGone[nBombs] = false; nBombs++;
      }
    }
    for (int q = 0; q < nBombs; q++) {
      bY[q] += t % 3 == 0 ? 2 : 1;
      if (ShieldAt(bX[q], bY[q] + 2)) { Erode(bX[q], bY[q] + 2, false); bGone[q] = true; continue; }
      if (bY[q] + 2 >= PY && bY[q] < PY + 3 && bX[q] >= px && bX[q] < px + 5) {
        lives--; dead = 50; AddBoom(px, PY - 1); nBombs = 0; shotOn = false; return;
      }
      if (bY[q] > 61) bGone[q] = true;
    }
    int nb = 0;
    for (int q = 0; q < nBombs; q++) if (!bGone[q]) { bX[nb] = bX[q]; bY[nb] = bY[q]; bKind[nb] = bKind[q]; bGone[nb] = false; nb++; }
    nBombs = nb;
    // mystery ship
    if (ufoPtsOn && --ufoPtsT <= 0) ufoPtsOn = false;
    if (!ufoOn && --ufoT <= 0 && nLive > 6) {
      bool l = Rnd() < 0.5; ufoOn = true; ufoX = l ? -7 : W; ufoD = l ? 1 : -1; ufoT = 500 + (int)Math.Floor(Rnd() * 400);
    }
    if (ufoOn && t % 2 == 0) { ufoX += ufoD; if (ufoX < -8 || ufoX > W + 1) ufoOn = false; }
  }

  // ---- the demo player (the JS core's demo()): returns Buttons bits
  int Demo() {
    Alive();
    int c = px + 2, tx = c, inp = 0;
    if (ufoOn && ufoX > 0 && ufoX < W - 8) tx = ufoX + 3 + ufoD * 6;
    else if (nLive > 0) {
      int p = -1;
      for (int q = 0; q < nLive; q++) {
        int a = live[q]; bool lower = false;
        for (int u = 0; u < nLive; u++) { int o = live[u]; if (aC[o] == aC[a] && aY[o] > aY[a]) { lower = true; break; } }
        if (lower) continue;
        if (p < 0 || Math.Abs(aX[a] + 2 - c) < Math.Abs(aX[p] + 2 - c)) p = a;
      }
      tx = aX[p] + 2 + dir;
    }
    int danger = -1;
    for (int q = 0; q < nBombs; q++) if (bY[q] > 34 && Math.Abs(bX[q] - c) <= 4) { danger = q; break; }
    if (danger >= 0) { if (bX[danger] >= c) inp |= Buttons.Left; else inp |= Buttons.Right; }
    else if (tx < c - 1) inp |= Buttons.Left; else if (tx > c + 1) inp |= Buttons.Right;
    if (Math.Abs(tx - c) <= 2 && danger < 0 && ((t >> 1) & 1) != 0) inp |= Buttons.A;
    return inp;
  }

  // ---- drawing
  static void Sprite(Screen s, int x, int y, int[] rows) {
    for (int j = 0; j < rows.Length; j++) { int r = rows[j]; for (int i = 0; r != 0; i++, r >>= 1) if ((r & 1) != 0) s.Set(x + i, y + j, true); }
  }
  public override void Draw(Screen s) {
    s.Clear();
    s.Text(0, 0, score.ToString().PadLeft(5, '0'), true);
    int nl = Math.Max(0, lives - (dead > 0 ? 0 : 1));
    for (int i = 0; i < nl; i++) Sprite(s, 59 - i * 6, 1, cannon);
    for (int y = SHIELD_Y; y < SHIELD_Y + 4; y++) for (int x = 0; x < W; x++) if (shield[y * W + x]) s.Set(x, y, true);   // only these rows are ever set
    for (int i = 0; i < NA; i++) if (aAlive[i]) Sprite(s, aX[i], aY[i], alien[KIND[aR[i]] * 2 + aF[i]]);
    for (int i = 0; i < nBooms; i++) if (oT[i] < 8) Sprite(s, oX[i], oY[i], boom);
    if (ufoOn) Sprite(s, ufoX, 6, ufoSpr);
    if (ufoPtsOn && ((t >> 2) & 1) != 0) s.Text(Math.Max(0, Math.Min(W - 12, ufoPtsX - 2)), 6, ufoPtsPts.ToString(), true);
    if (dead == 0) Sprite(s, px, PY, cannon);
    else if (((t >> 2) & 1) != 0) Sprite(s, px, PY - 1, boom);
    if (shotOn) for (int j = 0; j < 2; j++) s.Set(shotX, shotY + j, true);
    for (int q = 0; q < nBombs; q++) {
      int x = bX[q], y = bY[q];
      if (bKind[q] == 0) { for (int j = 0; j < 3; j++) s.Set(x + ((y + j + (t >> 2)) & 1), y + j, true); }
      else { for (int j = 0; j < 3; j++) s.Set(x, y + j, true); s.Set(x - 1, y + 2, true); s.Set(x + 1, y + 2, true); }
    }
    for (int x = 0; x < W; x++) s.Set(x, 62, true);
    if (clear > 0) { string w = "WAVE " + (wave + 2).ToString(); s.Text((W - w.Length * 4 + 1) >> 1, 28, w, true); }
    if (over > 0) {
      for (int y = 25; y < 36; y++) for (int x = 12; x < 52; x++) s.Set(x, y, y == 25 || y == 35 || x == 12 || x == 51);
      s.Text(15, 28, "GAME OVER", true);
    }
  }
}
