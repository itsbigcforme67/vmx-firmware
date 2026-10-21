// JUNCTION for the VMX: a Digidrive-style puzzle (working name; "Digidrive" is Nintendo's / Q-Games'). A line-for-line
// port of microgames/ui/games/junction_core.js to C# 1.0, so it plays exactly the same (same random numbers, same
// order of events, same doubles); tools/parity checks that frame by frame. Hold a direction to turn the shapes at the
// junction into that road (nothing held = straight on); A launches an attack shape from stock. The JS core draws with
// its own 3x5 font, so that font is here too.
using System;
using Microsoft.Vmx;

public class Junction : Game {
  const int W = 64, H = 64;
  const int CX = 24, CY = 32, ARM = 24, CORE = 4;
  static readonly int[] OX = { 0, 1, 0, -1 }, OY = { -1, 0, 1, 0 };     // N E S W, pointing away from the centre
  static int SLOT(int k) { return 22 - 4 * k; }
  static readonly string[][] SHAPES = {
    new string[] { "#..", ".#.", "#.." }, new string[] { ".#.", "#.#", ".#." }, new string[] { "##.", "###", "##." } };
  static readonly int[] BASE = { 0, 100, 450, 1800, 3000, 6000, 8000 }, STEP = { 0, 10, 10, 10, 10, 10, 85 },
    NEXT = { 0, 4, 8, 12, 24, 40 };
  const int UNIT = 16384, GAP_MAX = 0x4ce00, PACKET = 576, HUNGER = 1209;   // Math.round(108800 / 45 / 2)
  static readonly int[] R_CAP = { 1200, 1400, 1600 }, MILESTONE = { 5000, 6250, 7500 };
  const string FONTCH = "ABCDEGHIJLMNOPRSTUVXY:!0123456789 ?";
  static readonly string[] FONT = {
    "010101111101101", "110101110101110", "011100100100011", "110101101101110", "111100110100111", "011100101101011",
    "101101111101101", "111010010010111", "001001001101010", "100100100100111", "101111111101101", "110101101101101",
    "010101101101010", "110101110100100", "110101110101101", "011100010001110", "111010010010010", "101101101101111",
    "101101101101010", "101101010101101", "101101010010010", "000010000010000", "010010010000010",
    "111101101101111", "010110010010111", "110001010100111", "110001010001110", "101101111001001",
    "111100110001110", "011100111101111", "111001010010010", "111101111101111", "111101111001110",
    "000000000000000", "110001010000010" };

  uint rs;
  double Rnd() { rs = rs * 1664525u + 1013904223u; return rs / 4294967296.0; }

  // game state (the JS object g)
  int t, level = 1, best; bool title = true, over_; int overT;
  bool pUp, pDown, pLeft, pRight, pA;                       // g.prev
  int dist, pool, packets, piston, R, route, spawnT, attacks, nextBonus, shapes, playT; bool trance, warn;
  const int NP = 96;
  int[] pSide = new int[NP], pShape = new int[NP]; double[] pR = new double[NP]; bool[] pOut = new bool[NP],
    pAtk = new bool[NP], pDone = new bool[NP]; int nP;
  int[][] queue = new int[4][]; int[] qn = new int[4];
  int[] rShape = new int[4], rCount = new int[4], rTier = new int[4], rCnt = new int[4], rHunger = new int[4];
  bool[] rActive = new bool[4];
  const int NU = 32;
  int[] uS = new int[NU], uT = new int[NU]; string[] uText = new string[NU]; bool[] uBig = new bool[NU]; int nU;
  bool demo;
  double[] spiral; bool[] spiralOn;                        // a * 3 + r * 0.35 for each pixel of the Auto Bahn spiral
  int[][] shapeBits;
  Screen sc;

  public override bool UsesB { get { return false; } }

