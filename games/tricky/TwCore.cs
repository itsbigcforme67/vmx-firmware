// TRICKY WICKS rules: a line-for-line C# port of the owner's C core (TRICKY WICKS/v1.5 zip, port/core/tw_core.c),
// which microgames/ui/games/tricky_rules.js is the compiled form of. C's u8 / u16 / s8 / s16 are kept in ints;
// every place where the C type would wrap or truncate is masked the same way, so a seed deals the same game.
// Only one player's side (no versus): the cannon / meter / incoming code is kept as in the C for fidelity.
public class TwCore {
  public const int COLS = 6, ROWS = 8, CELLS = 48, TS = 16, FX = 8, FY = 8, SPAWN_COL = 2;
  public const int NONE = 0, ROPE = 1, BOMB = 2, CANNON = 3, FIRE = 4, BIGFIRE = 5, BLOCK = 6, OFF = 0xFF;
  public const int LEFT = 1, RIGHT = 2, UP = 4, DOWN = 8, A = 16, B = 32;
  public const int FALL = 0, RESOLVE = 1, SETTLE = 2, BURN = 3;
  const int SPREAD = 9, BURN_END = 22, BOOM_AT = 14, BOOM_END = 34, LOCK = 20, BLOCK_FADE = 18;
  const int BLOCK_FIRST = 6, BLOCK_GAP = 12, BLOCK_ODDS = 16, BIG_FIRST = 8, BIG_GAP = 12, BIG_ODDS = 12;
  const int UNIT = 200, MAX_SEND = 12, METER_MAX = 2400, CANNON_BONUS = 2, DRAIN_EVERY = 3;
  const int MAX_BOOMS = 12, MAX_POPS = 12, BOOM_LIFE = 20, POP_LIFE = 60;
  const int POP_X = 0, POP_BOMBS = 1, POP_CHAIN = 2, POP_SENT = 3, POP_BLOCKS = 4;
  const int MAX_SFX = 8;
  const int SFX_MOVE = 1, SFX_TURN = 2, SFX_LOCK = 3, SFX_LEVEL = 4, SFX_SETTLE = 5, SFX_IGNITE = 6, SFX_CATCH = 7,
    SFX_BOOM = 8, SFX_CHAIN = 9, SFX_SENT = 10, SFX_INCOMING = 11;
  public const int NOWHERE = 0xFF;
  const int U = 1, R = 2, D = 4, L = 8;

  public static readonly int[] ENDS = { L|R, U|D, D|R, L|D, U|L, U|R, L|R|D, U|L|D, U|L|R, U|D|R, 15, L|R|D, U|L|D, U|L|R, U|D|R, 15 };
  public static readonly int[] ROT_CW = { 1,0,3,4,5,2,7,8,9,6,15,12,13,14,11,10 };
  static readonly int[] CHAIN = { 1, 4, 8, 16 };
  static readonly int[] POL = {
    0,2,0,1,  1,0,2,0,  0,2,1,0,  0,0,2,1,  1,0,0,2,  2,1,0,0,  0,2,2,1,  1,0,2,2,
    2,1,0,2,  2,2,1,0,  1,2,2,1,  0,2,1,1,  1,0,2,1,  1,1,0,2,  2,1,1,0,  2,2,1,1
  };
  static readonly int[] ROT_CCW = { 1,0,5,2,3,4,9,6,7,8,15,14,11,12,13,10 };
  static readonly int[] ALT = { 0xFF,0xFF,0xFF,0xFF,0xFF,0xFF, 11,12,13,14,15, 6,7,8,9,10 };
  static readonly int[] TOG = { 0,0,0,0,0,0, D,L,U,R,U|D, D,L,U,R,U|D };
  public static readonly int[] DIR_BIT = { U, R, D, L };
  public static readonly int[] DIR_DR = { -1, 0, 1, 0 };
  public static readonly int[] DIR_DC = { 0, 1, 0, -1 };
  public static readonly int[] DIR_OPP = { D, L, U, R };
  static readonly int[] DIR_OI = { 2, 3, 0, 1 };
  static readonly int[] BLAST_DR = { -1,-1,-1, 0, 0, 1, 1, 1 };
  static readonly int[] BLAST_DC = { -1, 0, 1,-1, 1,-1, 0, 1 };
  static readonly int[] BAG = { 'S','S','S','S','C','C','C','T','T','X','B','B','F','F','F','K' };

