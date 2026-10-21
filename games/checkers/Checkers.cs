// CHECKERS for the VMX: American checkers (English draughts) against an alpha-beta CPU. A line-for-line port of
// microgames/ui/games/checkers_core.js to C# 1.0, driven as the web simulator's adapter (POCKET ARCADE vmx/games.js)
// drives it, so it plays exactly the same; tools/parity checks that frame by frame.
// D-pad moves the cursor, A picks up / puts down, B drops the selection. Demo: the CPU plays both sides.
// The search keeps the JS core's move order, alpha-beta cut-offs and tie-breaks; it only avoids its allocations:
// boards live in per-depth pools and multi-jumps are made and unmade in place.
using System;
using Microsoft.Vmx;

public class Checkers : Game {
  const int SQ = 7, BX = 0, BY = 4;
  const int MSG_NONE = 0, MSG_WIN = 1, MSG_LOSE = 2, MSG_DRAW = 3, MSG_KING = 4, MSG_JUMP = 5;
  static readonly string[] MSG = { "", "YOU WIN!", "YOU LOSE", "DRAW", "KING ME", "MUST JUMP" };
  // what Screen.Text draws for each message: the JS core's font has no J, so it draws a blank there
  static readonly string[] MSG_SHOWN = { "", "YOU WIN!", "YOU LOSE", "DRAW", "KING ME", "MUST  UMP" };
  static readonly string[] DIG = { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" };

  // cell values: 0 empty, 1 / 2 = you (moves up) / CPU (moves down), 3 / 4 = their kings
  static readonly int[] OWN = { 0, 1, 2, 1, 2 };
  // directions in the JS order (-1,-1), (-1,1), (1,-1), (1,1); a value uses DLO[v] <= d < DHI[v]
  // (JS dirs(0) is the CPU man's pair: only reachable for an empty cell, never asked for here)
  static readonly int[] DLO = { 2, 0, 2, 0, 0 }, DHI = { 4, 2, 4, 4, 4 };
  static int[] N1, N2;
  static readonly int[] DARK = { 1, 3, 5, 7, 8, 10, 12, 14, 17, 19, 21, 23, 24, 26, 28, 30,
    33, 35, 37, 39, 40, 42, 44, 46, 49, 51, 53, 55, 56, 58, 60, 62 };   // the playable squares, in order                           // per cell * 4 + d: the next cell / the one beyond, -1 off the board

  uint rs;                                       // the JS core's rng(): an LCG, divided down to [0, 1)
  double Rnd() { rs = rs * 1664525u + 1013904223u; return rs / 4294967296.0; }

  // ---- the game state (newGame())
  byte[] b = new byte[64];
  int t, turn, curR, curC, sel, chain, msg, msgT, over, think, quiet;
  int[] lost = new int[2];
  bool animOn; int animFrom, animTo, animV, animT;
  // the CPU's plan: the rest of its chosen turn (survives newGame, as in JS)
  bool planOn; int planSide, planLen, planPos;
  int[] planFrom = new int[16], planTo = new int[16], planCap = new int[16];
  bool demo, wasOver;
  int wins, losses, draws;

  public override bool UsesB { get { return true; } }

  public override void Start(int seed, bool demo) {
    this.demo = demo;
    if (N1 == null) {
      N1 = new int[256]; N2 = new int[256];
      for (int i = 0; i < 64; i++) for (int d = 0; d < 4; d++) {
        int dr = d < 2 ? -1 : 1, dc = (d & 1) == 0 ? -1 : 1, r = i >> 3, c = i & 7;
        int r1 = r + dr, c1 = c + dc, r2 = r + 2 * dr, c2 = c + 2 * dc;
        N1[i * 4 + d] = r1 >= 0 && r1 < 8 && c1 >= 0 && c1 < 8 ? r1 * 8 + c1 : -1;
        N2[i * 4 + d] = r2 >= 0 && r2 < 8 && c2 >= 0 && c2 < 8 ? r2 * 8 + c2 : -1;
      }
    }
    for (int l = 0; l < LEVELS; l++) { pool[l] = new byte[0][]; }
    byte[] sv = Storage.Load();
    if (sv != null && sv.Length >= 12) { wins = Get32(sv, 0); losses = Get32(sv, 4); draws = Get32(sv, 8); }
    NewGame((uint)seed);
  }
  static int Get32(byte[] a, int o) { return a[o] | a[o + 1] << 8 | a[o + 2] << 16 | a[o + 3] << 24; }
  static void Put32(byte[] a, int o, int v) { a[o] = (byte)v; a[o + 1] = (byte)(v >> 8); a[o + 2] = (byte)(v >> 16); a[o + 3] = (byte)(v >> 24); }

  void NewGame(uint seed) {
    rs = seed;
    for (int r = 0; r < 8; r++) for (int c = 0; c < 8; c++) {
      int v = 0;
      if ((r + c) % 2 == 1) { if (r < 3) v = 2; if (r > 4) v = 1; }
      b[r * 8 + c] = (byte)v;
    }
    t = 0; turn = 1; curR = 5; curC = 2; sel = -1; chain = -1; msg = MSG_NONE; msgT = 0; over = 0; think = 0;
    animOn = false; lost[0] = 0; lost[1] = 0; quiet = 0;
  }

  // ---- rules
  static bool PieceHasJump(byte[] bd, int i) {
    int v = bd[i], o = OWN[v], hi = DHI[v];
    for (int d = DLO[v]; d < hi; d++) {
      int to = N2[i * 4 + d]; if (to < 0) continue;
      int m = bd[N1[i * 4 + d]];
      if (m != 0 && OWN[m] != o && bd[to] == 0) return true;
    }
    return false;
  }
  // turns(b, side).length != 0
  static bool HasAny(byte[] bd, int side) {
    for (int q = 0; q < 32; q++) { int i = DARK[q];
      int v = bd[i]; if (OWN[v] != side) continue;
      int hi = DHI[v];
      for (int d = DLO[v]; d < hi; d++) { int to = N1[i * 4 + d]; if (to >= 0 && bd[to] == 0) return true; }
      if (PieceHasJump(bd, i)) return true;
    }
    return false;
  }

  // moves(b, side, only): the legal single steps or jumps, into lFrom / lTo / lCap (cap -1: a step)
  int[] lFrom = new int[64], lTo = new int[64], lCap = new int[64]; int nl;
  void Moves(byte[] bd, int side, int only) {
    nl = 0;
    for (int q = 0; q < 32; q++) { int i = DARK[q];
      if (only >= 0 ? i != only : OWN[bd[i]] != side) continue;
      int v = bd[i], o = OWN[v], hi = DHI[v];
      for (int d = DLO[v]; d < hi; d++) {
        int to = N2[i * 4 + d]; if (to < 0) continue;
        int c = N1[i * 4 + d], m = bd[c];
        if (m != 0 && OWN[m] != o && bd[to] == 0) { lFrom[nl] = i; lTo[nl] = to; lCap[nl] = c; nl++; }
      }
    }
    if (nl > 0 || only >= 0) return;
    for (int q = 0; q < 32; q++) { int i = DARK[q];
      if (OWN[bd[i]] != side) continue;
      int v = bd[i], hi = DHI[v];
      for (int d = DLO[v]; d < hi; d++) {
        int to = N1[i * 4 + d];
        if (to >= 0 && bd[to] == 0) { lFrom[nl] = i; lTo[nl] = to; lCap[nl] = -1; nl++; }
      }
    }
  }
  // apply(b, m) in place; returns 0 = turn over, 1 = turn over and crowned, 2 = the jump goes on
  static int Apply(byte[] bd, int from, int to, int cap) {
    int v = bd[from]; bd[from] = 0; bd[to] = (byte)v;
    bool crowned = false;
    if (v < 3 && ((OWN[v] == 1 && to < 8) || (OWN[v] == 2 && to >= 56))) { bd[to] = (byte)(v + 2); crowned = true; }
    if (cap >= 0) { bd[cap] = 0; if (!crowned && PieceHasJump(bd, to)) return 2; }
    return crowned ? 1 : 0;
  }

  // ---- search: turns() per depth level into a pool of boards (level 0 = bestTurn's, with the paths)
  const int LEVELS = 5;
  byte[][][] pool = new byte[LEVELS][][];
  int[] cnt = new int[LEVELS];
  int[] rootLen = new int[0], rootFrom = new int[0], rootTo = new int[0], rootCap = new int[0];   // 16 a turn
  int[] pFrom = new int[16], pTo = new int[16], pCap = new int[16];                               // the path so far

  void Push(byte[] bd, int level, int plen) {
    int n = cnt[level]; byte[][] p = pool[level];
    if (n == p.Length) {
      byte[][] np = new byte[p.Length + 16][];
      for (int k = 0; k < np.Length; k++) np[k] = k < p.Length ? p[k] : new byte[64];
      pool[level] = p = np;
      if (level == 0) {
        int[] a = new int[np.Length]; Array.Copy(rootLen, 0, a, 0, rootLen.Length); rootLen = a;
        a = new int[np.Length * 16]; Array.Copy(rootFrom, 0, a, 0, rootFrom.Length); rootFrom = a;
        a = new int[np.Length * 16]; Array.Copy(rootTo, 0, a, 0, rootTo.Length); rootTo = a;
        a = new int[np.Length * 16]; Array.Copy(rootCap, 0, a, 0, rootCap.Length); rootCap = a;
      }
    }
    Array.Copy(bd, 0, p[n], 0, 64);
    if (level == 0) {
      rootLen[n] = plen;
      for (int k = 0; k < plen; k++) { rootFrom[n * 16 + k] = pFrom[k]; rootTo[n * 16 + k] = pTo[k]; rootCap[n * 16 + k] = pCap[k]; }
    }
    cnt[level] = n + 1;
  }
  // the jumps from cell i (the piece's v), each made in place, followed while they go on, then unmade
  void JumpsDfs(byte[] bd, int i, int level, int depth) {
    int v = bd[i], o = OWN[v], hi = DHI[v];
    for (int d = DLO[v]; d < hi; d++) {
      int to = N2[i * 4 + d]; if (to < 0) continue;
      int c = N1[i * 4 + d], m = bd[c];
      if (m == 0 || OWN[m] == o || bd[to] != 0) continue;
      int nv = v; bool crowned = false;
      if (v < 3 && ((o == 1 && to < 8) || (o == 2 && to >= 56))) { nv = v + 2; crowned = true; }
      bd[i] = 0; bd[to] = (byte)nv; bd[c] = 0;
      pFrom[depth] = i; pTo[depth] = to; pCap[depth] = c;
      if (!crowned && PieceHasJump(bd, to)) JumpsDfs(bd, to, level, depth + 1);
      else Push(bd, level, depth + 1);
      bd[to] = 0; bd[c] = (byte)m; bd[i] = (byte)v;
    }
  }
  // turns(b, side) into pool[level]; returns how many
  int Turns(byte[] bd, int side, int level) {
    cnt[level] = 0;
    // the jumps first (captures are mandatory); only when there are none, the steps
    for (int q = 0; q < 32; q++) { int i = DARK[q]; if (OWN[bd[i]] == side) JumpsDfs(bd, i, level, 0); }
    if (cnt[level] == 0) {
      for (int q = 0; q < 32; q++) { int i = DARK[q];
        int v = bd[i]; if (OWN[v] != side) continue;
        int hi = DHI[v];
        for (int d = DLO[v]; d < hi; d++) {
          int to = N1[i * 4 + d]; if (to < 0 || bd[to] != 0) continue;
          int nv = v;
          if (v < 3 && ((side == 1 && to < 8) || (side == 2 && to >= 56))) nv = v + 2;
          bd[i] = 0; bd[to] = (byte)nv;
          pFrom[0] = i; pTo[0] = to; pCap[0] = -1;
          Push(bd, level, 1);
          bd[to] = 0; bd[i] = (byte)v;
        }
      }
    }
    return cnt[level];
  }
  static int Evaluate(byte[] bd) {                // from player 2's view
    int s = 0;
    for (int q = 0; q < 32; q++) { int i = DARK[q];
      int v = bd[i]; if (v == 0) continue;
      int r = i >> 3, c = i & 7, o = OWN[v];
      bool king = v >= 3;
      int val = king ? 160 : 100;
      if (!king) val += (o == 2 ? r : 7 - r) * 4;
      if (c == 0 || c == 7) val += 4;
      if (!king && ((o == 2 && r == 0) || (o == 1 && r == 7))) val += 8;
      if (o == 2) s += val; else s -= val;
    }
    return s;
  }
  int Search(byte[] bd, int side, int depth, int a, int bb, int level) {
    if (depth == 0) {                            // JS builds every turn here only to see if there is one
      if (!HasAny(bd, side)) return side == 2 ? -100000 : 100000;
      return Evaluate(bd);
    }
    int n = Turns(bd, side, level);
    if (n == 0) return side == 2 ? -100000 : 100000;
    byte[][] p = pool[level];
    if (side == 2) {
      int best = -1000000000;
      for (int k = 0; k < n; k++) {
        best = Math.Max(best, Search(p[k], 1, depth - 1, a, bb, level + 1));
        a = Math.Max(a, best); if (a >= bb) break;
      }
      return best;
    }
    int bst = 1000000000;
    for (int k = 0; k < n; k++) {
      bst = Math.Min(bst, Search(p[k], 2, depth - 1, a, bb, level + 1));
      bb = Math.Min(bb, bst); if (a >= bb) break;
    }
    return bst;
  }
  // bestTurn(g, side, 5): the chosen turn's index in pool[0], or -1
  int BestTurn(int side) {
    int n = Turns(b, side, 0); if (n == 0) return -1;
    int best = -1; double bestV = 0;
    for (int k = 0; k < n; k++) {
      int sv = Search(pool[0][k], side == 1 ? 2 : 1, 4, -1000000000, 1000000000, 1); if (side == 1) sv = -sv;
      double v = sv + Rnd() * 6;                 // a little variety between equal moves
      if (best < 0 || v > bestV) { best = k; bestV = v; }
    }
    return best;
  }

  // ---- play
  void FinishTurn() {
    turn = turn == 1 ? 2 : 1; chain = -1; sel = -1;
    if (!HasAny(b, turn)) { over = 150; msg = turn == 1 ? MSG_LOSE : MSG_WIN; msgT = 150; }
  }
  void Play(int from, int to, int cap) {
    int before = b[from];
    quiet = (cap >= 0 || before < 3) ? 0 : quiet + 1;     // draw: 40 king moves, no capture
    int r = Apply(b, from, to, cap);
    animOn = true; animFrom = from; animTo = to; animV = before; animT = 0;
    if (cap >= 0) lost[OWN[before] == 1 ? 1 : 0]++;
    if (r == 1) { msg = MSG_KING; msgT = 40; }
    if (r == 2) { chain = to; sel = to; } else FinishTurn();
    if (over == 0 && quiet >= 40) { over = 150; msg = MSG_DRAW; msgT = 150; }
  }

  public override void Tick(int held, int pressed) {
    Step(demo ? 0 : pressed);
    // the simulator's tally(): a finished game counts once, from its message (not in demo)
    if (!demo) {
      bool o = over > 0, was = wasOver; wasOver = o;
      if (o && !was) {
        if (msg == MSG_WIN) wins++; else if (msg == MSG_LOSE) losses++; else draws++;
        Scores.Report(wins, wins, losses, draws);
        byte[] sv = new byte[12]; Put32(sv, 0, wins); Put32(sv, 4, losses); Put32(sv, 8, draws); Storage.Save(sv);
      }
    }
  }

  void Step(int inp) {
    t++;
    if (msgT > 0) msgT--;
    if (animOn && ++animT > 6) animOn = false;
    if (over > 0) { if (--over == 0) NewGame((uint)Math.Floor(Rnd() * 1e9)); return; }
    if (animOn) return;
    if (demo || turn == 2) {                     // the CPU's sides: [2], or [1, 2] in the demo
      if (++think < 18) return; think = 0;        // a beat between moves
      if (!planOn || planSide != turn) {
        int k = BestTurn(turn);
        planOn = true; planSide = turn; planPos = 0; planLen = k < 0 ? 0 : rootLen[k];
        for (int j = 0; j < planLen; j++) { planFrom[j] = rootFrom[k * 16 + j]; planTo[j] = rootTo[k * 16 + j]; planCap[j] = rootCap[k * 16 + j]; }
      }
      if (planPos < planLen) { int j = planPos++; Play(planFrom[j], planTo[j], planCap[j]); }
      if (planPos >= planLen) planOn = false;
      return;
    }
    // you: cursor, pick up, put down (each direction starts from the old cursor, as the JS does)
    int r = curR, c = curC;
    if ((inp & Buttons.Up) != 0) { curR = Math.Max(0, r - 1); curC = c; }
    if ((inp & Buttons.Down) != 0) { curR = Math.Min(7, r + 1); curC = c; }
    if ((inp & Buttons.Left) != 0) { curR = r; curC = Math.Max(0, c - 1); }
    if ((inp & Buttons.Right) != 0) { curR = r; curC = Math.Min(7, c + 1); }
    int i = curR * 8 + curC;
    if ((inp & Buttons.B) != 0 && chain < 0) sel = -1;
    if ((inp & Buttons.A) != 0) {
      Moves(b, 1, chain);
      if (sel >= 0) {
        int f = -1;
        for (int k = 0; k < nl; k++) if (lFrom[k] == sel && lTo[k] == i) { f = k; break; }
        if (f >= 0) Play(lFrom[f], lTo[f], lCap[f]);
        else if (chain < 0 && OWN[b[i]] == 1 && LegalFrom(i)) sel = i;
      } else if (OWN[b[i]] == 1 && LegalFrom(i)) sel = i;
      else if (OWN[b[i]] == 1 && nl > 0 && lCap[0] >= 0) { msg = MSG_JUMP; msgT = 40; }
    }
  }
  bool LegalFrom(int i) { for (int k = 0; k < nl; k++) if (lFrom[k] == i) return true; return false; }

  // ---- drawing
  static int Round(double v) { return (int)Math.Floor(v + 0.5); }    // JS Math.round
  // 5x5 inside a 7x7 square at (x, y); ink false = dark on a lit square
  static void Piece(Screen s, double x, double y, int v, bool ink) {
    bool mine = OWN[v] == 1, king = v >= 3;
    for (int j = 0; j < 5; j++) for (int i = 0; i < 5; i++) {
      int d = (i - 2) * (i - 2) + (j - 2) * (j - 2); if (d > 5) continue;
      bool ring = d >= 3;
      bool on = mine ? true : ring;
      if (king && i == 2 && j == 2) on = !mine;             // crown mark: a hole in yours, a dot in theirs
      if (king && d == 1 && j == 2) on = !mine;
      s.Set(Round(x + 1 + i), Round(y + 1 + j), on ? ink : !ink);
    }
  }
  // the same at whole pixels, as runs for Fill: per value RUN0[v]..RUN0[v + 1] in RUNS, 4 ints each (x, y, w, on)
  static int[] RUNS, RUN0;
  static void MakeRuns() {
    int[] pix = new int[25]; RUNS = new int[4 * 4 * 25]; RUN0 = new int[6]; int n = 0;
    for (int pv = 0; pv < 5; pv++) {
      RUN0[pv] = n;
      if (pv == 0) continue;
      bool mine = OWN[pv] == 1, king = pv >= 3;
      for (int j = 0; j < 5; j++) for (int i = 0; i < 5; i++) {
        int d = (i - 2) * (i - 2) + (j - 2) * (j - 2); pix[j * 5 + i] = 0; if (d > 5) continue;
        bool on = mine ? true : d >= 3;
        if (king && i == 2 && j == 2) on = !mine;
        if (king && d == 1 && j == 2) on = !mine;
        pix[j * 5 + i] = on ? 1 : 2;
      }
      for (int j = 0; j < 5; j++) for (int i = 0; i < 5; ) {
        int p = pix[j * 5 + i], e = i + 1;
        while (e < 5 && pix[j * 5 + e] == p) e++;
        if (p != 0) { RUNS[n] = i; RUNS[n + 1] = j; RUNS[n + 2] = e - i; RUNS[n + 3] = p; n += 4; }
        i = e;
      }
    }
    RUN0[5] = n;
  }
  static void PieceI(Screen s, int x, int y, int v, bool ink) {
    if (RUNS == null) MakeRuns();
    int e = RUN0[v + 1];
    for (int k = RUN0[v]; k < e; k += 4) s.Fill(x + 1 + RUNS[k], y + 1 + RUNS[k + 1], RUNS[k + 2], 1, (RUNS[k + 3] == 1) == ink);
  }
  static readonly int[] BRACKET = { 0, 0, 1, 0, 0, 1, 6, 0, 5, 0, 6, 1, 0, 6, 1, 6, 0, 5, 6, 6, 5, 6, 6, 5 };

  public override void Draw(Screen s) {
    s.Clear();
    for (int r = 0; r < 8; r++) for (int c = 0; c < 8; c++)
      if ((r + c) % 2 == 1) s.Fill(BX + c * SQ, BY + r * SQ, SQ, SQ, true);       // playable squares solid
    for (int q = 0; q < 32; q++) { int i = DARK[q];
      int v = b[i]; if (v == 0) continue;
      if (animOn && i == animTo) continue;
      if (sel == i && ((t >> 3) & 1) != 0) continue;         // selected: blinks
      PieceI(s, BX + (i & 7) * SQ, BY + (i >> 3) * SQ, v, false);
    }
    if (animOn) {
      double k = animT / 6.0; int f = animFrom, to = animTo;
      int v = b[to] != 0 ? b[to] : animV;
      Piece(s, BX + ((f & 7) + ((to & 7) - (f & 7)) * k) * SQ, BY + ((f >> 3) + ((to >> 3) - (f >> 3)) * k) * SQ, v, false);
    }
    if (turn == 1 && over == 0) {                // cursor: corner brackets that flip what they cover
      int x = BX + curC * SQ, y = BY + curR * SQ;
      for (int q = 0; q < BRACKET.Length; q += 2) { int px = x + BRACKET[q], py = y + BRACKET[q + 1]; s.Set(px, py, !s.Get(px, py)); }
    }
    // right strip: whose turn (a piece), pieces taken
    s.Fill(57, 0, 1, 64, true);
    PieceI(s, 57, 2, turn == 1 ? 1 : 2, true);
    PieceI(s, 57, 22, 2, true); s.Text(59, 30, DIG[lost[1] / 10 % 10], true); s.Text(59, 36, DIG[lost[1] % 10], true);   // CPU pieces you took
    PieceI(s, 57, 44, 1, true); s.Text(59, 52, DIG[lost[0] / 10 % 10], true); s.Text(59, 58, DIG[lost[0] % 10], true);   // yours they took
    if (msgT > 0) {
      int w = MSG[msg].Length * 4 + 3, x = Round((56 - w) / 2.0);
      s.Fill(x, 26, w, 11, true); s.Fill(x + 1, 27, w - 2, 9, false);   // the JS box: a lit border, dark inside
      s.Text(x + 2, 29, MSG_SHOWN[msg], true);
    }
  }
}