  public Junction() {
    for (int s = 0; s < 4; s++) queue[s] = new int[64];
    shapeBits = new int[3][];
    for (int k = 0; k < 3; k++) { shapeBits[k] = new int[3]; for (int j = 0; j < 3; j++) for (int i = 0; i < 3; i++)
      if (SHAPES[k][j][i] == '#') shapeBits[k][j] |= 1 << i; }
    spiral = new double[49 * 51]; spiralOn = new bool[49 * 51];
    for (int y = 7; y < 58; y++) for (int x = 0; x < 49; x++) {
      double dx = x - CX, dy = y - CY, a = Math.Atan2(dy, dx), r = Hypot(dx, dy);
      spiral[(y - 7) * 49 + x] = a * 3 + r * 0.35; spiralOn[(y - 7) * 49 + x] = r > 6;
    }
  }
  static double Hypot(double x, double y) {                 // V8's Math.hypot (Kahan-summed, scaled by the larger)
    double ax = Math.Abs(x), ay = Math.Abs(y), max = Math.Max(ax, ay);
    if (max == 0) return 0;
    double sum = 0, comp = 0, n, summand, pre;
    n = ax / max; summand = n * n - comp; pre = sum + summand; comp = (pre - sum) - summand; sum = pre;
    n = ay / max; summand = n * n - comp; pre = sum + summand; comp = (pre - sum) - summand; sum = pre;
    return Math.Sqrt(sum) * max;
  }

  public override void Start(int seed, bool demo) {
    this.demo = demo; rs = (uint)seed; t = 0; title = true; over_ = false; level = 1; best = 0;
    pUp = pDown = pLeft = pRight = pA = false; playT = 0;
  }
  void StartPlay() {
    title = false; over_ = false; dist = 0; pool = 0; packets = 0; piston = -138240; R = 0; route = -1; nP = 0;
    for (int s = 0; s < 4; s++) { qn[s] = 0; rShape[s] = -1; rCount[s] = 0; rActive[s] = false; rTier[s] = 1; rCnt[s] = 0; rHunger[s] = 0; }
    spawnT = 30; attacks = 1; nextBonus = MILESTONE[Math.Min(2, level - 1)]; trance = false; shapes = 1;
    nU = 0; overT = 0; warn = false;
  }
  int PodValue(int s) { return rActive[s] ? BASE[rTier[s]] + STEP[rTier[s]] * rCnt[s] : 0; }
  int PodUnits(int s) { int u = 0; for (int i = 1; i < rTier[s] && i < NEXT.Length; i++) u += NEXT[i]; return u + rCnt[s] + 1; }
  static int TierOf(int v) { return v >= 8000 ? 6 : v >= 6000 ? 5 : v >= 3000 ? 4 : v >= 1800 ? 3 : v >= 450 ? 2 : 1; }
  int Metres() { return dist >> 14; }
  double Speed() { return (trance ? 1.0 : 0.45 + Math.Min(0.35, R / 3000.0)) * (1 + level * 0.08); }
  void ClearRoad(int s) { rShape[s] = -1; rCount[s] = 0; rActive[s] = false; rTier[s] = 1; rCnt[s] = 0; }

  bool Spawn(int side, int shape, bool attack) {
    for (int i = 0; i < nP; i++) if (!pOut[i] && pSide[i] == side && pR[i] > ARM - 5) return false;
    if (nP == NP) return false;
    pSide[nP] = side; pR[nP] = ARM + 1; pOut[nP] = false; pShape[nP] = shape; pAtk[nP] = attack; pDone[nP] = false; nP++;
    return true;
  }
  void Popup(int s, string text, bool big) {
    if (nU == NU) return;
    uS[nU] = s; uText[nU] = text; uT[nU] = 0; uBig[nU] = big; nU++;
  }
  void FuelTo(int s, int feeds) {
    for (int k = 0; k < feeds; k++) {
      rCnt[s]++;
      if (rTier[s] < 6 && rCnt[s] >= NEXT[rTier[s]]) { rTier[s]++; rCnt[s] = 0; }
    }
  }
  void BreakRoad(int s, int byShape) {
    if (rActive[s]) {
      int others = 0; for (int i = 0; i < 4; i++) if (i != s && rActive[i]) others++;
      if (trance) Launch(PodValue(s), s);
      else { int u = PodUnits(s); for (int i = 0; i < 4; i++) if (i != s && rActive[i]) { FuelTo(i, u); rHunger[i] = HUNGER; } }
      if (others > 0 && !trance) Popup(s, others > 1 ? "X2" : "+", false);
      pool -= 2560;
    } else for (int k = 0; k < rCount[s]; k++) if (qn[s] < 64) queue[s][qn[s]++] = rShape[s];
    rShape[s] = byShape; rCount[s] = 1; rActive[s] = false; rTier[s] = 1; rCnt[s] = 0; rHunger[s] = 0;
    EndTrance();
  }
  void Launch(int fuel, int s) {
    packets += (int)Math.Round(fuel * 16 / 12.0);
    Popup(s, "GO", true);
  }
  void EndTrance() { if (!trance) return; trance = false; if (attacks < 3) attacks++; }
  void Arrive(int p) {
    int s = pSide[p];
    if (pAtk[p]) {
      if (rActive[s]) { Launch(PodValue(s), s); ClearRoad(s); EndTrance(); }
      return;
    }
    if (rShape[s] < 0 || rCount[s] == 0) { rShape[s] = pShape[p]; rCount[s] = 1; rActive[s] = false; rTier[s] = 1; rCnt[s] = 0; return; }
    if (rShape[s] != pShape[p]) { BreakRoad(s, pShape[p]); return; }
    if (rActive[s]) { FuelTo(s, 1); rHunger[s] = HUNGER; return; }
    rCount[s]++;
    if (rCount[s] >= 5) {
      rActive[s] = true; rTier[s] = 1; rCnt[s] = 0; rHunger[s] = HUNGER; rCount[s] = 5;
      if (rActive[0] && rActive[1] && rActive[2] && rActive[3]) trance = true;
    }
  }