  // ---- tw_game
  public int[] kind = new int[CELLS], t = new int[CELLS], tm = new int[CELLS], off = new int[CELLS];
  public int[] mult = new int[CELLS], nth = new int[CELLS], chn = new int[CELLS];
  public int p_kind, p_t, p_col, p_y, p_serial, n_kind, n_t;
  public int rng; int[] bag = new int[16]; int bag_n; int drawn, since_block, since_big;
  public int score; public int placed; public int level, to_level, wick, best_wick;
  public int phase, over;
  int lock_t, soft_ok, das, burn_idle, chain, wave_bombs; public int shake;
  int das_dir; int fall_acc;
  int vs; public int power; uint turn_gain; int cannon_turn, shot_x, shot_y, outgoing, incoming, drop_col, tick;
  public int n_booms; public int[] boom_r = new int[MAX_BOOMS], boom_c = new int[MAX_BOOMS], boom_t = new int[MAX_BOOMS];
  public int n_pops; public int[] pop_x = new int[MAX_POPS], pop_y = new int[MAX_POPS], pop_t = new int[MAX_POPS], pop_type = new int[MAX_POPS];
  public int[] pop_a = new int[MAX_POPS], pop_b = new int[MAX_POPS];
  int n_sfx; int[] sfx_id = new int[MAX_SFX], sfx_arg = new int[MAX_SFX];
  int fx_lost, retiled;
  int n_active; int[] active = new int[CELLS];
  int[] snap = new int[CELLS];                  // step_burn's static
  int[] stacked = new int[COLS];                // drop_incoming's local

  static bool IS_FIRE(int k) { return k == FIRE || k == BIGFIRE; }
  static bool IS_BOMB(int k) { return k == BOMB || k == CANNON; }

  // ---------- small helpers ----------
  public int cell(int r, int c) {
    if (r < 0 || r >= ROWS || c < 0 || c >= COLS) return NOWHERE;
    int i = r * COLS + c;
    return kind[i] != 0 ? i : NOWHERE;
  }
  void clear_cell(int i) { kind[i] = NONE; t[i] = 0; tm[i] = OFF; off[i] = 0; mult[i] = 0; nth[i] = 0; chn[i] = 0; }
  void sfx(int id, int arg) {
    if (n_sfx < MAX_SFX) { sfx_id[n_sfx] = id; sfx_arg[n_sfx] = arg; n_sfx++; }
  }
  void add_boom(int r, int c) {
    if (n_booms == MAX_BOOMS) { fx_lost = (fx_lost + 1) & 0xFF; return; }
    boom_r[n_booms] = r; boom_c[n_booms] = c; boom_t[n_booms] = 0; n_booms++;
  }
  void add_pop(int x, int y, int type, int a, int b) {
    int n = n_pops;
    if (n == MAX_POPS) { fx_lost = (fx_lost + 1) & 0xFF; return; }
    pop_x[n] = x & 0xFF; pop_y[n] = y & 0xFF; pop_t[n] = 0; pop_type[n] = type; pop_a[n] = a & 0xFFFF; pop_b[n] = b & 0xFFFF;
    n_pops++;
  }

  void reseed(int v) {
    rng = (int)(((uint)v * 40503u) & 0xFFFF);
    if (rng == 0) rng = 1;
  }
  int rnd16() {
    rng = (rng ^ (rng << 7)) & 0xFFFF;
    rng = (rng ^ (rng >> 9)) & 0xFFFF;
    rng = (rng ^ (rng << 8)) & 0xFFFF;
    return rng;
  }
  int rnd(int n) {
    int v = rnd16();
    if ((n & (n - 1)) == 0) return v & 0xFF & (n - 1);
    return (v % n) & 0xFF;
  }

