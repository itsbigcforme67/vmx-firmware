// BLOBS for the VMX: a falling-pairs, match-four game (working name). A line-for-line port of
// microgames/ui/games/blobs_core.js to C# 1.0, so it plays exactly the same (same random numbers, same doubles);
// tools/parity checks that frame by frame.
// Left/right move the pair, A turns it left, B or Up turns it right, Down held drops it faster.
// 4+ touching blobs of a kind pop; the rest fall and may pop again as a chain.
using System;
using Microsoft.Vmx;

public class Blobs : Game {
  const int COLS = 6, ROWS = 12, KINDS = 4, CELL = 5, N = COLS * ROWS;
  const int BX = 2, BY = 3;                      // board's top-left on screen
  const double FALL = 0.06, SOFT = 0.5;          // rows per tick
  const int LOCK = 15, POP_T = 24;
  // 5x5 patterns, one per kind, a row per int (bit 4 = left)
  static readonly int[][] BLOB = {
    new int[] { 14, 31, 31, 31, 14 },            // solid
    new int[] { 14, 17, 17, 17, 14 },            // ring
    new int[] { 10, 21, 10, 21, 10 },            // checker
    new int[] { 14, 21, 31, 17, 14 },            // face
  };
  static readonly int[] OFFX = { 0, 1, 0, -1 }, OFFY = { -1, 0, 1, 0 };   // child offset by rotation: up, right, down, left

  uint rs;                                       // the JS core's rng(): an LCG, divided down to [0, 1)
  double Rnd() { rs = rs * 1664525u + 1013904223u; return rs / 4294967296.0; }

  int[] board = new int[N];                      // board[y * COLS + x], -1 = empty
  int t, score, chain, best, over, banner;
  string banner1, banner2;                       // bannerText split into its words (a second word or null)
  const int SPAWN = 0, FALLING = 1, CHECK = 2, POP = 3;
  int phase;
  bool hasPair; int pairX, pairRot, pairK0, pairK1, pairLock, pairId; double pairY;
  int next0, next1;
  bool popOn; int popT; bool[] popHit = new bool[N];
  bool demo; int top;                            // top: the best score reported so far

  public override bool UsesB { get { return true; } }

  public override void Start(int seed, bool demo) {
    this.demo = demo;
    NewGame((uint)seed);
  }
  void NewGame(uint seed) {
    rs = seed; t = 0;
    for (int y = 0; y < ROWS; y++) for (int x = 0; x < COLS; x++) board[y * COLS + x] = -1;
    score = 0; chain = 0; best = 0; phase = SPAWN; hasPair = false; popOn = false; over = 0; banner = 0;
    banner1 = ""; banner2 = null;
    next0 = (int)Math.Floor(Rnd() * KINDS); next1 = (int)Math.Floor(Rnd() * KINDS);
  }

  bool Free(int x, int y) { return x >= 0 && x < COLS && y < ROWS && (y < 0 || board[y * COLS + x] < 0); }
  bool Fits(int x, double y, int rot) {          // pair (pivot at x, floor(y)) fits; y is fractional
    int dx = OFFX[rot], dy = OFFY[rot], yi = (int)Math.Floor(y), yc = (int)Math.Ceiling(y);
    return Free(x, yi) && Free(x, yc) && Free(x + dx, yi + dy) && Free(x + dx, yc + dy);
  }

