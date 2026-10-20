// BLOX for the VMX: a Klax-style tile game (working name). A line-for-line port of microgames/ui/games/blox_core.js
// to C# 1.0, so it plays exactly the same (same random numbers, same doubles); tools/parity checks that frame by frame.
// Left/right move the paddle, A drops its top tile into the bin. Three of a kind in a row, column or diagonal clear.
using System;
using Microsoft.Vmx;

public class Blox : Game {
  const int LANES = 5, ROWS = 5, STACK = 5;
  const int BELT0 = 6, BELT1 = 27, PADDLE_Y = 29, BIN_Y = 34, CELL_H = 6;
  static int LX(int l) { return 4 + l * 11; }

  uint rs;                                       // the JS core's rng(): an LCG, divided down to [0, 1)
  double Rnd() { rs = rs * 1664525u + 1013904223u; return rs / 4294967296.0; }

  // the belt's tiles, in the order the JS array holds them
  int[] beltLane = new int[64], beltKind = new int[64]; double[] beltY = new double[64]; int nBelt;
  int[] stack = new int[STACK]; int nStack;
  int[][] bin;                                   // bin[c][r], nBin[c] tiles in column c
  int[] nBin = new int[LANES];
  int t, paddle, score, drops, banner, spawnIn, over, klaxes, chain;
  string bannerText;
  bool flashOn; int flashT; bool[] flashHit = new bool[LANES * ROWS]; int flashN;
  bool demo; int best;

  public override bool UsesB { get { return false; } }

  public override void Start(int seed, bool demo) {
    this.demo = demo;
    rs = (uint)seed;
    NewGame();
  }
  void NewGame() {                               // newGame(), keeping the rng (as the JS restart does)
    t = 0; nBelt = 0; paddle = 2; nStack = 0;
    bin = new int[LANES][];
    for (int c = 0; c < LANES; c++) { bin[c] = new int[ROWS + 1]; nBin[c] = 0; }
    score = 0; drops = 3; flashOn = false; banner = 0; bannerText = ""; spawnIn = 20; over = 0; klaxes = 0;
  }

  void Spawn() {
    beltLane[nBelt] = (int)Math.Floor(Rnd() * LANES);
    beltKind[nBelt] = (int)Math.Floor(Rnd() * 5);
    beltY[nBelt] = BELT0; nBelt++;
    spawnIn = 34 + (int)Math.Floor(Rnd() * 30) - Math.Min(20, klaxes * 2);
  }

  static int At(int[][] b, int[] n, int c, int r) {
    if (c < 0 || c >= LANES || r < 0 || r >= n[c]) return -1;
    return b[c][r];
  }
  // marks the cells in 3+ runs of the same tile in hit; returns how many
  static int Lines(int[][] b, int[] n, bool[] hit) {
    for (int i = 0; i < hit.Length; i++) hit[i] = false;
    int count = 0;
    for (int d = 0; d < 4; d++) {
      int dc = d == 1 ? 0 : 1, dr = d == 0 ? 0 : d == 3 ? -1 : 1;
      for (int c = 0; c < LANES; c++) for (int r = 0; r < ROWS; r++) {
        int k = At(b, n, c, r); if (k < 0) continue;
        int len = 1; while (At(b, n, c + dc * len, r + dr * len) == k) len++;
        if (len >= 3) for (int i = 0; i < len; i++) {
          int cell = (c + dc * i) * ROWS + (r + dr * i);
          if (!hit[cell]) { hit[cell] = true; count++; }
        }
      }
    }
    return count;
  }

  public override void Tick(int held, int pressed) {
    bool left, right, a;
    if (demo) { int w = Demo(); left = (w & 1) != 0; right = (w & 2) != 0; a = (w & 4) != 0; }
    else { left = (pressed & Buttons.Left) != 0; right = (pressed & Buttons.Right) != 0; a = (pressed & Buttons.A) != 0; }
    Step(left, right, a);
    if (score > best) { best = score; Scores.Report(score, 0, 0, 0); }
  }

  void Step(bool left, bool right, bool a) {
    t++;
    if (over > 0) { if (--over == 0) { Math.Floor(Rnd() * 1e9); NewGame(); } return; }
    if (left) paddle = Math.Max(0, paddle - 1);
    if (right) paddle = Math.Min(LANES - 1, paddle + 1);
    if (a && nStack > 0 && !flashOn) {
      if (nBin[paddle] < ROWS) {
        bin[paddle][nBin[paddle]++] = stack[--nStack];
        flashN = Lines(bin, nBin, flashHit); if (flashN > 0) { flashOn = true; flashT = 18; }
      }
    }
    if (flashOn && --flashT == 0) {                // clear the line, let the bin settle, chain
      int n = flashN;
      for (int c = 0; c < LANES; c++) {
        int k = 0;
        for (int r = 0; r < nBin[c]; r++) if (!flashHit[c * ROWS + r]) bin[c][k++] = bin[c][r];
        nBin[c] = k;
      }
      chain++;
      score += n * 50 * chain; klaxes++;
      banner = 40; bannerText = chain > 1 ? "BLOX *" + chain.ToString() : "BLOX!";
      flashN = Lines(bin, nBin, flashHit);
      if (flashN > 0) { flashOn = true; flashT = 18; } else { flashOn = false; chain = 0; }
    }
    if (--spawnIn <= 0) Spawn();
    double speed = 1 / (5 - Math.Min(3.0, klaxes / 4.0));
    for (int i = 0; i < nBelt; i++) beltY[i] += speed;
    for (int i = 0; i < nBelt; ) {                 // tiles reaching the paddle, in order
      if (beltY[i] < BELT1) { i++; continue; }
      int lane = beltLane[i], kind = beltKind[i];
      for (int j = i; j < nBelt - 1; j++) { beltLane[j] = beltLane[j + 1]; beltKind[j] = beltKind[j + 1]; beltY[j] = beltY[j + 1]; }
      nBelt--;
      if (lane == paddle && nStack < STACK) stack[nStack++] = kind;
      else if (--drops <= 0) { over = 90; bannerText = "GAME OVER"; banner = 90; }
    }
    if (banner > 0) banner--;
  }