  void draw_piece() {
    int i, j, x;
    if (drawn < 255) drawn++;
    if (since_block < 255) since_block++;
    if (since_big < 255) since_big++;
    n_t = 0;
    if (drawn > BLOCK_FIRST && since_block > BLOCK_GAP && rnd(BLOCK_ODDS) == 0) { since_block = 0; n_kind = BLOCK; return; }
    if (drawn > BIG_FIRST && since_big > BIG_GAP && rnd(BIG_ODDS) == 0) { since_big = 0; n_kind = BIGFIRE; return; }
    if (bag_n == 0) {
      bag_n = vs != 0 ? 16 : 15;
      for (i = 0; i < bag_n; i++) bag[i] = BAG[i];
      for (i = bag_n - 1; i > 0; i--) { j = rnd(i + 1); x = bag[i]; bag[i] = bag[j]; bag[j] = x; }
    }
    switch (bag[--bag_n]) {
      case 'S': n_kind = ROPE; n_t = rnd(2); break;
      case 'C': n_kind = ROPE; n_t = 2 + rnd(4); break;
      case 'T': n_kind = ROPE; n_t = 6 + rnd(4); break;
      case 'X': n_kind = ROPE; n_t = 10; break;
      case 'B': n_kind = BOMB; break;
      case 'K': n_kind = CANNON; break;
      default: n_kind = FIRE; break;
    }
  }
  void spawn() {
    if (kind[SPAWN_COL] != 0) { over = 1; p_kind = NONE; return; }
    p_kind = n_kind; p_t = n_t; draw_piece();
    p_col = SPAWN_COL; p_y = 0; p_serial = (p_serial + 1) & 0xFF;
    lock_t = 0; fall_acc = 0; soft_ok = 0; das = 0; das_dir = 0; phase = FALL;
  }
  void gain(int pts) {
    score += pts; if (score > 999999) score = 999999;
    if (vs != 0) {
      uint p = (uint)power + (uint)(cannon_turn != 0 ? pts * CANNON_BONUS : pts);
      power = p > METER_MAX ? METER_MAX : (int)(p & 0xFFFF);
      turn_gain += (uint)pts;
    }
  }
  public bool is_free(int col, int y) {
    int r0, r1;
    y &= 0xFF;
    if (col < 0 || col >= COLS) return false;
    r0 = (y >> 4) - 1;
    r1 = (((y + 15) & 0xFF) >> 4) - 1;
    if (r0 >= ROWS || r1 >= ROWS) return false;
    if (r0 >= 0 && kind[r0 * COLS + col] != 0) return false;
    if (r1 >= 0 && kind[r1 * COLS + col] != 0) return false;
    if (y < TS && kind[col] != 0) return false;
    return true;
  }
  void try_move(int d) {
    if (is_free(p_col + d, p_y)) { p_col = (p_col + d) & 0xFF; sfx(SFX_MOVE, 0); }
  }
  void rotate(int dir) {
    if (p_kind != ROPE) return;
    p_t = dir > 0 ? ROT_CW[p_t] : ROT_CCW[p_t];
    sfx(SFX_TURN, 0);
  }
  public int speed() {
    int s = (77 + 18 * ((level - 1) & 0xFFFF)) & 0xFFFF;
    return s > 512 ? 512 : s;
  }

