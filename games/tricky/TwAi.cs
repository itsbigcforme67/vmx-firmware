// TRICKY WICKS CPU player: a line-for-line C# port of the owner's port/core/tw_ai.c (the demo uses it, as the web
// simulator does through tricky_core.js aiInit / ai). Its state lives for the whole session (static in the C):
// only Init resets the parts tw_ai_init does.
public class TwAi {
  const int COLS = TwCore.COLS, ROWS = TwCore.ROWS, CELLS = TwCore.CELLS, NOWHERE = 0xFF;
  const int PER_FRAME = 2, THINK = 55, STEP = 12, FUZZ = 24;
  static readonly int[] DIR_BIT = { 1, 2, 4, 8 };
  static readonly int[] DIR_DR = { -1, 0, 1, 0 };
  static readonly int[] DIR_DC = { 0, 1, 0, -1 };
  static readonly int[] DIR_OPP = { 4, 8, 1, 2 };

  TwCore G;
  int rng;
  int has_cur, cur_serial;
  int[] turns = new int[4]; int n_turns;
  int c_col, c_turn, weighed;
  int has_best, best_col, best_t;
  int best_v;
  int wait, pace, stuck;
  int last_col;
  int[] col_top = new int[COLS];
  int tallest;
  int[] fires = new int[CELLS]; int n_fires;
  int p_any, p_ropes, p_bombs, p_cannons;
  int[] seen = new int[CELLS], stack = new int[CELLS];

  public TwAi(TwCore g) { G = g; }

  static bool IS_FIRE(int k) { return k == TwCore.FIRE || k == TwCore.BIGFIRE; }
  static bool IS_BOMB(int k) { return k == TwCore.BOMB || k == TwCore.CANNON; }

  int fuzz(int n) {
    rng = (rng ^ (rng << 7)) & 0xFFFF;
    rng = (rng ^ (rng >> 9)) & 0xFFFF;
    rng = (rng ^ (rng << 8)) & 0xFFFF;
    return (rng % n) & 0xFF;
  }
  int at(int r, int c) {
    if (r < 0 || r >= ROWS || c < 0 || c >= COLS) return NOWHERE;
    int i = r * COLS + c;
    return G.kind[i] != 0 ? i : NOWHERE;
  }
  bool would_light(int i) {
    int d, n, k, r = 0, c = i; bool big = G.kind[i] == TwCore.BIGFIRE;
    while (c >= COLS) { c -= COLS; r++; }
    for (d = 0; d < 4; d++) {
      n = at(r + DIR_DR[d], c + DIR_DC[d]);
      if (n == NOWHERE) continue;
      k = G.kind[n];
      if ((k == TwCore.ROPE && (big || (TwCore.ENDS[G.t[n]] & DIR_OPP[d]) != 0)) || IS_BOMB(k)) return true;
    }
    return false;
  }
  void preview(int placed) {
    int i, n, d, sp = 0, ends, k, f, r, c; bool big;
    p_any = 0; p_ropes = 0; p_bombs = 0; p_cannons = 0;
    if (n_fires == 0 && !IS_FIRE(G.kind[placed])) return;
    for (f = 0; f <= n_fires; f++) {
      if (f == n_fires) { if (!IS_FIRE(G.kind[placed])) break; i = placed; } else i = fires[f];
      if (!would_light(i)) continue;
      if (sp == 0) for (n = 0; n < CELLS; n++) seen[n] = 0;
      stack[sp++] = i; seen[i] = 1;
    }
    if (sp == 0) return;
    p_any = 1;
    while (sp != 0) {
      i = stack[--sp];
      r = 0; n = i; while (n >= COLS) { n -= COLS; r++; }
      c = n;
      big = G.kind[i] == TwCore.BIGFIRE; ends = IS_FIRE(G.kind[i]) ? 15 : TwCore.ENDS[G.t[i]];
      for (d = 0; d < 4; d++) {
        if ((ends & DIR_BIT[d]) == 0) continue;
        n = at(r + DIR_DR[d], c + DIR_DC[d]);
        if (n == NOWHERE || seen[n] != 0) continue;
        k = G.kind[n];
        if (k == TwCore.ROPE && (big || (TwCore.ENDS[G.t[n]] & DIR_OPP[d]) != 0)) { seen[n] = 1; p_ropes++; stack[sp++] = n; }
        else if (IS_BOMB(k)) { seen[n] = 1; if (k == TwCore.CANNON) p_cannons++; else p_bombs++; }
      }
    }
  }
  static int S16(int v) { return (short)v; }       // C's s16 assignment (wraps)
  int value(int col, int t) {
    int k = G.p_kind, i, n, d, tall = tallest, nk, row, rr, cc, v; bool facing, inside;
    row = col_top[col] - 1;
    if (row < 0) return -9999;
    i = row * COLS + col;
    G.kind[i] = k; G.t[i] = k == TwCore.ROPE ? t : 0;
    v = S16(fuzz(FUZZ) - (ROWS - 1 - row) * 6);
    if (col == TwCore.SPAWN_COL && row < 3) v = S16(v - (3 - row) * 150);
    preview(i);
    if (p_any != 0) {
      if (p_bombs + p_cannons != 0) v = S16(v + 30 + 45 * p_ropes * (p_bombs + p_cannons) + (p_cannons != 0 ? 80 + (G.power >> 2) : 0));
      else v = S16(v + p_ropes * 10 - 50 + (tall > 4 ? (tall - 4) * 25 : 0));
    } else {
      for (d = 0; d < 4; d++) {
        rr = row + DIR_DR[d]; cc = col + DIR_DC[d];
        inside = rr >= 0 && rr < ROWS && cc >= 0 && cc < COLS;
        n = inside ? at(rr, cc) : NOWHERE;
        nk = n == NOWHERE ? TwCore.NONE : G.kind[n];
        facing = nk == TwCore.ROPE && (TwCore.ENDS[G.t[n]] & DIR_OPP[d]) != 0;
        if (k == TwCore.ROPE) {
          if ((TwCore.ENDS[t] & DIR_BIT[d]) != 0) {
            if (!inside) v = S16(v - 14);
            else if (nk == 0) v = S16(v + 3);
            else if (nk == TwCore.ROPE) v = S16(v + (facing ? 16 : -10));
            else if (IS_BOMB(nk)) v = S16(v + 18);
            else if (nk == TwCore.BLOCK) v = S16(v - 10);
          } else if (facing) v = S16(v - 8);
        } else if (IS_BOMB(k)) { if (facing) v = S16(v + 20); }
        else if (k == TwCore.BLOCK) { if (facing) v = S16(v - 12); }
      }
      if (k == TwCore.BLOCK && (col == 0 || col == COLS - 1)) v = S16(v + 6);
    }
    G.kind[i] = TwCore.NONE; G.t[i] = 0;
    return v;
  }
  bool reachable(int col, int n_turn) {
    int away = col > G.p_col ? col - G.p_col : G.p_col - col, from, to, c, low, r;
    uint fall = ((uint)(((away + n_turn) & 0xFFFF) * (pace + 1) + wait) * (uint)G.speed()) >> 12;
    low = S16((((G.p_y + 15) & 0xFF) >> 4) - 1 + S16((int)fall) + 1);
    from = col < G.p_col ? col : G.p_col; to = col > G.p_col ? col : G.p_col;
    for (c = from; c <= to; c++) {
      if (c == G.p_col) continue;
      for (r = 0; r <= low && r < ROWS; r++) if (G.kind[r * COLS + c] != 0) return false;
    }
    return true;
  }