  // groups of 4+ touching blobs of a kind: marks their cells in hit (all false before; the caller clears it), returns how many cells. Flood fills only from
  // the cells listed in from[0..nfrom) (or every cell when nfrom < 0): a board with no groups gets new ones only
  // through the cells just dropped onto it, so the demo player checks just those (same result, much less work).
  int[] seen = new int[N]; int stamp;            // seen[c] == stamp: visited in this call
  int[] fstack = new int[N], grp = new int[N], from = new int[2];
  int Groups(int[] bd, bool[] hit, int nfrom) {
    stamp++;
    int total = 0, cnt = nfrom < 0 ? N : nfrom;
    for (int q = 0; q < cnt; q++) {
      int s0 = nfrom < 0 ? q : from[q];
      int k = bd[s0]; if (k < 0 || seen[s0] == stamp) continue;
      int gn = 0, sp = 0; fstack[sp++] = s0; seen[s0] = stamp;
      while (sp > 0) {
        int c = fstack[--sp]; grp[gn++] = c;
        int cx = c % COLS, n;
        n = c + 1; if (cx < COLS - 1 && bd[n] == k && seen[n] != stamp) { seen[n] = stamp; fstack[sp++] = n; }
        n = c - 1; if (cx > 0 && bd[n] == k && seen[n] != stamp) { seen[n] = stamp; fstack[sp++] = n; }
        n = c + COLS; if (n < N && bd[n] == k && seen[n] != stamp) { seen[n] = stamp; fstack[sp++] = n; }
        n = c - COLS; if (n >= 0 && bd[n] == k && seen[n] != stamp) { seen[n] = stamp; fstack[sp++] = n; }
      }
      if (gn >= 4) { total += gn; for (int i = 0; i < gn; i++) hit[grp[i]] = true; }
    }
    return total;
  }

  static void Settle(int[] bd) {                 // blobs fall
    for (int x = 0; x < COLS; x++) {
      int w = N - COLS + x;
      for (int c = w; c >= 0; c -= COLS) if (bd[c] >= 0) { bd[w] = bd[c]; w -= COLS; }
      for (; w >= 0; w -= COLS) bd[w] = -1;
    }
  }

  public override void Tick(int held, int pressed) {
    int w;
    if (demo) w = Demo();
    else {
      w = 0;
      if ((pressed & Buttons.Left) != 0) w |= 1;
      if ((pressed & Buttons.Right) != 0) w |= 2;
      if ((pressed & Buttons.A) != 0) w |= 4;
      if ((pressed & (Buttons.B | Buttons.Up)) != 0) w |= 8;
      if ((held & Buttons.Down) != 0) w |= 16;
    }
    Step((w & 1) != 0, (w & 2) != 0, (w & 4) != 0, (w & 8) != 0, (w & 16) != 0);
    if (score > top) { top = score; Scores.Report(score, 0, 0, 0); }
  }

  void Step(bool left, bool right, bool rotL, bool rotR, bool down) {
    t++;
    if (banner > 0) banner--;
    if (over > 0) { if (--over == 0) NewGame((uint)(int)Math.Floor(Rnd() * 1e9)); return; }
    if (phase == SPAWN) {
      hasPair = true; pairId++; pairX = 2; pairY = -1; pairRot = 0; pairK0 = next0; pairK1 = next1; pairLock = 0;
      next0 = (int)Math.Floor(Rnd() * KINDS); next1 = (int)Math.Floor(Rnd() * KINDS);
      if (!Fits(2, 0, 0)) { over = 90; banner = 90; banner1 = "GAME"; banner2 = "OVER"; return; }
      phase = FALLING; chain = 0;
    }
    if (phase == FALLING) {
      if (left && Fits(pairX - 1, pairY, pairRot)) pairX--;
      if (right && Fits(pairX + 1, pairY, pairRot)) pairX++;
      for (int pass = 0; pass < 2; pass++) {
        if (!(pass == 0 ? rotR : rotL)) continue;
        int r = (pairRot + (pass == 0 ? 1 : 3)) % 4;
        if (Fits(pairX, pairY, r)) pairRot = r;
        else if (Fits(pairX - OFFX[r], pairY, r)) { pairX -= OFFX[r]; pairRot = r; }        // kick off the wall
        else if (OFFY[r] == 1 && Fits(pairX, pairY - 1, r)) { pairY -= 1; pairRot = r; }     // into the floor: lift
      }
      double dy = down ? SOFT : FALL;
      if (Fits(pairX, pairY + dy, pairRot)) {
        if (down && Math.Floor(pairY + dy) > Math.Floor(pairY)) score += 1;
        pairY += dy; pairLock = 0;
      } else {
        pairY = Math.Floor(pairY + dy - 1e-9);
        while (!Fits(pairX, pairY, pairRot)) pairY--;
        if (++pairLock >= (down ? 2 : LOCK)) {      // lock: place both, they settle on their own
          int y = (int)Math.Floor(pairY + 0.5), ddy = OFFY[pairRot];
          if (y >= 0) board[y * COLS + pairX] = pairK0;
          if (y + ddy >= 0) board[(y + ddy) * COLS + pairX + OFFX[pairRot]] = pairK1;
          hasPair = false; Settle(board); phase = CHECK;
        }
      }
    }
    if (phase == CHECK) {
      int n = Groups(board, popHit, -1);
      if (n == 0) { phase = SPAWN; return; }
      chain++;
      score += n * 10 * (1 << (chain - 1));      // 10 a blob, doubled every chain step
      best = Math.Max(best, chain);
      if (chain >= 2) { banner = 40; banner1 = chain.ToString(); banner2 = "CHAIN!"; }
      popOn = true; popT = POP_T;
      phase = POP;
    }
    if (phase == POP && --popT <= 0) {
      for (int c = 0; c < N; c++) if (popHit[c]) { board[c] = -1; popHit[c] = false; }
      popOn = false; Settle(board); phase = CHECK;
      bool empty = true;
      for (int y = 0; y < ROWS; y++) for (int x = 0; x < COLS; x++) if (board[y * COLS + x] >= 0) empty = false;
      if (empty) { score += 2100; banner = 60; banner1 = "ALL"; banner2 = "CLEAR!"; }
    }
  }