  // ---- drawing
  static int Round(double v) { return (int)Math.Floor(v + 0.5); }    // JS Math.round
  static bool TileOn(int kind, int i, int j, int h) {
    bool edge = i == 0 || i == 9 || j == 0 || j == h - 1;
    switch (kind) {
      case 0: return true;
      case 1: return (i + j) % 2 == 0;
      case 2: return edge;
      case 3: return edge || j % 2 == 0;
      default: return edge || (i + j) % 3 == 0;
    }
  }
  static void Tile(Screen s, int x, int y, int kind, int h, bool on) {
    if (!on) return;
    for (int j = 0; j < h; j++) for (int i = 0; i < 10; i++) if (TileOn(kind, i, j, h)) s.Set(x + i, y + j, true);
  }
  static readonly int[] ROLL = { 4, 3, 1, 3 };

  public override void Draw(Screen s) {
    s.Clear();
    if (banner > 0) s.Text((64 - Screen.TextWidth(bannerText)) >> 1, 0, bannerText, true);
    else {
      s.Text(1, 0, score.ToString().PadLeft(5, '0'), true);
      for (int i = 0; i < drops; i++) for (int j = 0; j < 3; j++) { s.Set(61 - i * 4, 2, true); s.Set(60 - i * 4, 1 + j, true); }
    }
    for (int l = 0; l <= LANES; l++) for (int y = BELT0; y <= BELT1; y += 2) s.Set(LX(l) - 1, y, true);
    for (int i = 0; i < nBelt; i++) {
      int h = ROLL[(int)Math.Floor(beltY[i] / 2) % 4];
      Tile(s, LX(beltLane[i]), Round(beltY[i]) - h + 1, beltKind[i], h, true);
    }
    int x = LX(paddle);
    for (int i = -1; i <= 10; i++) s.Set(x + i, PADDLE_Y + 2, true);
    s.Set(x - 1, PADDLE_Y + 1, true); s.Set(x + 10, PADDLE_Y + 1, true);
    for (int i = 0; i < nStack; i++) Tile(s, x, PADDLE_Y - i * 2, stack[i], 2, true);
    for (int i = 2; i < 61; i++) s.Set(i, 63, true);
    for (int y = BIN_Y - 1; y < 64; y++) { s.Set(2, y, true); s.Set(60, y, true); }
    for (int c = 0; c < LANES; c++) for (int r = 0; r < nBin[c]; r++) {
      bool on = !(flashOn && flashHit[c * ROWS + r] && ((flashT >> 2) % 2) != 0);
      Tile(s, LX(c), 64 - 2 - (r + 1) * CELL_H + 1, bin[c][r], CELL_H - 1, on);
    }
  }

  // ---- the demo player: bits 1 left, 2 right, 4 A (as the JS core's demo())
  int[][] trial; int[] trialN = new int[LANES]; bool[] trialHit = new bool[LANES * ROWS];
  int Demo() {
    if (t % 4 != 0) return 0;
    double speed = 1 / (5 - Math.Min(3.0, klaxes / 4.0));
    int near = -1;
    for (int i = 0; i < nBelt; i++) {
      if ((BELT1 - beltY[i]) / speed < (Math.Abs(beltLane[i] - paddle) + 2) * 4 + 6)
        if (near < 0 || beltY[i] > beltY[near]) near = i;          // the lowest; ties: the first (a stable sort)
    }
    bool has = near >= 0 && nStack < STACK; int target = has ? beltLane[near] : 0;
    if (!has && nStack > 0) {
      int k = stack[nStack - 1], bestC = -1; double bestScore = -1e9;
      if (trial == null) { trial = new int[LANES][]; for (int c = 0; c < LANES; c++) trial[c] = new int[ROWS + 1]; }
      for (int c = 0; c < LANES; c++) {
        if (nBin[c] >= ROWS) continue;
        for (int cc = 0; cc < LANES; cc++) { trialN[cc] = nBin[cc]; for (int r = 0; r < nBin[cc]; r++) trial[cc][r] = bin[cc][r]; }
        trial[c][trialN[c]++] = k;
        double sc = Lines(trial, trialN, trialHit) * 100 + (nBin[c] > 0 && bin[c][nBin[c] - 1] == k ? 10 : 0) - nBin[c];
        if (sc > bestScore) { bestScore = sc; bestC = c; }
      }
      target = bestC; has = true;
      if (target == paddle) return 4;
    }
    if (!has) return 0;
    if (target < paddle) return 1;
    if (target > paddle) return 2;
    return 0;
  }
}