  void lock_piece() {
    int r = (p_y >> 4) - 1;
    int i;
    if (r < 0) { over = 1; p_kind = NONE; return; }
    i = r * COLS + p_col;
    clear_cell(i);
    kind[i] = p_kind; t[i] = p_kind == ROPE ? p_t : 0;
    p_kind = NONE; placed = (placed + 1) & 0xFFFF; wick = 0; chain = 0; turn_gain = 0; cannon_turn = 0;
    to_level = (to_level + 1) & 0xFF;
    if (to_level == 12) { to_level = 0; if (level < 255) level++; sfx(SFX_LEVEL, 0); }
    else sfx(SFX_LOCK, 0);
    phase = RESOLVE;
  }
  void step_fall(int held, int pressed) {
    int dir;
    bool soft;
    if (vs != 0 && power > 0) {
      tick = (tick + 1) & 0xFF;
      if (tick == DRAIN_EVERY) { tick = 0; power--; }
    }
    if ((pressed & A) != 0) rotate(1);
    if ((pressed & B) != 0) rotate(-1);
    dir = ((held & RIGHT) != 0 ? 1 : 0) - ((held & LEFT) != 0 ? 1 : 0);
    if ((pressed & LEFT) != 0) { try_move(-1); das = 0; das_dir = -1; }
    else if ((pressed & RIGHT) != 0) { try_move(1); das = 0; das_dir = 1; }
    else if (dir != 0 && dir == das_dir) {
      das = (das + 1) & 0xFF;
      if (das >= 12) { if (((das - 12) & 3) == 0) try_move(dir); if (das >= 16) das -= 4; }
    }
    else if (dir == 0) das_dir = 0;
    if ((pressed & DOWN) != 0) soft_ok = 1;
    if ((pressed & UP) != 0) {
      int rb = (((p_y + 15) & 0xFF) >> 4) - 1;
      while (rb + 1 < ROWS && kind[(rb + 1) * COLS + p_col] == 0) rb++;
      p_y = ((rb + 1) << 4) & 0xFF;
      lock_piece();
      return;
    }
    soft = (held & DOWN) != 0 && soft_ok != 0;
    fall_acc = (fall_acc + (soft ? 1024 : speed())) & 0xFFFF;
    while (fall_acc >= 256) {
      fall_acc -= 256;
      if (is_free(p_col, p_y + 1)) { p_y = (p_y + 1) & 0xFF; lock_t = 0; } else { fall_acc = 0; break; }
    }
    if (!is_free(p_col, p_y + 1)) {
      lock_t = (lock_t + 1) & 0xFF;
      if (lock_t >= LOCK || soft) lock_piece();
    }
  }

  // ---------- twist matching (looks only) ----------
  int seam_at(int tt, int r, int c, int d) {
    int n;
    if ((ENDS[tt] & DIR_BIT[d]) == 0) return 0;
    n = cell(r + DIR_DR[d], c + DIR_DC[d]);
    if (n == NOWHERE || kind[n] != ROPE || (ENDS[t[n]] & DIR_OPP[d]) == 0) return 0;
    return POL[tt * 4 + d] == POL[t[n] * 4 + DIR_OI[d]] ? 1 : 0;
  }
  bool swappable(int i) {
    return i != NOWHERE && kind[i] == ROPE && tm[i] == OFF && ALT[t[i]] != NOWHERE;
  }
  bool is_cross(int i) { return swappable(i) && ENDS[t[i]] == 15; }
  int firm_seam(int tt, int r, int c, int d) {
    int n = cell(r + DIR_DR[d], c + DIR_DC[d]);
    return (seam_at(tt, r, c, d) != 0 && !(swappable(n) && (TOG[t[n]] & DIR_OPP[d]) != 0)) ? 1 : 0;
  }
  void retile() {
    int r, c, e;
    int a, b, tt, d, i;
    for (i = 0; i < CELLS; i++) if (kind[i] == ROPE && t[i] >= 6) break;
    if (i == CELLS) return;
    for (c = 0; c < COLS; c++) for (r = 0; r < ROWS; r++) {
      i = r * COLS + c;
      if (kind[i] != ROPE || t[i] < 10) continue;
      if (!is_cross(i)) continue;
      e = r; while (e + 1 < ROWS && is_cross(cell(e + 1, c))) e++;
      a = firm_seam(10, r, c, 0) + firm_seam(10, e, c, 2);
      b = firm_seam(15, r, c, 0) + firm_seam(15, e, c, 2);
      tt = a < b ? 10 : b < a ? 15 : t[r * COLS + c];
      for (; r <= e; r++) { i = r * COLS + c; if (t[i] != tt) { t[i] = tt; retiled = (retiled + 1) & 0xFF; } }
    }
    for (i = 0, r = 0; r < ROWS; r++) for (c = 0; c < COLS; c++, i++) {
      if (kind[i] != ROPE || t[i] < 6) continue;
      if (!swappable(i) || is_cross(i)) continue;
      for (d = 0; d < 4; d++) if ((TOG[t[i]] & DIR_BIT[d]) != 0 && seam_at(t[i], r, c, d) != 0) { t[i] = ALT[t[i]]; retiled = (retiled + 1) & 0xFF; break; }
    }
  }