  // ---- drawing
  static void Blob(Screen s, int x, int y, int k) {
    int[] p = BLOB[k];
    for (int j = 0; j < 5; j++) for (int i = 0; i < 5; i++) if ((p[j] & (16 >> i)) != 0) s.Set(x + i, y + j, true);
  }

  public override void Draw(Screen s) {
    s.Clear();
    for (int y = BY - 1; y <= BY + ROWS * CELL; y++) { s.Set(BX - 1, y, true); s.Set(BX + COLS * CELL, y, true); }   // well
    for (int x = BX - 1; x <= BX + COLS * CELL; x++) s.Set(x, BY + ROWS * CELL, true);
    for (int y = 0; y < ROWS; y++) for (int x = 0; x < COLS; x++) {
      int k = board[y * COLS + x]; if (k < 0) continue;
      if (popOn && popHit[y * COLS + x] && ((popT >> 2) % 2) != 0) continue;                       // pop blink
      Blob(s, BX + x * CELL, BY + y * CELL, k);
    }
    if (hasPair) {
      int dx = OFFX[pairRot], dy = OFFY[pairRot], sy = (int)Math.Floor(pairY * CELL + 0.5);
      if (pairY >= 0) Blob(s, BX + pairX * CELL, BY + sy, pairK0);
      if (pairY + dy >= 0) Blob(s, BX + (pairX + dx) * CELL, BY + sy + dy * CELL, pairK1);
    }
    // side panel: NEXT pair, score, best chain
    s.Text(36, 2, "NEXT", true);
    for (int y = 8; y <= 20; y++) { s.Set(38, y, true); s.Set(46, y, true); }
    for (int x = 38; x <= 46; x++) { s.Set(x, 8, true); s.Set(x, 20, true); }
    Blob(s, 40, 9, next1); Blob(s, 40, 14, next0);
    s.Text(36, 26, "SCORE", true);
    string sc = (score % 1000000).ToString().PadLeft(6, '0');
    s.Text(36, 33, sc.Substring(0, 3), true);
    s.Text(48, 33, sc.Substring(3, 3), true);
    if (banner > 0) {
      s.Text(36, 44, banner1, true);
      if (banner2 != null) s.Text(36, 51, banner2, true);
    } else if (best >= 2) { s.Text(36, 44, "BEST", true); s.Text(36, 51, best.ToString() + " CH", true); }
  }

  // ---- the demo player (as the JS core's demo()/plan()): bits 1 left, 2 right, 4 turn left, 8 turn right, 16 down.
  // Tries every column and turn, simulates the drop and the chain it sets off, looks one pair ahead.
  int goalPair = -1, goalX, goalRot;
  int[] bdA = new int[N], bdB = new int[N]; bool[] phit = new bool[N];
  int pChain, pPopped;                           // the last Place()'s chain and popped count

