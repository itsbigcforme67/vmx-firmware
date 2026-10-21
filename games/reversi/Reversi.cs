// REVERSI for the VMX: a port of microgames/ui/games/reversi_core.js to C# 1.0 that plays exactly the same (same random
// numbers, same alpha-beta search order and tie-breaks, same screens); tools/parity checks that frame by frame.
// You (solid discs) play the computer (rings). D-pad moves the cursor (legal squares are dotted), A places.
// Demo: the computer plays both sides (as the web simulator's adapter does).
//
// Speed (the JS core's rules, made fast without changing a result):
// - bitboards: a ulong per side, bit i = square i (row * 8 + column). Legal moves, flips, disc counts and the weighted
//   evaluation are a few hundred IL instructions instead of square-by-square loops.
// - the CPU's search is a resumable alpha-beta (an explicit stack per ply) that starts as soon as the CPU is to move
//   (while the last move's discs flip and during the 20-tick thinking wait) and runs a slice of work each tick. On the
//   tick the JS plays the move it finishes whatever is left, then draws the random numbers in the JS order, so the
//   move appears on the same tick with the same choice.
using System;
using Microsoft.Vmx;

public class Reversi : Game {
  const int SQ = 7, BX = 3, BY = 0;
  const int INF = 1000000000;                    // the JS core's 1e9
  const int DEPTH = 4;                           // bestMove's depth
  const int QUOTA = 3000;                        // search work a tick (units below; ~100 IL each): ~300k IL + Draw
  const int W_LEAF = 12, W_NODE = 6;             // work units: a scored leaf, a searched node
  const ulong NOTA = 0xFEFEFEFEFEFEFEFEUL, NOTH = 0x7F7F7F7F7F7F7F7FUL;
  static readonly int[] WEIGHT = { 120, -20, 20, 5, 5, 20, -20, 120, -20, -40, -5, -5, -5, -5, -40, -20, 20, -5, 15, 3, 3, 15, -5, 20,
    5, -5, 3, 3, 3, 3, -5, 5, 5, -5, 3, 3, 3, 3, -5, 5, 20, -5, 15, 3, 3, 15, -5, 20, -20, -40, -5, -5, -5, -5, -40, -20,
    120, -20, 20, 5, 5, 20, -20, 120 };
  static readonly int[] DISC1 = { 6, 15, 15, 6 }, DISC2 = { 6, 9, 9, 6 };   // 4 rows of 4 bits (bit 3 = column 0)

  int[] rowW = new int[8 * 256];                 // rowW[r * 256 + byte]: WEIGHT summed over row r's discs
  int[] lowIdx = new int[64];                    // de Bruijn: index of a single bit

  uint rs;                                       // the JS core's rng()
  double Rnd() { rs = rs * 1664525u + 1013904223u; return rs / 4294967296.0; }

  ulong G1, G2;                                  // the board: side 1 (you), side 2 (the CPU)
  int t, turn, curR, curC, msgT, over, think;
  string msg;
  int[] flipI = new int[64], flipT = new int[64], flipTo = new int[64]; int nFlips;
  int placedI = -1, placedT;                     // g.placed (null = -1); not reset by a restart, as in the JS
  bool demo;
  int wins, lost, drawn; bool wasOver;           // the simulator's tally() and rec

  public override bool UsesB { get { return false; } }

  public override void Start(int seed, bool demo) {
    this.demo = demo;
    for (int r = 0; r < 8; r++) for (int v = 0; v < 256; v++) {
      int s = 0; for (int c = 0; c < 8; c++) if (((v >> c) & 1) != 0) s += WEIGHT[r * 8 + c];
      rowW[r * 256 + v] = s;
    }
    for (int i = 0; i < 64; i++) lowIdx[(int)(((1UL << i) * 0x03F79D71B4CB0A89UL) >> 58)] = i;
    byte[] sv = Storage.Load();
    if (sv != null && sv.Length >= 12) { wins = GetInt(sv, 0); lost = GetInt(sv, 4); drawn = GetInt(sv, 8); }
    NewGame((uint)seed);
  }
  static int GetInt(byte[] s, int o) { return s[o] | s[o + 1] << 8 | s[o + 2] << 16 | s[o + 3] << 24; }
  static void PutInt(byte[] s, int o, int v) { s[o] = (byte)v; s[o + 1] = (byte)(v >> 8); s[o + 2] = (byte)(v >> 16); s[o + 3] = (byte)(v >> 24); }

