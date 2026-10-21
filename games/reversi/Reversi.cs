// REVERSI for the VMX: a line-for-line port of microgames/ui/games/reversi_core.js to C# 1.0, so it plays exactly the
// same (same random numbers, same alpha-beta search order and tie-breaks); tools/parity checks that frame by frame.
// You (solid discs) play the computer (rings). D-pad moves the cursor (legal squares are dotted), A places.
// Demo: the computer plays both sides (as the web simulator's adapter does).
using System;
using Microsoft.Vmx;

public class Reversi : Game {
  const int SQ = 7, BX = 3, BY = 0;
  const int INF = 1000000000;                    // the JS core's 1e9
  static readonly int[] DR = { -1, -1, -1, 0, 0, 1, 1, 1 }, DC = { -1, 0, 1, -1, 1, -1, 0, 1 };
  static readonly int[] WEIGHT = { 120, -20, 20, 5, 5, 20, -20, 120, -20, -40, -5, -5, -5, -5, -40, -20, 20, -5, 15, 3, 3, 15, -5, 20,
    5, -5, 3, 3, 3, 3, -5, 5, 5, -5, 3, 3, 3, 3, -5, 5, 20, -5, 15, 3, 3, 15, -5, 20, -20, -40, -5, -5, -5, -5, -40, -20,
    120, -20, 20, 5, 5, 20, -20, 120 };
  // DISC[side]: 4 rows of 4 bits (bit 3 = column 0)
  static readonly int[] DISC1 = { 6, 15, 15, 6 }, DISC2 = { 6, 9, 9, 6 };

  // ray tables: for square i and direction d, how many steps fit on the board, and the index step
  int[] ray = new int[64 * 8]; int[] dstep = new int[8];

  uint rs;                                       // the JS core's rng()
  double Rnd() { rs = rs * 1664525u + 1013904223u; return rs / 4294967296.0; }

  int[] b = new int[64];
  int t, turn, curR, curC, msgT, over, think;
  string msg;
  int[] flipI = new int[64], flipT = new int[64], flipTo = new int[64]; int nFlips;
  int placedI = -1, placedT;                     // g.placed (null = -1); not reset by a restart, as in the JS
  bool demo;
  int wins, lost, drawn; bool wasOver;           // the simulator's tally() and rec

  // search buffers, one per ply (no allocation inside the search)
  const int PLIES = 8;
  int[][] boards, moves;
  int[] tmpMoves = new int[64], tmpFlips = new int[64];

  public override bool UsesB { get { return false; } }

  public override void Start(int seed, bool demo) {
    this.demo = demo;
    for (int i = 0; i < 64; i++) for (int d = 0; d < 8; d++) {
      int r = i >> 3, c = i & 7, n = 0;
      while (true) { r += DR[d]; c += DC[d]; if (r < 0 || r >= 8 || c < 0 || c >= 8) break; n++; }
      ray[i * 8 + d] = n;
    }
    for (int d = 0; d < 8; d++) dstep[d] = DR[d] * 8 + DC[d];
    boards = new int[PLIES][]; moves = new int[PLIES][];
    for (int p = 0; p < PLIES; p++) { boards[p] = new int[64]; moves[p] = new int[64]; }
    byte[] s = Storage.Load();
    if (s != null && s.Length >= 12) { wins = GetInt(s, 0); lost = GetInt(s, 4); drawn = GetInt(s, 8); }
    NewGame((uint)seed);
  }
  static int GetInt(byte[] s, int o) { return s[o] | s[o + 1] << 8 | s[o + 2] << 16 | s[o + 3] << 24; }
  static void PutInt(byte[] s, int o, int v) { s[o] = (byte)v; s[o + 1] = (byte)(v >> 8); s[o + 2] = (byte)(v >> 16); s[o + 3] = (byte)(v >> 24); }

  void NewGame(uint seed) {
    rs = seed; t = 0;
    for (int i = 0; i < 64; i++) b[i] = 0;
    b[27] = b[36] = 2; b[28] = b[35] = 1;
    turn = 1; curR = 2; curC = 3; nFlips = 0; msg = ""; msgT = 0; over = 0; think = 0;
  }