  // ---------- what happens after a piece lands ----------
  bool gravity() {
    bool moved = false;
    int a, b, c, n;
    for (c = 0; c < COLS; c++) {
      a = b = (ROWS - 1) * COLS + c;
      for (n = 0; n < ROWS; n++, a -= COLS) {
        if (kind[a] == 0) continue;
        if (a != b) {
          kind[b] = kind[a]; t[b] = t[a]; tm[b] = tm[a]; mult[b] = mult[a]; nth[b] = nth[a]; chn[b] = chn[a];
          { int d = b - a, px = 0; while (d != 0) { d -= COLS; px += TS; } off[b] = px & 0xFF; }
          clear_cell(a);
          moved = true;
        }
        b -= COLS;
      }
    }
    return moved;
  }
  bool can_light(int n, int opp, bool big) {
    if (n == NOWHERE || tm[n] != OFF) return false;
    if (kind[n] == ROPE) return big || (ENDS[t[n]] & opp) != 0;
    return IS_BOMB(kind[n]);
  }
  bool ignite() {
    bool any = false, big;
    int i, d, r, c;
    for (i = 0, r = 0; r < ROWS; r++) for (c = 0; c < COLS; c++, i++) {
      if (!IS_FIRE(kind[i]) || tm[i] != OFF) continue;
      big = kind[i] == BIGFIRE;
      for (d = 0; d < 4; d++) if (can_light(cell(r + DIR_DR[d], c + DIR_DC[d]), DIR_OPP[d], big)) { tm[i] = 0; any = true; break; }
    }
    if (any) { burn_idle = 0; wave_bombs = 0; sfx(SFX_IGNITE, 0); }
    return any;
  }
  bool drop_incoming() {
    int n = incoming, landed = 0, tries, c, i, r;
    for (c = 0; c < COLS; c++) stacked[c] = 0;
    incoming = 0;
    for (tries = 0; n > 0 && tries < COLS * ROWS; tries++) {
      c = drop_col; drop_col = (drop_col + 1) % COLS;
      r = ROWS - 1; while (r >= 0 && kind[r * COLS + c] != 0) r--;
      if (r < 0) continue;
      i = r * COLS + c;
      clear_cell(i);
      kind[i] = BLOCK; off[i] = ((r + 2 + stacked[c]) * TS) & 0xFF; stacked[c]++;
      n--; landed++;
    }
    if (landed == 0) return false;
    add_pop(FX + 48, FY + 24, POP_BLOCKS, landed, 0);
    sfx(SFX_INCOMING, 0); shake = 6;
    phase = SETTLE;
    return true;
  }
  void resolve() {
    int n;
    if (gravity()) { phase = SETTLE; return; }
    retile();
    if (ignite()) { phase = BURN; return; }
    if (cannon_turn != 0) {
      n = 0; while (n < MAX_SEND && power >= UNIT) { power -= UNIT; n++; }
      outgoing = (outgoing + n) & 0xFF; power = 0; cannon_turn = 0;
      add_pop(shot_x, shot_y + 6, POP_SENT, n, 0);
      sfx(SFX_SENT, 0);
    }
    turn_gain = 0;
    if (incoming != 0 && drop_incoming()) return;
    spawn();
  }
  void step_settle() {
    bool busy = false;
    for (int i = 0; i < CELLS; i++) if (kind[i] != 0 && off[i] > 0) {
      off[i] = off[i] > 4 ? off[i] - 4 : 0;
      if (off[i] > 0) busy = true;
    }
    if (!busy) { sfx(SFX_SETTLE, 0); phase = RESOLVE; }
  }
  void spread_from(int r, int c, int i) {
    bool big = kind[i] == BIGFIRE;
    int ends = IS_FIRE(kind[i]) ? 15 : ENDS[t[i]], d, n;
    for (d = 0; d < 4; d++) {
      if ((ends & DIR_BIT[d]) == 0) continue;
      n = cell(r + DIR_DR[d], c + DIR_DC[d]);
      if (n == NOWHERE || tm[n] != OFF) continue;
      if (kind[n] == ROPE) {
        if (big || (ENDS[t[n]] & DIR_OPP[d]) != 0) { tm[n] = 0; wick = (wick + 1) & 0xFF; sfx(SFX_CATCH, wick < 16 ? wick : 16); }
      } else if (IS_BOMB(kind[n])) {
        tm[n] = 0; mult[n] = wick != 0 ? wick : 1; wave_bombs = (wave_bombs + 1) & 0xFF; nth[n] = wave_bombs; chn[n] = chain < 3 ? chain : 3;
        if (kind[n] == CANNON && cannon_turn == 0) {
          uint p = (uint)power + turn_gain * (uint)(CANNON_BONUS - 1);
          cannon_turn = 1; power = p > METER_MAX ? METER_MAX : (int)(p & 0xFFFF);
        }
      }
    }
  }
  void step_burn() {
    int i, k, n, a, na, lit, booms, crumbs, px, py, r, c, total;
    lit = 0; booms = 0; crumbs = 0; na = 0;
    for (i = 0; i < CELLS; i++) {
      if (tm[i] == OFF) continue;
      k = kind[i];
      if (k == ROPE || IS_FIRE(k)) { snap[na] = 1; lit++; }
      else if (IS_BOMB(k)) { snap[na] = 2; booms++; }
      else { snap[na] = 3; crumbs++; }
      active[na++] = i;
    }
    n_active = na;
    if (lit != 0) for (a = 0; a < na; a++) {
      if (snap[a] != 1) continue;
      i = active[a];
      r = 0; c = i; while (c >= COLS) { c -= COLS; r++; }
      tm[i]++;
      if (tm[i] == SPREAD) spread_from(r, c, i);
      if (tm[i] >= BURN_END) { k = kind[i]; clear_cell(i); if (k == ROPE) gain(10); }
    }
    if (booms != 0) for (a = 0; a < na; a++) {
      if (snap[a] != 2) continue;
      i = active[a];
      r = 0; c = i; while (c >= COLS) { c -= COLS; r++; }
      tm[i]++;
      if (tm[i] == BOOM_AT) {
        total = (mult[i] * nth[i] * CHAIN[chn[i]]) & 0xFFFF;
        gain(100 * total);
        if (mult[i] > best_wick) best_wick = mult[i];
        px = (FX + c * TS + 8) & 0xFF; py = (FY + r * TS + 8) & 0xFF;
        add_boom(r, c);
        add_pop(px, py - 4, chn[i] != 0 ? POP_CHAIN : nth[i] > 1 ? POP_BOMBS : POP_X, nth[i], total);
        shake = 8;
        sfx(SFX_BOOM, 0);
        if (chn[i] != 0) sfx(SFX_CHAIN, 0);
        if (kind[i] == CANNON) { shot_x = px; shot_y = py; }
        else for (k = 0; k < 8; k++) {
          n = cell(r + BLAST_DR[k], c + BLAST_DC[k]);
          if (n != NOWHERE && kind[n] == BLOCK && tm[n] == OFF) { tm[n] = 0; gain(50); }
        }
      }
      if (tm[i] >= BOOM_END) clear_cell(i);
    }
    if (crumbs != 0) for (a = 0; a < na; a++) if (snap[a] == 3) { i = active[a]; if (++tm[i] >= BLOCK_FADE) clear_cell(i); }
    if (lit == 0 && booms == 0 && crumbs == 0) {
      burn_idle = (burn_idle + 1) & 0xFF;
      if (burn_idle > 6) { if (wave_bombs != 0) chain = (chain + 1) & 0xFF; wave_bombs = 0; phase = RESOLVE; }
    } else burn_idle = 0;
  }