  // [0,1,2,3].sort(() => rnd() - 0.5) as V8 does it (TimSort; four elements = one run + binary insertion)
  int[] sides = new int[4];
  void Shuffle4() {
    for (int i = 0; i < 4; i++) sides[i] = i;
    int run = 2; bool desc = Rnd() - 0.5 < 0;
    for (int i = 2; i < 4; i++) { double o = Rnd() - 0.5; if (desc ? o >= 0 : o < 0) break; run++; }
    if (desc) for (int a = 0, b = run - 1; a < b; a++, b--) { int x = sides[a]; sides[a] = sides[b]; sides[b] = x; }
    for (int st = run; st < 4; st++) {
      int left = 0, right = st, piv = sides[st];
      while (left < right) { int mid = left + ((right - left) >> 1); if (Rnd() - 0.5 < 0) right = mid; else left = mid + 1; }
      for (int q = st; q > left; q--) sides[q] = sides[q - 1];
      sides[left] = piv;
    }
  }

  public override void Tick(int held, int pressed) {
    int b = demo ? Demo() : held;
    Step((b & Buttons.Up) != 0, (b & Buttons.Down) != 0, (b & Buttons.Left) != 0, (b & Buttons.Right) != 0, (b & Buttons.A) != 0);
  }

  void Step(bool up, bool down, bool left, bool right, bool a) {
    t++;
    if (title) {
      if (left && !pLeft) level = Math.Max(1, level - 1);
      if (right && !pRight) level = Math.Min(5, level + 1);
      if (a && !pA) StartPlay();
      Prev(up, down, left, right, a); return;
    }
    if (over_) { if (--overT <= 0) { over_ = false; title = true; } Prev(up, down, left, right, a); return; }
    route = up ? 0 : right ? 1 : down ? 2 : left ? 3 : -1;
    if (a && !pA && attacks > 0) {
      Shuffle4();
      for (int i = 0; i < 4; i++) if (Spawn(sides[i], -1, true)) { attacks--; break; }
    }
    Prev(up, down, left, right, a);
    playT++;
    if (playT >= 52 * 30 && shapes < 2) shapes = 2;
    if (playT >= 210 * 30 && shapes < 3) shapes = 3;
    if (--spawnT <= 0) {
      spawnT = (int)Math.Round(36 * (trance ? 0.4 : 1));
      int s = (int)Math.Floor(Rnd() * 4); bool attack = Rnd() < 0.025;
      if (Spawn(s, attack ? -1 : (int)Math.Floor(Rnd() * shapes), attack)) R = Math.Min(R_CAP[Math.Min(2, level - 1)], R + 1);
    }
    for (int s = 0; s < 4; s++) if (qn[s] > 0 && t % 10 == 0 && Spawn(s, queue[s][0])) {
      for (int k = 1; k < qn[s]; k++) queue[s][k - 1] = queue[s][k]; qn[s]--;
    }
    double v = Speed();
    for (int p = 0; p < nP; p++) {
      if (!pOut[p]) {
        bool ahead = false;
        for (int q = 0; q < nP; q++) if (q != p && !pOut[q] && pSide[q] == pSide[p] && pR[q] < pR[p] && pR[p] - pR[q] < 4.5) { ahead = true; break; }
        if (ahead && pR[p] > CORE + 1) continue;
        pR[p] -= v;
        if (pR[p] <= 0) {
          int from = pSide[p], straight = (from + 2) % 4;
          pSide[p] = route >= 0 && route != from ? route : straight; pOut[p] = true; pR[p] = 0;
        }
      } else {
        pR[p] += v;
        int s = pSide[p]; int target = rActive[s] || pAtk[p] ? CORE + 2 : SLOT(Math.Min(rCount[s], 4));
        if (pR[p] >= target) { Arrive(p); pDone[p] = true; }
      }
    }
    int n = 0;
    for (int p = 0; p < nP; p++) if (!pDone[p]) {
      pSide[n] = pSide[p]; pR[n] = pR[p]; pOut[n] = pOut[p]; pShape[n] = pShape[p]; pAtk[n] = pAtk[p]; pDone[n] = false; n++;
    }
    nP = n;
    for (int s = 0; s < 4; s++) {
      if (!rActive[s]) continue;
      if (--rHunger[s] <= 0) {
        if (trance) Launch(PodValue(s), s);
        ClearRoad(s);
        EndTrance();
      }
    }
    for (int sub = 0; sub < 2; sub++) {
      if (packets > 0) { pool += PACKET; packets--; }
      int sb = Math.Min(pool >> 4, 0x3f00);
      pool -= sb; dist += sb;
      if (!trance) {
        double bse = (R / 4.0 + 40) * 320 / 256;
        piston += Math.Max(1, (int)Math.Floor(bse * (dist - piston + 0x6000) / GAP_MAX));
      }
      if (dist - piston > GAP_MAX) piston = dist - GAP_MAX;
    }
    if (Metres() >= nextBonus) { nextBonus += MILESTONE[Math.Min(2, level - 1)]; if (attacks < 3) attacks++; }
    warn = dist - piston < 3 * UNIT;
    if (piston >= dist) {
      best = Math.Max(best, Metres()); over_ = true; overT = 150;
      if (best > 0) Scores.Report(best, 0, 0, 0);
    }
    n = 0;
    for (int i = 0; i < nU; i++) { uT[i]++; if (uT[i] < 30) { uS[n] = uS[i]; uT[n] = uT[i]; uText[n] = uText[i]; uBig[n] = uBig[i]; n++; } }
    nU = n;
  }
  void Prev(bool up, bool down, bool left, bool right, bool a) { pUp = up; pDown = down; pLeft = left; pRight = right; pA = a; }
  bool Spawn(int side, int shape) { return Spawn(side, shape, false); }