  // ---- rules
  bool HasFlip(int[] bd, int i, int side) {     // flipsFor(b, i, side).length > 0
    if (bd[i] != 0) return false;
    int other = 3 - side, o = i * 8;
    for (int d = 0; d < 8; d++) {
      int n = ray[o + d]; if (n < 2) continue;
      int st = dstep[d], j = i + st;
      if (bd[j] != other) continue;
      int k = 1; j += st;
      while (k < n && bd[j] == other) { k++; j += st; }
      if (k < n && bd[j] == side) return true;
    }
    return false;
  }
  int FlipsFor(int[] bd, int i, int side, int[] outv) {   // the discs placing at i would flip, in the JS order
    if (bd[i] != 0) return 0;
    int other = 3 - side, o = i * 8, cnt = 0;
    for (int d = 0; d < 8; d++) {
      int n = ray[o + d]; if (n < 2) continue;
      int st = dstep[d], j = i + st, k = 0;
      while (k < n && bd[j] == other) { k++; j += st; }
      if (k > 0 && k < n && bd[j] == side) { int q = i + st; for (int m = 0; m < k; m++) { outv[cnt++] = q; q += st; } }
    }
    return cnt;
  }
  int Legal(int[] bd, int side, int[] outv) {
    int n = 0;
    for (int i = 0; i < 64; i++) if (HasFlip(bd, i, side)) outv[n++] = i;
    return n;
  }
  int CountLegal(int[] bd, int side) {
    int n = 0;
    for (int i = 0; i < 64; i++) if (HasFlip(bd, i, side)) n++;
    return n;
  }
  void Place(int[] src, int i, int side, int[] dst) {      // place(b, i, side).b into dst
    Array.Copy(src, 0, dst, 0, 64);
    int other = 3 - side, o = i * 8;
    for (int d = 0; d < 8; d++) {
      int n = ray[o + d]; if (n < 2) continue;
      int st = dstep[d], j = i + st, k = 0;
      while (k < n && src[j] == other) { k++; j += st; }
      if (k > 0 && k < n && src[j] == side) { int q = i + st; for (int m = 0; m < k; m++) { dst[q] = side; q += st; } }
    }
    dst[i] = side;
  }

  int Evaluate(int[] bd) {                       // from side 2's view
    int s = 0, n1 = 0, n2 = 0;
    for (int i = 0; i < 64; i++) { int v = bd[i]; if (v == 2) { s += WEIGHT[i]; n2++; } else if (v == 1) { s -= WEIGHT[i]; n1++; } }
    int m2 = CountLegal(bd, 2), m1 = CountLegal(bd, 1);
    return s + (m2 - m1) * 8 + (n1 + n2 > 54 ? (n2 - n1) * 10 : 0);
  }
  // search(boards[ply], side, depth, a, bb); the JS computes legal() before the depth test too, without effect
  int Search(int ply, int side, int depth, int a, int bb) {
    int[] bd = boards[ply];
    if (depth == 0) return Evaluate(bd);
    int[] ms = moves[ply];
    int n = Legal(bd, side, ms);
    if (n == 0) {
      if (CountLegal(bd, 3 - side) == 0) { int d = 0; for (int i = 0; i < 64; i++) { int v = bd[i]; d += v == 2 ? 1 : v == 1 ? -1 : 0; } return d * 1000; }
      Array.Copy(bd, 0, boards[ply + 1], 0, 64);
      return Search(ply + 1, 3 - side, depth - 1, a, bb);
    }
    int[] nb = boards[ply + 1];
    if (side == 2) {
      int best = -INF;
      for (int k = 0; k < n; k++) {
        Place(bd, ms[k], 2, nb); best = Math.Max(best, Search(ply + 1, 1, depth - 1, a, bb));
        a = Math.Max(a, best); if (a >= bb) break;
      }
      return best;
    } else {
      int best = INF;
      for (int k = 0; k < n; k++) {
        Place(bd, ms[k], 1, nb); best = Math.Min(best, Search(ply + 1, 2, depth - 1, a, bb));
        bb = Math.Min(bb, best); if (a >= bb) break;
      }
      return best;
    }
  }
  int BestMove(int side) {                       // -1 = null
    const int depth = 4;
    int n = Legal(b, side, tmpMoves);
    int bestI = -1; double bestV = 0;
    for (int k = 0; k < n; k++) {
      int i = tmpMoves[k];
      Place(b, i, side, boards[0]);
      int iv = Search(0, 3 - side, depth - 1, -INF, INF); if (side == 1) iv = -iv;
      double v = iv + Rnd() * 4;
      if (bestI < 0 || v > bestV) { bestI = i; bestV = v; }
    }
    return bestI;
  }