  // ---------- the calls a shell makes ----------
  public void Step(int held, int pressed) {
    int i, j;
    n_sfx = 0; n_active = 0;
    if (over != 0) return;
    if (phase == FALL) step_fall(held, pressed);
    else if (phase == BURN) step_burn();
    else if (phase == SETTLE) step_settle();
    if (phase == RESOLVE) resolve();
    for (i = 0, j = 0; i < n_booms; i++) {
      boom_t[i] = (boom_t[i] + 1) & 0xFF;
      if (boom_t[i] >= BOOM_LIFE) continue;
      if (j != i) { boom_r[j] = boom_r[i]; boom_c[j] = boom_c[i]; boom_t[j] = boom_t[i]; }
      j++;
    }
    n_booms = j;
    for (i = 0, j = 0; i < n_pops; i++) {
      pop_t[i] = (pop_t[i] + 1) & 0xFF;
      if (pop_t[i] >= POP_LIFE) continue;
      if (j != i) {
        pop_x[j] = pop_x[i]; pop_y[j] = pop_y[i]; pop_t[j] = pop_t[i];
        pop_type[j] = pop_type[i]; pop_a[j] = pop_a[i]; pop_b[j] = pop_b[i];
      }
      j++;
    }
    n_pops = j;
    if (shake > 0) shake--;
  }