  public void Init(int seed) {
    rng = (seed * 31 + 7) & 0xFFFF;
    if (rng == 0) rng = 1;
    has_cur = 0; wait = 0; pace = STEP;
  }
  public int Think() {
    int i, t, col, v;
    if (G.phase != TwCore.FALL || G.p_kind == 0 || G.over != 0) { has_cur = 0; return 0; }
    if (has_cur == 0 || cur_serial != G.p_serial) {
      has_cur = 1; cur_serial = G.p_serial;
      c_col = 0; c_turn = 0; weighed = 0; has_best = 0; last_col = -1; stuck = 0;
      wait = (THINK > 20 + 2 * (G.level - 1) ? THINK - 2 * (G.level - 1) : 20) & 0xFF;
      pace = (STEP > 8 + (G.level >> 3) ? STEP - (G.level >> 3) : 8) & 0xFF;
      tallest = 0; n_fires = 0;
      for (col = 0; col < COLS; col++) {
        t = 0; while (t < ROWS && G.kind[t * COLS + col] == 0) t++;
        col_top[col] = t; if (ROWS - t > tallest) tallest = ROWS - t;
      }
      for (i = 0; i < CELLS; i++) if (IS_FIRE(G.kind[i])) fires[n_fires++] = i;
      n_turns = 0;
      if (G.p_kind == TwCore.ROPE) { t = G.p_t; do { turns[n_turns++] = t; t = TwCore.ROT_CW[t]; } while (t != G.p_t); }
      else turns[n_turns++] = 0;
    }
    if (weighed == 0) {
      for (i = 0; i < PER_FRAME && weighed == 0; i++) {
        col = c_col; t = c_turn;
        if (++c_turn == n_turns) { c_turn = 0; if (++c_col == COLS) weighed = 1; }
        if (col != G.p_col && !reachable(col, t)) continue;
        v = value(col, turns[t]);
        if (has_best == 0 || v > best_v) { has_best = 1; best_col = col; best_t = turns[t]; best_v = v; }
      }
      return 0;
    }
    if (wait > 0) { wait--; return 0; }
    wait = pace;
    if (G.p_kind == TwCore.ROPE && G.p_t != best_t) return TwCore.A;
    if (G.p_col != best_col) {
      if (G.p_col == last_col && (stuck = (stuck + 1) & 0xFF) >= 2) return TwCore.UP;
      last_col = G.p_col;
      return G.p_col < best_col ? TwCore.RIGHT : TwCore.LEFT;
    }
    return TwCore.UP;
  }
}