  void Play(int i) {
    int nf = FlipsFor(b, i, turn, tmpFlips);
    for (int k = 0; k < nf; k++) { flipI[k] = tmpFlips[k]; flipT[k] = 0; flipTo[k] = turn; b[tmpFlips[k]] = turn; }
    b[i] = turn; nFlips = nf;
    placedI = i; placedT = 0;
    turn = 3 - turn; AfterTurn();
  }
  void AfterTurn() {
    if (CountLegal(b, turn) > 0) return;
    if (CountLegal(b, 3 - turn) == 0) {          // nobody can move: count
      int n1 = 0, n2 = 0; for (int i = 0; i < 64; i++) { if (b[i] == 1) n1++; else if (b[i] == 2) n2++; }
      msg = n1 > n2 ? "YOU WIN!" : n1 < n2 ? "YOU LOSE" : "DRAW"; msgT = 150; over = 150; return;
    }
    msg = turn == 1 ? "YOU PASS" : "CPU PASS"; msgT = 40; turn = 3 - turn;
  }

  public override void Tick(int held, int pressed) {
    if (demo) Step(0, true);
    else Step(pressed, false);
    if (!demo) {                                  // tally(): a finished game counts once (the simulator skips demos)
      bool isOver = over > 0, was = wasOver; wasOver = isOver;
      if (isOver && !was) {
        if (msg == "YOU WIN!") wins++; else if (msg == "YOU LOSE") lost++; else drawn++;
        Scores.Report(wins, wins, lost, drawn);
        byte[] s = new byte[12]; PutInt(s, 0, wins); PutInt(s, 4, lost); PutInt(s, 8, drawn); Storage.Save(s);
      }
    }
  }

  void Step(int inp, bool bothCpu) {
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
    if (bothCpu || turn == 2) {
      if (++think < 20) return; think = 0;
      int i = BestMove(turn); if (i >= 0) Play(i);
      return;
    }
    int r = curR, c = curC;                       // as the JS: each direction rebuilds cur from the old r, c
    if ((inp & Buttons.Up) != 0) { curR = Math.Max(0, r - 1); curC = c; }
    if ((inp & Buttons.Down) != 0) { curR = Math.Min(7, r + 1); curC = c; }
    if ((inp & Buttons.Left) != 0) { curR = r; curC = Math.Max(0, c - 1); }
    if ((inp & Buttons.Right) != 0) { curR = r; curC = Math.Min(7, c + 1); }
    if ((inp & Buttons.A) != 0) { int i = curR * 8 + curC; if (HasFlip(b, i, 1)) Play(i); }
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
    for (int i = 0; i < 64; i++) {
      int v = b[i]; if (v == 0) continue;
      int x = BX + (i & 7) * SQ + 2, y = BY + (i >> 3) * SQ + 2, f = -1;
      for (int k = 0; k < nFlips; k++) if (flipI[k] == i) f = k;          // a Map: the last one wins
      if (f >= 0) Disc(s, x, y, flipT[f] < 5 ? 3 - flipTo[f] : flipTo[f], flipT[f] >= 3 && flipT[f] < 7 ? 2 : 4);
      else if (placedI == i && placedT < 3) Disc(s, x, y, v, 2);
      else Disc(s, x, y, v, 4);
    }
    if (turn == 1 && over == 0) {
      if (((t >> 4) & 1) != 0) {
        int n = Legal(b, 1, tmpMoves);
        for (int k = 0; k < n; k++) { int i = tmpMoves[k]; s.Fill(BX + (i & 7) * SQ + 3, BY + (i >> 3) * SQ + 3, 2, 2, true); }
      }
      int x = BX + curC * SQ, y = BY + curR * SQ;
      s.Fill(x + 1, y + 1, 6, 1, true); s.Fill(x + 1, y + 6, 6, 1, true);
      s.Fill(x + 1, y + 1, 1, 6, true); s.Fill(x + 6, y + 1, 1, 6, true);
    }
    int n1 = 0, n2 = 0; for (int i = 0; i < 64; i++) { if (b[i] == 1) n1++; else if (b[i] == 2) n2++; }
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