  // drops k into column cx of bd; the cell it lands in, or -1 if the column is full
  static int Drop(int[] bd, int cx, int k) {
    int c = cx - COLS; while (c + COLS < N && bd[c + COLS] < 0) c += COLS;
    if (c < 0) return -1;
    bd[c] = k; return c;
  }
  // one placement (a column and turn) of the pair k0/k1 dropped onto src (a board with no groups), into dst;
  // false if it doesn't fit
  bool Place(int[] src, int k0, int k1, int rot, int x, int[] dst) {
    int dx = OFFX[rot], dy = OFFY[rot]; if (x + dx < 0 || x + dx >= COLS) return false;
    Array.Copy(src, 0, dst, 0, N);
    if (dy == 1) { if ((from[0] = Drop(dst, x + dx, k1)) < 0 || (from[1] = Drop(dst, x, k0)) < 0) return false; }
    else if ((from[0] = Drop(dst, x, k0)) < 0 || (from[1] = Drop(dst, x + dx, k1)) < 0) return false;
    pChain = 0; pPopped = 0;
    int n = Groups(dst, phit, 2);
    while (n > 0) {
      pChain++;
      for (int i = 0; i < N; i++) if (phit[i]) { dst[i] = -1; phit[i] = false; }
      pPopped += n; Settle(dst);
      n = Groups(dst, phit, -1);
    }
    return true;
  }

  static double Value(int[] bd) {                // how good a board is to build on
    int adj = 0, height = 0, tall = 0;
    for (int cx = 0; cx < COLS; cx++) {
      int c = cx; while (c < N && bd[c] < 0) c += COLS;       // the column's top blob
      int h = ROWS - c / COLS; if (h > tall) tall = h;
      height += h * (h + 1) / 2;                 // blobs are settled: none below the top is empty
      if (h == 0) continue;
      int k = bd[c];
      if (cx + 1 < COLS) {
        for (; c < N - COLS; c += COLS) { int k2 = bd[c + COLS]; if (bd[c + 1] == k) adj++; if (k2 == k) adj++; k = k2; }
        if (bd[c + 1] == k) adj++;
      } else {
        for (; c < N - COLS; c += COLS) { int k2 = bd[c + COLS]; if (k2 == k) adj++; k = k2; }
      }
    }
    int danger = (bd[COLS + 2] >= 0 || bd[COLS + 3] >= 0) ? 5000 : 0;
    return adj * 8 - height * 0.5 - Math.Max(0, tall - 8) * 30 - danger;
  }

  static double Worth(int chain, int popped, int stackH) {   // a move's own reward
    bool early = stackH < 24 && chain == 1;
    return chain * chain * chain * 80 + (early ? -60 : popped * 4);
  }

  void Plan() {
    int stackH = 0;
    for (int y = 0; y < ROWS; y++) for (int x = 0; x < COLS; x++) if (board[y * COLS + x] >= 0) stackH++;
    bool found = false; double bestScore = 0;
    goalX = pairX; goalRot = pairRot;
    for (int rot = 0; rot < 4; rot++) for (int x = 0; x < COLS; x++) {
      if (!Place(board, pairK0, pairK1, rot, x, bdA)) continue;
      int c1 = pChain, p1 = pPopped;
      double follow = -1e9;
      for (int rot2 = 0; rot2 < 4; rot2++) for (int x2 = 0; x2 < COLS; x2++) {
        if (!Place(bdA, next0, next1, rot2, x2, bdB)) continue;
        follow = Math.Max(follow, Worth(pChain, pPopped, stackH) * 0.8 + Value(bdB));
      }
      double sc = Worth(c1, p1, stackH) + (follow > -1e9 ? follow : Value(bdA) - 3000);
      if (!found || sc > bestScore) { found = true; bestScore = sc; goalX = x; goalRot = rot; }
    }
  }

  int Demo() {
    if (!hasPair) return 0;
    if (goalPair != pairId) { goalPair = pairId; Plan(); }
    if (t % 3 != 0) return 0;                    // a human-ish 10 inputs a second
    if (pairRot != goalRot) return 8;
    if (pairX < goalX) return 2;
    if (pairX > goalX) return 1;
    return 16;
  }
}