  public void Init(int seed, int versus) {          // tw_init: the whole game zeroed first
    seed &= 0xFFFF;
    for (int i = 0; i < CELLS; i++) { kind[i] = 0; t[i] = 0; off[i] = 0; mult[i] = 0; nth[i] = 0; chn[i] = 0; active[i] = 0; tm[i] = OFF; }
    p_kind = p_t = p_col = p_y = p_serial = n_kind = n_t = 0;
    rng = 0; for (int i = 0; i < 16; i++) bag[i] = 0; bag_n = 0; drawn = since_block = since_big = 0;
    score = 0; placed = 0; level = to_level = wick = best_wick = 0;
    phase = over = 0; lock_t = soft_ok = das = burn_idle = chain = wave_bombs = shake = 0; das_dir = 0; fall_acc = 0;
    vs = 0; power = 0; turn_gain = 0; cannon_turn = shot_x = shot_y = outgoing = incoming = drop_col = tick = 0;
    n_booms = 0; for (int i = 0; i < MAX_BOOMS; i++) { boom_r[i] = 0; boom_c[i] = 0; boom_t[i] = 0; }
    n_pops = 0; for (int i = 0; i < MAX_POPS; i++) { pop_x[i] = 0; pop_y[i] = 0; pop_t[i] = 0; pop_type[i] = 0; pop_a[i] = 0; pop_b[i] = 0; }
    n_sfx = 0; for (int i = 0; i < MAX_SFX; i++) { sfx_id[i] = 0; sfx_arg[i] = 0; }
    fx_lost = 0; retiled = 0; n_active = 0;
    vs = versus; level = 1; phase = FALL;
    drop_col = seed % COLS;
    reseed(seed);
    draw_piece();
    spawn();
  }
}