  // ---- demo ----
  int nextP;
  double Value(int s) {
    int p = nextP;
    if (pAtk[p]) return rActive[s] ? 100 + PodValue(s) / 50.0 : -50;
    if (rActive[s]) {
      if (rShape[s] == pShape[p]) return 35 + 70 * (1 - rHunger[s] / (double)HUNGER);
      int others = 0; for (int i = 0; i < 4; i++) if (i != s && rActive[i]) others++;
      return others > 0 ? 5 + PodValue(s) / 50.0 * (others > 1 ? 3 : 1) - 30 : -100;
    }
    if (rCount[s] == 0 || rShape[s] < 0) return 55;
    if (rShape[s] == pShape[p]) return 70 + rCount[s] * 6;
    return -10 - rCount[s] * 4;
  }
  int Demo() {
    if (title) return t % 40 < 2 ? Buttons.A : 0;
    if (over_) return 0;
    int inp = 0;
    nextP = -1;
    for (int p = 0; p < nP; p++) if (!pOut[p] && (nextP < 0 || pR[p] < pR[nextP])) nextP = p;
    if (nextP >= 0) {
      int from = pSide[nextP], bst = -1;
      for (int s = 0; s < 4; s++) { if (s == from) continue; if (bst < 0 || Value(s) > Value(bst)) bst = s; }
      int want = bst == (from + 2) % 4 ? -1 : bst;
      if (want == 0) inp |= Buttons.Up; else if (want == 1) inp |= Buttons.Right;
      else if (want == 2) inp |= Buttons.Down; else if (want == 3) inp |= Buttons.Left;
    }
    int biggest = 0; for (int s = 0; s < 4; s++) if (rActive[s]) biggest = Math.Max(biggest, PodValue(s));
    if (attacks > 0 && warn && biggest >= 100 && t % 3 == 0) inp |= Buttons.A;
    return inp;
  }