  void NewGame(uint seed) {
    rs = seed; t = 0;
    G1 = (1UL << 28) | (1UL << 35); G2 = (1UL << 27) | (1UL << 36);
    turn = 1; curR = 2; curC = 3; nFlips = 0; msg = ""; msgT = 0; over = 0; think = 0;
    jobState = 0;
  }

  // ---- bitboard rules
  static int Pop(ulong x) {
    x = x - ((x >> 1) & 0x5555555555555555UL);
    x = (x & 0x3333333333333333UL) + ((x >> 2) & 0x3333333333333333UL);
    x = (x + (x >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
    return (int)((x * 0x0101010101010101UL) >> 56);
  }
  int Low(ulong x) { return lowIdx[(int)(((x & (~x + 1)) * 0x03F79D71B4CB0A89UL) >> 58)]; }
  // legal(b, side) as a set of squares: per direction, walk P's rays through O's discs to an empty square
  static ulong Mob(ulong P, ulong O) {
    ulong e = ~(P | O), ea = e & NOTA, eh = e & NOTH, oa = O & NOTA, oh = O & NOTH, mv = 0, t, pr;
    t = oa & (P << 1); t |= oa & (t << 1); pr = oa & (oa << 1); t |= pr & (t << 2); t |= pr & (t << 2); mv |= (t << 1) & ea;
    t = oh & (P >> 1); t |= oh & (t >> 1); pr = oh & (oh >> 1); t |= pr & (t >> 2); t |= pr & (t >> 2); mv |= (t >> 1) & eh;
    t = O & (P << 8); t |= O & (t << 8); pr = O & (O << 8); t |= pr & (t << 16); t |= pr & (t << 16); mv |= (t << 8) & e;
    t = O & (P >> 8); t |= O & (t >> 8); pr = O & (O >> 8); t |= pr & (t >> 16); t |= pr & (t >> 16); mv |= (t >> 8) & e;
    t = oh & (P << 7); t |= oh & (t << 7); pr = oh & (oh << 7); t |= pr & (t << 14); t |= pr & (t << 14); mv |= (t << 7) & eh;
    t = oa & (P >> 7); t |= oa & (t >> 7); pr = oa & (oa >> 7); t |= pr & (t >> 14); t |= pr & (t >> 14); mv |= (t >> 7) & ea;
    t = oa & (P << 9); t |= oa & (t << 9); pr = oa & (oa << 9); t |= pr & (t << 18); t |= pr & (t << 18); mv |= (t << 9) & ea;
    t = oh & (P >> 9); t |= oh & (t >> 9); pr = oh & (oh >> 9); t |= pr & (t >> 18); t |= pr & (t >> 18); mv |= (t >> 9) & eh;
    return mv;
  }
  // flipsFor(b, i, side) for the move bit x: per direction, the run of O's discs closed by one of P's
  static ulong Flips(ulong P, ulong O, ulong x) {
    ulong r = 0, f, y;
    f = 0; y = (x << 1) & NOTA; while ((y & O) != 0) { f |= y; y = (y << 1) & NOTA; } if ((y & P) != 0) r |= f;
    f = 0; y = (x >> 1) & NOTH; while ((y & O) != 0) { f |= y; y = (y >> 1) & NOTH; } if ((y & P) != 0) r |= f;
    f = 0; y = x << 8; while ((y & O) != 0) { f |= y; y <<= 8; } if ((y & P) != 0) r |= f;
    f = 0; y = x >> 8; while ((y & O) != 0) { f |= y; y >>= 8; } if ((y & P) != 0) r |= f;
    f = 0; y = (x << 7) & NOTH; while ((y & O) != 0) { f |= y; y = (y << 7) & NOTH; } if ((y & P) != 0) r |= f;
    f = 0; y = (x >> 7) & NOTA; while ((y & O) != 0) { f |= y; y = (y >> 7) & NOTA; } if ((y & P) != 0) r |= f;
    f = 0; y = (x << 9) & NOTA; while ((y & O) != 0) { f |= y; y = (y << 9) & NOTA; } if ((y & P) != 0) r |= f;
    f = 0; y = (x >> 9) & NOTH; while ((y & O) != 0) { f |= y; y = (y >> 9) & NOTH; } if ((y & P) != 0) r |= f;
    return r;
  }
  int RowSum(ulong x) {
    return rowW[(int)(x & 255)] + rowW[256 + (int)((x >> 8) & 255)] + rowW[512 + (int)((x >> 16) & 255)]
      + rowW[768 + (int)((x >> 24) & 255)] + rowW[1024 + (int)((x >> 32) & 255)] + rowW[1280 + (int)((x >> 40) & 255)]
      + rowW[1536 + (int)((x >> 48) & 255)] + rowW[1792 + (int)(x >> 56)];
  }
  int Evaluate(ulong p1, ulong p2) {             // from side 2's view
    int s = RowSum(p2) - RowSum(p1) + (Pop(Mob(p2, p1)) - Pop(Mob(p1, p2))) * 8;
    if (Pop(p1 | p2) > 54) s += (Pop(p2) - Pop(p1)) * 10;
    return s;
  }

  // ---- the CPU's search: bestMove(g, side, 4) as a resumable alpha-beta. Frame f = one search() call; boards
  // B1/B2[f] are its position; the JS's legal() order (ascending squares) is the lowest-bit-first order.
  const int PLIES = 8;
  ulong[] B1 = new ulong[PLIES], B2 = new ulong[PLIES], fRem = new ulong[PLIES];
  int[] fSide = new int[PLIES], fDepth = new int[PLIES], fA = new int[PLIES], fB = new int[PLIES], fBest = new int[PLIES];
  bool[] fPass = new bool[PLIES];
  int sp, retVal, work; bool haveRet;
  int jobState, jobSide; ulong rootRem; int rootN; int[] rootI = new int[64], rootV = new int[64];

  void StartJob() {
    jobSide = turn; rootRem = turn == 1 ? Mob(G1, G2) : Mob(G2, G1);
    rootN = 0; sp = -1; haveRet = false; jobState = 1;
  }
  void Enter(int ply, int side, int depth, int a, int b) {
    ulong p1 = B1[ply], p2 = B2[ply];
    if (depth == 0) { work += W_LEAF; retVal = Evaluate(p1, p2); haveRet = true; sp = ply - 1; return; }
    work += W_NODE;
    ulong ms = side == 1 ? Mob(p1, p2) : Mob(p2, p1);
    if (ms == 0) {
      ulong mo = side == 1 ? Mob(p2, p1) : Mob(p1, p2);
      if (mo == 0) { retVal = (Pop(p2) - Pop(p1)) * 1000; haveRet = true; sp = ply - 1; return; }
      fPass[ply] = true; sp = ply; B1[ply + 1] = p1; B2[ply + 1] = p2;
      Enter(ply + 1, 3 - side, depth - 1, a, b); return;
    }
    if (depth == 1) {                            // the children are leaves: score them here, in order
      int best = side == 2 ? -INF : INF;
      while (ms != 0) {
        ulong low = ms & (~ms + 1); ms ^= low; ulong c1, c2;
        if (side == 1) { ulong f = Flips(p1, p2, low); c1 = p1 | f | low; c2 = p2 & ~f; }
        else { ulong f = Flips(p2, p1, low); c2 = p2 | f | low; c1 = p1 & ~f; }
        work += W_LEAF; int v = Evaluate(c1, c2);
        if (side == 2) { if (v > best) best = v; if (best > a) a = best; }
        else { if (v < best) best = v; if (best < b) b = best; }
        if (a >= b) break;
      }
      retVal = best; haveRet = true; sp = ply - 1; return;
    }
    fPass[ply] = false; fSide[ply] = side; fDepth[ply] = depth; fA[ply] = a; fB[ply] = b;
    fBest[ply] = side == 2 ? -INF : INF; fRem[ply] = ms; sp = ply;
    NextChild(ply);
  }
  void NextChild(int ply) {
    ulong rem = fRem[ply], low = rem & (~rem + 1); fRem[ply] = rem ^ low;
    int side = fSide[ply]; ulong p1 = B1[ply], p2 = B2[ply];
    if (side == 1) { ulong f = Flips(p1, p2, low); B1[ply + 1] = p1 | f | low; B2[ply + 1] = p2 & ~f; }
    else { ulong f = Flips(p2, p1, low); B2[ply + 1] = p2 | f | low; B1[ply + 1] = p1 & ~f; }
    Enter(ply + 1, 3 - side, fDepth[ply] - 1, fA[ply], fB[ply]);
  }
  // runs the search until work reaches limit or it is done
  void Work(int limit) {
    while (work < limit) {
      if (sp < 0) {
        if (haveRet) { haveRet = false; rootV[rootN - 1] = retVal; }
        if (rootRem == 0) { jobState = 2; return; }
        ulong low = rootRem & (~rootRem + 1); rootRem ^= low; rootI[rootN++] = Low(low);
        if (jobSide == 1) { ulong f = Flips(G1, G2, low); B1[0] = G1 | f | low; B2[0] = G2 & ~f; }
        else { ulong f = Flips(G2, G1, low); B2[0] = G2 | f | low; B1[0] = G1 & ~f; }
        Enter(0, 3 - jobSide, DEPTH - 1, -INF, INF);
        continue;
      }
      int v = retVal, k = sp; haveRet = false;
      if (fPass[k]) { haveRet = true; sp = k - 1; continue; }
      if (fSide[k] == 2) { if (v > fBest[k]) fBest[k] = v; if (fBest[k] > fA[k]) fA[k] = fBest[k]; }
      else { if (v < fBest[k]) fBest[k] = v; if (fBest[k] < fB[k]) fB[k] = fBest[k]; }
      if (fA[k] >= fB[k] || fRem[k] == 0) { retVal = fBest[k]; haveRet = true; sp = k - 1; }
      else NextChild(k);
    }
  }
  int BestMove() {                               // bestMove(g, turn): -1 = null
    if (jobState == 0) StartJob();
    if (jobState == 1) Work(INF);
    jobState = 0;
    int bestI = -1; double bestV = 0;
    for (int k = 0; k < rootN; k++) {
      int iv = rootV[k]; if (jobSide == 1) iv = -iv;
      double v = iv + Rnd() * 4;
      if (bestI < 0 || v > bestV) { bestI = rootI[k]; bestV = v; }
    }
    return bestI;
  }
  bool CpuTurn() { return demo || turn == 2; }

  void Play(int i) {
    ulong x = 1UL << i, f;
    if (turn == 1) { f = Flips(G1, G2, x); G1 |= f | x; G2 &= ~f; } else { f = Flips(G2, G1, x); G2 |= f | x; G1 &= ~f; }
    nFlips = 0;
    while (f != 0) { ulong low = f & (~f + 1); f ^= low; flipI[nFlips] = Low(low); flipT[nFlips] = 0; flipTo[nFlips] = turn; nFlips++; }
    placedI = i; placedT = 0;
    jobState = 0;
    turn = 3 - turn; AfterTurn();
  }
  bool CanMove(int side) { return (side == 1 ? Mob(G1, G2) : Mob(G2, G1)) != 0; }
  void AfterTurn() {
    if (CanMove(turn)) return;
    if (!CanMove(3 - turn)) {                    // nobody can move: count
      int n1 = Pop(G1), n2 = Pop(G2);
      msg = n1 > n2 ? "YOU WIN!" : n1 < n2 ? "YOU LOSE" : "DRAW"; msgT = 150; over = 150; return;
    }
    msg = turn == 1 ? "YOU PASS" : "CPU PASS"; msgT = 40; turn = 3 - turn;
  }

  public override void Tick(int held, int pressed) {
    work = 0;
    Step(demo ? 0 : pressed);
    // search ahead while the CPU waits (the board can't change until it moves)
    if (jobState == 0 && over == 0 && CpuTurn()) StartJob();
    if (jobState == 1) Work(QUOTA);
    if (!demo) {                                  // tally(): a finished game counts once (the simulator skips demos)
      bool isOver = over > 0, was = wasOver; wasOver = isOver;
      if (isOver && !was) {
        if (msg == "YOU WIN!") wins++; else if (msg == "YOU LOSE") lost++; else drawn++;
        Scores.Report(wins, wins, lost, drawn);
        byte[] s = new byte[12]; PutInt(s, 0, wins); PutInt(s, 4, lost); PutInt(s, 8, drawn); Storage.Save(s);
      }
    }
  }

  void Step(int inp) {
    t++;
    if (msgT != 0) msgT--;
    int k2 = 0;
    for (int k = 0; k < nFlips; k++) {
      flipT[k]++;
      if (flipT[k] < 10) { flipI[k2] = flipI[k]; flipT[k2] = flipT[k]; flipTo[k2] = flipTo[k]; k2++; }
    }
    nFlips = k2;
    if (placedI >= 0 && ++placedT > 8) placedI = -1;
    if (over != 0) { if (--over == 0) NewGame((uint)(int)Math.Floor(Rnd() * 1e9)); return; }
    if (nFlips > 0 || (msgT != 0 && (msg == "YOU PASS" || msg == "CPU PASS"))) return;
    if (CpuTurn()) {
      if (++think < 20) return; think = 0;
      int i = BestMove(); if (i >= 0) Play(i);
      return;
    }
    int r = curR, c = curC;                       // as the JS: each direction rebuilds cur from the old r, c
    if ((inp & Buttons.Up) != 0) { curR = Math.Max(0, r - 1); curC = c; }
    if ((inp & Buttons.Down) != 0) { curR = Math.Min(7, r + 1); curC = c; }
    if ((inp & Buttons.Left) != 0) { curR = r; curC = Math.Max(0, c - 1); }
    if ((inp & Buttons.Right) != 0) { curR = r; curC = Math.Min(7, c + 1); }
    if ((inp & Buttons.A) != 0) { int i = curR * 8 + curC; if (((Mob(G1, G2) >> i) & 1) != 0) Play(i); }
  }

  // ---- drawing
  static void Disc(Screen s, int x, int y, int side, int w) {   // 4 x 4 disc; w = 2 is the mid-flip edge-on view
    int[] rows = side == 1 ? DISC1 : DISC2;
    for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) {
      if (w == 2 && (c == 0 || c == 3)) continue;
      bool on = w == 2 ? !(r > 0 && r < 3 && side == 2) : ((rows[r] >> (3 - c)) & 1) != 0;
      if (on) s.Set(x + c, y + r, true);
    }
  }

  public override void Draw(Screen s) {
    s.Clear();
    for (int k = 0; k <= 8; k++) { s.Fill(BX + k * SQ, BY, 1, 57, true); s.Fill(BX, BY + k * SQ, 57, 1, true); }
    ulong occ = G1 | G2;
    while (occ != 0) {
      ulong low = occ & (~occ + 1); occ ^= low; int i = Low(low);
      int v = (G1 & low) != 0 ? 1 : 2;
      int x = BX + (i & 7) * SQ + 2, y = BY + (i >> 3) * SQ + 2, f = -1;
      for (int k = 0; k < nFlips; k++) if (flipI[k] == i) f = k;          // a Map: the last one wins
      if (f >= 0) Disc(s, x, y, flipT[f] < 5 ? 3 - flipTo[f] : flipTo[f], flipT[f] >= 3 && flipT[f] < 7 ? 2 : 4);
      else if (placedI == i && placedT < 3) Disc(s, x, y, v, 2);
      else Disc(s, x, y, v, 4);
    }
    if (turn == 1 && over == 0) {
      if (((t >> 4) & 1) != 0) {
        ulong m = Mob(G1, G2);
        while (m != 0) { ulong low = m & (~m + 1); m ^= low; int i = Low(low); s.Fill(BX + (i & 7) * SQ + 3, BY + (i >> 3) * SQ + 3, 2, 2, true); }
      }
      int x = BX + curC * SQ, y = BY + curR * SQ;
      s.Fill(x + 1, y + 1, 6, 1, true); s.Fill(x + 1, y + 6, 6, 1, true);
      s.Fill(x + 1, y + 1, 1, 6, true); s.Fill(x + 6, y + 1, 1, 6, true);
    }
    int n1 = Pop(G1), n2 = Pop(G2);
    bool blink = over == 0 && ((t >> 3) & 1) != 0;
    if (!(blink && turn == 1)) Disc(s, 4, 59, 1, 4);
    s.Text(10, 58, n1.ToString().PadLeft(2, '0'), true);
    if (!(blink && turn == 2)) Disc(s, 45, 59, 2, 4);
    s.Text(51, 58, n2.ToString().PadLeft(2, '0'), true);
    if (msgT != 0) {
      int w = msg.Length * 4 + 3, x = BX + (int)Math.Floor((57 - w) / 2.0 + 0.5);
      s.Fill(x, 26, w, 11, false);
      s.Fill(x, 26, w, 1, true); s.Fill(x, 36, w, 1, true); s.Fill(x, 26, 1, 11, true); s.Fill(x + w - 1, 26, 1, 11, true);
      s.Text(x + 2, 29, msg, true);
    }
  }
}