  // ---- drawing ----
  void Px(double x, double y, bool on) {
    x = Math.Round(x); y = Math.Round(y);
    if (x >= 0 && y >= 0 && x < W && y < H) sc.Set((int)x, (int)y, on);
  }
  void Px(double x, double y) { Px(x, y, true); }
  void Text(double x, double y, string s) {
    for (int c = 0; c < s.Length; c++) {
      int fi = FONTCH.IndexOf(s[c]);
      if (fi >= 0) { string f = FONT[fi]; for (int r = 0; r < 5; r++) for (int q = 0; q < 3; q++) if (f[r * 3 + q] == '1') Px(x + q, y + r); }
      x += 4;
    }
  }
  void Shape(double x, double y, int k, int dir, bool on) {
    int[] sp = shapeBits[k];
    for (int j = 0; j < 3; j++) for (int i = 0; i < 3; i++) {
      if ((sp[j] >> i & 1) == 0) continue;
      int u = i - 1, w = j - 1, z;
      if (dir == 0) { z = u; u = w; w = -z; } else if (dir == 2) { z = u; u = -w; w = z; } else if (dir == 3) u = -u;
      Px(x + 1 + u, y + 1 + w, on);
    }
  }
  double lx, ly;
  void LanePos(int side, double r, bool outgoing) {
    int ox = OX[side], oy = OY[side], dx = outgoing ? ox : -ox, dy = outgoing ? oy : -oy, rx = -dy, ry = dx;
    lx = CX + ox * r + rx * 2 - 0.5; ly = CY + oy * r + ry * 2 - 0.5;
  }
  static readonly int[] PODN = { 3, 4, 5, 6, 24 };
  double[] ptx = new double[24], pty = new double[24];
  void Pod(double x, double y, int fuel) {
    int tier = TierOf(fuel) - 1;
    if (tier > 4) return;                                   // the JS core's table has no 6th shape: nothing drawn
    int n = PODN[tier]; double R = 3 + Math.Min(tier, 2) * 0.5;
    for (int i = 0; i < n; i++) { double a = -Math.PI / 2 + i * 2 * Math.PI / n; ptx[i] = x + Math.Cos(a) * R; pty[i] = y + Math.Sin(a) * R; }
    for (int i = 0; i < n; i++) {
      double x0 = ptx[i], y0 = pty[i], x1 = ptx[(i + 1) % n], y1 = pty[(i + 1) % n];
      double m = Math.Max(Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)), 1);
      for (int k = 0; k <= m; k++) Px(x0 + (x1 - x0) * k / m, y0 + (y1 - y0) * k / m);
    }
  }

  public override void Draw(Screen screen) {
    sc = screen; sc.Clear();
    if (title) {
      Text(16, 8, "JUNCTION");
      for (int s = 0; s < 4; s++) Shape(CX - 1 + OX[s] * 9 + 8, 28 + OY[s] * 6, (t >> 4) % 3, s, true);
      Text(8, 40, "LEVEL < " + level.ToString() + " >");
      if (((t >> 4) & 1) != 0) Text(16, 48, "A: START");
      if (best != 0) Text(8, 57, "BEST " + best.ToString() + "M");
      return;
    }
    if (trance) {
      double tt = t * 0.25, TAU = Math.PI * 2;
      for (int y = 7; y < 58; y++) for (int x = 0; x < 49; x++) {
        int i = (y - 7) * 49 + x;
        if (spiralOn[i] && ((spiral[i] - tt) % TAU + TAU) % TAU < 0.35) sc.Set(x, y, true);
      }
    }
    for (int s = 0; s < 4; s++) {
      int ox = OX[s], oy = OY[s], qx = -oy, qy = ox;
      for (int r = CORE; r <= ARM; r++) {
        for (int side = -1; side <= 1; side += 2) Px(CX - 0.5 + ox * r + qx * 4.5 * side, CY - 0.5 + oy * r + qy * 4.5 * side, !trance);
        if (r % 3 == 0) Px(CX - 0.5 + ox * r, CY - 0.5 + oy * r);
        if (trance) for (int side = -1; side <= 1; side += 2) Px(CX - 0.5 + ox * r + qx * 4.5 * side, CY - 0.5 + oy * r + qy * 4.5 * side);
      }
      if (rActive[s]) {
        bool blink = rHunger[s] < 60 && ((t >> 2) & 1) != 0;
        for (int k = 0; k < 5; k++) {
          LanePos(s, SLOT(k), true); double x = lx, y = ly;
          if (!blink) for (int j = -2; j <= 2; j++) for (int i = -2; i <= 2; i++) Px(x + 1 + i, y + 1 + j);
          Shape(x, y, rShape[s], s, false);
        }
        int cx = s == 1 || s == 0 ? 37 : 11, cy = s == 0 || s == 3 ? 13 : 51;
        Pod(cx, cy, PodValue(s));
      } else for (int k = 0; k < rCount[s]; k++) { LanePos(s, SLOT(k), true); Shape(lx, ly, rShape[s], s, true); }
    }
    if (route >= 0) {
      int ox = OX[route], oy = OY[route];
      for (int k = -1; k <= 2; k++) Px(CX - 0.5 + ox * k, CY - 0.5 + oy * k);
      Px(CX - 0.5 + ox * 2 - oy, CY - 0.5 + oy * 2 + ox); Px(CX - 0.5 + ox * 2 + oy, CY - 0.5 + oy * 2 - ox);
    } else Px(CX - 0.5, CY - 0.5);
    for (int p = 0; p < nP; p++) {
      LanePos(pSide[p], Math.Max(0, pR[p]), pOut[p]); double x = lx, y = ly;
      int dir = pOut[p] ? pSide[p] : (pSide[p] + 2) % 4;
      if (pAtk[p]) { if (((t >> 1) & 1) != 0) for (int j = 0; j < 3; j++) for (int i = 0; i < 3; i++) if ((i + j) % 2 == 0) Px(x + i, y + j); else Px(x + 1, y + 1); }
      else Shape(x, y, pShape[p], dir, true);
    }
    for (int i = 0; i < nU; i++) {
      int ox = OX[uS[i]], oy = OY[uS[i]];
      if (((uT[i] >> 2) & 1) != 0 || uBig[i]) Text(CX - 4 + ox * 14, CY - 2 + oy * 14 - uT[i] / 4.0, uText[i]);
    }
    string m = Metres().ToString();
    Text(Math.Max(0, 5 - m.Length) * 4, 0, m + "M");
    for (int k = 0; k < 3; k++) {
      int x = 28 + k * 6;
      if (k < attacks) { Px(x + 1, 0); Px(x, 1); Px(x + 2, 1); Px(x + 1, 2); Px(x + 1, 1); Px(x + 1, 3); }
      else Px(x + 1, 2);
    }
    double gap = (dist - piston) / (double)UNIT, scale = 48 / 19.2;
    for (int y = 7; y < 64; y++) { Px(51, y); Px(63, y); }
    double dy = Math.Max(9, 58 - gap * scale);
    for (int j = -2; j <= 2; j++) for (int i = -2; i <= 2; i++) if (i * i + j * j <= 5) Px(57 + i, dy + j);
    if (!(warn && ((t >> 2) & 1) != 0)) for (int y = 60; y < 63; y++) for (int x = 53; x < 62; x++) Px(x, y);
    Px(57, 59); Px(57, 58);                                   // the JS loop runs y = 59, 58
    if (trance) Text(0, 58, "AUTO BAHN");
    if (over_) {
      for (int y = 22; y < 42; y++) for (int x = 2; x < 47; x++) Px(x, y, y == 22 || y == 41 || x == 2 || x == 46);
      Text(8, 25, "GAME OVER"); string s2 = Metres().ToString() + "M"; Text(25 - s2.Length * 2, 33, s2);
    }
  }
}
