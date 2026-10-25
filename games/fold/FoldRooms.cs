// FOLD ROOMS for the VMX: a port of the firmware's C game (core/game_fold.c), its rules (FoldGame.cs, from
// microgames/fold/core/fold.c) and its 64x64 picture (from microgames/fold/app/vmx.c), checked tick for tick against
// the C by tools/parity/check.sh fold. Controls: D-pad moves (repeats while held), A talks to the tile you face, B undoes
// (held: restart). Clearing a level: A goes to the next one. The save (4 bytes, as the C's: the levels cleared) starts
// you at the next level; Scores gets that count.
// Levels: the FILE chunk "levels" (games/fold/levels.py packs microgames/fold/levels/*.fold), one read per start.
// Levels made in FOLD BUILDER on the Xbox: VMX World packs them the same way into this game's DATA\LEVELS.BIN
// (Storage.ReadData). With any there, the game opens on a list: ROOMS (the built-in levels, from the next one to clear)
// and the made ones, MADE 01 .. (a dot: cleared; no names, so nothing hints at a shape). A on the last made one goes
// back to the list, and so does holding B for 2 s. Without the file the game is exactly the C's (no list).
// The save: 4 bytes of levels cleared (the C's), then a u32 for each made level cleared (FNV-1a of its text).
// Text in the rooms and talk boxes uses the FOLD renderer's font rule (an unknown character is blank), so it is drawn
// here; Screen.Text (the firmware's, unknown = '?') only draws CLEARED! and A: NEXT, as the C does.
// Walking into the next room turns the view over to it as the FOLD builder's playtest does (microgames/fold/app/app.c
// room_anim_draw): a flat join slides, an outside corner tips away like a cube, an inside corner folds up towards you,
// 12 ticks, the two rooms in perspective (DrawTurn, all integer). It shows only the edge you just crossed.
using System;
using Microsoft.Vmx;

public class FoldRooms : Game {
  const int N = 8, NN = 64, CELL = 7, OX = 4, OY = 8, TALKLEN = 2 * FoldLevel.TEXTLEN;
  FoldLevel lv; FoldGame g = new FoldGame();
  int nlevels, level, cleared, holdB, popT, wonT, talkPage, demoK, demoWait; bool talkOn, isDemo, loaded;
  int[] hold = new int[4];
  string popWord; byte[] talk = new byte[TALKLEN];
  // the turn into the next room: tick anT of ANLEN (-1: none), the way moved (screen), 0 flat / 1 outside / 2 inside
  // corner; the two rooms' pictures as cell masks (Cells; the new one made again from the room when anDirty)
  const int ANLEN = 12;
  int anT = -1, anSd, anRel; bool anDirty;
  long[] cmOld = new long[64], cmtOld = new long[64], cmNew = new long[64], cmtNew = new long[64];
  // made levels: how many, which one is playing (-1: a built-in one), the list (menuOn) and its cursor, the cleared ones'
  // hashes, a message box over the list (tooBig)
  // FitsVmx: what a level needs of the 128 KB heap, measured (vxrun HEAP=, load + restart + back to the list):
  // ~62 KB + ~1.1 KB a square + 2 bytes a byte of its text (the text and the parse's line buffer). VMX World and FOLD
  // BUILDER use the same rule (microgames/fold/app/app.c fold_fits_vmx) to warn and to leave such levels off the VMX.
  const string USER = "LEVELS.BIN"; const int MAXDONE = 256;
  public static bool FitsVmx(int squares, int bytes) { return 1100 * squares + 2 * bytes <= 64000; }
  int nuser, userIx = -1, menuSel, menuTop, ndone, msgT; bool menuOn; int[] done = new int[MAXDONE]; string msg;

  int[] hdr = new int[2]; byte[] four = new byte[4];
  int U32(int at) { Package.Read("levels", at, four, 0, 4); return four[0] | four[1] << 8 | four[2] << 16 | four[3] << 24; }
  int UserU32(int at) { return Storage.ReadData(USER, at, four, 0, 4) == 4 ? four[0] | four[1] << 8 | four[2] << 16 | four[3] << 24 : 0; }
  void Load(int i) {
    level = ((i % nlevels) + nlevels) % nlevels; userIx = -1;
    int at = U32(2 + 4 * level), n = U32(6 + 4 * level) - at;
    lv = null; g.Drop();                          // the old level's cells can go before the new one is read
    byte[] t = new byte[n + 1]; Package.Read("levels", at, t, 0, n);
    Play(t, n);
  }
  void Play(byte[] t, int n) {
    lv = new FoldLevel(FoldLevel.CountSquares(t, n));
    if (!lv.Parse(t, n)) { lv = new FoldLevel(0); lv.player = 0; }   // the C: memset, so player 0
    t = null;
    g.Start(lv); anT = -1; popT = 0; wonT = 0; talkOn = false; holdB = 0; demoK = 0; demoWait = 0; for (int d = 0; d < 4; d++) hold[d] = 0;
  }
  // made level i (from 0); false (and the list stays) if it's not there or is too big for the heap
  bool LoadUser(int i) {
    int at = UserU32(2 + 4 * i), n = UserU32(6 + 4 * i) - at, len = Storage.DataLength(USER);
    if (at < 6 || n <= 0 || at + n > len) { Say("CAN'T READ IT"); return false; }
    if (!FitsVmx(0, n)) { Say("TOO BIG FOR|THE VMX"); return false; }   // before its text is even read
    lv = null; g.Drop();
    byte[] t = new byte[n + 1]; Storage.ReadData(USER, at, t, 0, n);
    if (!FitsVmx(FoldLevel.CountSquares(t, n), n)) { Say("TOO BIG FOR|THE VMX"); return false; }
    userIx = i; menuOn = false; Play(t, n);
    return true;
  }
  int UserHash(int i) {                           // FNV-1a of made level i's text
    int at = UserU32(2 + 4 * i), n = UserU32(6 + 4 * i) - at, h = unchecked((int)2166136261);
    byte[] b = new byte[64];
    for (int k = 0; k < n; k += 64) {
      int m = Storage.ReadData(USER, at + k, b, 0, n - k < 64 ? n - k : 64);
      for (int j = 0; j < m; j++) h = (h ^ b[j]) * 16777619;
    }
    return h;
  }
  bool Done(int h) { for (int k = 0; k < ndone; k++) if (done[k] == h) return true; return false; }
  void SaveAll() {
    byte[] s = new byte[4 + 4 * ndone];
    for (int i = 0; i < 4; i++) s[i] = (byte)(cleared >> (8 * i));
    for (int k = 0; k < ndone; k++) for (int i = 0; i < 4; i++) s[4 + 4 * k + i] = (byte)(done[k] >> (8 * i));
    Storage.Save(s);
  }
  void Say(string m) { msg = m; msgT = 60; }
  int[] userDone;                                 // the list's dots, worked out when it opens
  void OpenMenu() {
    menuOn = true; userIx = -1; lv = null; g.Drop();
    if (userDone == null) userDone = new int[nuser];
    for (int i = 0; i < nuser; i++) userDone[i] = Done(UserHash(i)) ? 1 : 0;
  }
  public override void Start(int seed, bool demo) {
    if (!loaded) {
      loaded = true;
      byte[] s = Storage.Load();
      if (s != null) for (int i = 0; i < 4 && i < s.Length; i++) cleared |= s[i] << (8 * i);
      if (s != null) for (int k = 0; 4 + 4 * k + 3 < s.Length && ndone < MAXDONE; k++)
        done[ndone++] = s[4 + 4 * k] | s[5 + 4 * k] << 8 | s[6 + 4 * k] << 16 | s[7 + 4 * k] << 24;
      Package.Read("levels", 0, four, 0, 2); nlevels = four[0] | four[1] << 8;
      if (Storage.DataLength(USER) >= 6 && Storage.ReadData(USER, 0, four, 0, 2) == 2) nuser = four[0] | four[1] << 8;
      Scores.Report(cleared, 0, 0, 0);
    }
    isDemoNow = demo;
    if (!demo && nuser > 0) { OpenMenu(); menuSel = cleared < nlevels ? 0 : 1; return; }
    Load(demo ? 0 : cleared < nlevels ? cleared : nlevels - 1);
  }
  void Moved(int r) {
    popWord = (r & FoldGame.FLIPPED) != 0 ? "FLIPPED" : (r & FoldGame.TURNED) != 0 ? "TURNED" : (r & FoldGame.SHIFTED) != 0 ? "SHIFTED" : (r & FoldGame.HINGED) != 0 ? "HINGED" : null;
    if (popWord != null) popT = 30;
    if (g.Solved()) {
      wonT = 1;
      if (isDemo) return;
      if (userIx >= 0) {
        int h = UserHash(userIx);
        if (!Done(h) && ndone < MAXDONE) { done[ndone++] = h; SaveAll(); }
      } else if (level + 1 > cleared) {
        cleared = level + 1;
        SaveAll(); Scores.Report(cleared, 0, 0, 0);
      }
    }
  }
  static readonly int[] BIT = { Buttons.Up, Buttons.Right, Buttons.Down, Buttons.Left };
  public override void Tick(int held, int pressed) {
    bool demo = isDemoNow; isDemo = demo;
    if (anT >= 0 && ++anT >= ANLEN) anT = -1;
    if (menuOn) { MenuTick(pressed); return; }
    if (talkOn) {
      if ((pressed & (Buttons.A | Buttons.B)) != 0) { if (++talkPage >= Pages()) talkOn = false; }
      return;
    }
    if (popT != 0) { popT--; return; }
    if (wonT != 0) {
      wonT++;
      if ((demo && wonT > 60) || (!demo && wonT > 15 && (pressed & Buttons.A) != 0)) {
        if (userIx < 0) Load(level + 1);
        else if (userIx + 1 >= nuser || !LoadUser(userIx + 1)) OpenMenu();
      }
      return;
    }
    if (demo) {                                   // the level's stored solution, a move every 6 ticks
      if (++demoWait < 6) return;
      demoWait = 0;
      if (demoK >= lv.nsol) { Load(level + 1); return; }
      int d = lv.sol[demoK++] - '0';
      DoMove((d - g.camrot) & 3);
      return;
    }
    for (int d = 0; d < 4; d++) {                 // first press, then every 5 ticks after 10
      hold[d] = (held & BIT[d]) != 0 ? hold[d] + 1 : 0;
      if (hold[d] == 1 || (hold[d] > 10 && (hold[d] - 10) % 5 == 0)) { DoMove(d); break; }
    }
    if ((pressed & Buttons.A) != 0) {
      int c = g.Facing();
      if (!g.Describe(c, talk, TALKLEN)) g.Describe(g.p, talk, TALKLEN);
      if (talk[0] != 0) { talkOn = true; talkPage = 0; }
    }
    if ((held & Buttons.B) != 0) {
      if (++holdB == 20) { if (userIx < 0) Load(level); else LoadUser(userIx); if (nuser > 0) holdB = 20; }   // (the C: from 0)
      if (holdB == 60 && nuser > 0) OpenMenu();
    }
    else { if (holdB > 0 && holdB < 20) { g.Undo(); anDirty = true; } holdB = 0; }
  }
  bool isDemoNow;
  // a move; through a doorway (not a teleporter) starts the turn, from the room as it was before the move
  void DoMove(int sd) {
    int f0 = g.camf, r0 = g.camrot, c1 = g.Step(g.p, (sd + r0) & 3);
    if (c1 >= 0 && (c1 / NN != f0 || g.kind[c1] == FoldLevel.FK_ICE)) Cells(cmOld, cmtOld, f0, r0, g.p);   // only a step out (or onto ice) can leave the room
    int r = g.Move(sd);
    anDirty = true;
    if ((r & FoldGame.CROSSED) != 0 && (r & FoldGame.TELEPORTED) == 0) { anSd = sd; anRel = Rel(f0, r0, sd, g.camf); anT = 0; }
    Moved(r);
  }

  // ---- the list of levels (only when there are made ones)
  const int ROWS = 6;
  void MenuTick(int pressed) {
    if (msgT > 0) { msgT--; if ((pressed & (Buttons.A | Buttons.B)) != 0) msgT = 0; return; }
    int n = nuser + 1;
    if ((pressed & Buttons.Up) != 0) menuSel = (menuSel + n - 1) % n;
    if ((pressed & Buttons.Down) != 0) menuSel = (menuSel + 1) % n;
    if (menuSel < menuTop) menuTop = menuSel;
    if (menuSel >= menuTop + ROWS) menuTop = menuSel - ROWS + 1;
    if ((pressed & Buttons.A) != 0) {
      if (menuSel == 0) { menuOn = false; Load(cleared < nlevels ? cleared : nlevels - 1); }
      else LoadUser(menuSel - 1);
    }
  }
  void DrawMenu(Screen s) {
    if (glyph == null) MakeFont();
    s.Clear();
    Text(s, 32 - 2 * 10 + 1, 2, "FOLD ROOMS"); Rect(s, 0, 9, 63, 9, true);
    for (int r = 0; r < ROWS && menuTop + r <= nuser; r++) {
      int i = menuTop + r, y = 13 + 8 * r;
      string label;
      if (i == 0) { int nx = cleared < nlevels ? cleared + 1 : nlevels; label = "ROOMS " + (nx < 10 ? "0" : "") + nx.ToString(); }
      else label = "MADE " + (i < 10 ? "0" : "") + i.ToString();
      if (i == menuSel) { Rect(s, 0, y - 1, 63, y + 5, true); TextInk(s, 6, y, label, false); }
      else Text(s, 6, y, label);
      bool dot = i == 0 ? cleared >= nlevels : userDone[i - 1] != 0;
      if (dot) Rect(s, 58, y + 1, 60, y + 3, i != menuSel);
    }
    if (menuTop > 0) { s.Set(62, 11, true); s.Set(61, 12, true); s.Set(63, 12, true); }
    if (menuTop + ROWS <= nuser) { s.Set(61, 60, true); s.Set(63, 60, true); s.Set(62, 61, true); }
    if (msgT > 0) {
      int bar = msg.IndexOf('|');
      string a = bar < 0 ? msg : msg.Substring(0, bar), b = bar < 0 ? null : msg.Substring(bar + 1, msg.Length - bar - 1);
      Rect(s, 4, 22, 59, 44, false); Rect(s, 4, 22, 59, 22, true); Rect(s, 4, 44, 59, 44, true); Rect(s, 4, 22, 4, 44, true); Rect(s, 59, 22, 59, 44, true);
      Text(s, 32 - 2 * a.Length + 1, b == null ? 31 : 27, a);
      if (b != null) Text(s, 32 - 2 * b.Length + 1, 35, b);
    }
  }

  public override void Draw(Screen s) {
    if (menuOn) { DrawMenu(s); return; }
    if (talkOn) { DrawTalk(s, talkPage); return; }
    DrawGame(s, userIx >= 0 ? userIx + 1 : level + 1, popT != 0 ? popWord : null);
    if (wonT != 0) {
      s.Fill(6, 22, 52, 22, false); Outline(s, 6, 22, 52, 22);
      s.Text(32 - Screen.TextWidth("CLEARED!") / 2, 26, "CLEARED!", true);
      if (wonT > 15) s.Text(32 - Screen.TextWidth("A: NEXT") / 2, 35, "A: NEXT", true);
    }
  }
  static void Outline(Screen s, int x, int y, int w, int h) {   // s_rect
    s.Fill(x, y, w, 1, true); s.Fill(x, y + h - 1, w, 1, true); s.Fill(x, y, 1, h, true); s.Fill(x + w - 1, y, 1, h, true);
  }

  // ---- the picture (vmx.c) ----
  // a 7x7 sprite ('#' = lit) as its lit pixels, x | y << 3 each
  static byte[] Spr(string[] r) {
    int n = 0; for (int j = 0; j < 7; j++) for (int i = 0; i < 7; i++) if (r[j][i] == '#') n++;
    byte[] b = new byte[n]; n = 0;
    for (int j = 0; j < 7; j++) for (int i = 0; i < 7; i++) if (r[j][i] == '#') b[n++] = (byte)(i | j << 3);
    return b;
  }
  static readonly byte[] BOX = Spr(new string[] { "#######", "#.....#", "#.#.#.#", "#..#..#", "#.#.#.#", "#.....#", "#######" });
  static readonly byte[] BOXON = Spr(new string[] { "#######", "#######", "##...##", "##.#.##", "##...##", "#######", "#######" });
  static readonly byte[] TARGET = Spr(new string[] { ".......", ".......", "..###..", "..#.#..", "..###..", ".......", "......." });
  static readonly byte[] MAN = Spr(new string[] { "..###..", "..###..", "...#...", ".#####.", "...#...", "..#.#..", ".#...#." });
  static readonly byte[] FLOOR = Spr(new string[] { ".......", ".......", ".......", "...#...", ".......", ".......", "......." });
  static readonly byte[] ARROW = Spr(new string[] { ".......", "...#...", "..###..", ".#.#.#.", "...#...", ".......", "......." });
  static readonly byte[] ICE = Spr(new string[] { ".......", ".#.....", "..#....", ".......", "....#..", ".....#.", "......." });
  static readonly byte[] ONEWAY = Spr(new string[] { "...#...", "..###..", ".#####.", "#######", "..###..", "..###..", "......." });
  static readonly byte[] PIT = Spr(new string[] { "#######", "#.#.#.#", "##.#.##", "#.#.#.#", "##.#.##", "#.#.#.#", "#######" });
  static readonly byte[] TELE = Spr(new string[] { ".#####.", "#.....#", "#.###.#", "#.#.#.#", "#.###.#", "#.....#", ".#####." });
  static readonly byte[] PLATE = Spr(new string[] { ".......", ".#####.", ".#####.", ".##.##.", ".#####.", ".#####.", "......." });
  static readonly byte[] GATE_SHUT = Spr(new string[] { "#######", "#.#.#.#", "#.#.#.#", "#.#.#.#", "#.#.#.#", "#.#.#.#", "#######" });
  static readonly byte[] GATE_OPEN = Spr(new string[] { "#.....#", ".......", ".......", ".......", ".......", ".......", "#.....#" });
  static readonly byte[] KEY = Spr(new string[] { ".......", ".###...", "##.####", ".###.##", ".......", ".......", "......." });
  static readonly byte[] LOCK = Spr(new string[] { "#######", "#######", "###.###", "##...##", "###.###", "###.###", "#######" });
  static readonly byte[] GRATE = Spr(new string[] { "#.#.#.#", ".......", "#.#.#.#", ".......", "#.#.#.#", ".......", "#.#.#.#" });
  static readonly byte[] LIGHT_OFF = Spr(new string[] { "..###..", ".#...#.", ".#...#.", "..#.#..", "..###..", "..###..", "......." });
  static readonly byte[] LIGHT_ON = Spr(new string[] { "#.###.#", ".#####.", ".#####.", "#.###.#", "..###..", "..###..", "......." });
  static readonly byte[] SENSOR = Spr(new string[] { ".......", "...#...", "..#.#..", ".#.#.#.", "..#.#..", "...#...", "......." });
  static readonly byte[] SIGN = Spr(new string[] { "#######", "#.....#", "#.###.#", "#.....#", "#######", "...#...", "...#..." });
  static readonly byte[] KEYPAD = Spr(new string[] { "#######", "#.....#", "#.#.#.#", "#.....#", "#.#.#.#", "#.....#", "#######" });
  static readonly byte[] BUTTON = Spr(new string[] { ".......", ".#####.", ".#...#.", ".#.#.#.", ".#...#.", ".#####.", "......." });

  // the sprites by number (CellCode), the arrows turned 4 ways; as masks (bit x + 8 y; MSKT: y + 8 x) for the turn
  const int S_WALL = 1, S_BOX = 2, S_BOXON = 3, S_TARGET = 4, S_MAN = 5, S_FLOOR = 6, S_ICE = 7, S_PIT = 8, S_TELE = 9, S_PLATE = 10;
  const int S_GATE_SHUT = 11, S_GATE_OPEN = 12, S_KEY = 13, S_LOCK = 14, S_GRATE = 15, S_LIGHT_OFF = 16, S_LIGHT_ON = 17, S_SENSOR = 18;
  const int S_SIGN = 19, S_KEYPAD = 20, S_BUTTON = 21, S_ARROW = 22, S_ONEWAY = 26, NSPR = 30;
  static readonly byte[] WALLSPR = Spr(new string[] { "######.", "######.", "######.", ".......", "##.####", "##.####", "......." });
  static readonly byte[][] SPR = MakeSpr();
  static readonly long[] MSK = new long[NSPR], MSKT = new long[NSPR];
  static readonly int[] RUNS = new int[128];        // a 7-bit slice's runs of 1s: count, then start | end << 3 (6 bits each)
  static byte[][] MakeSpr() {
    byte[][] t = new byte[NSPR][];
    t[S_WALL] = WALLSPR; t[S_BOX] = BOX; t[S_BOXON] = BOXON; t[S_TARGET] = TARGET; t[S_MAN] = MAN; t[S_FLOOR] = FLOOR; t[S_ICE] = ICE;
    t[S_PIT] = PIT; t[S_TELE] = TELE; t[S_PLATE] = PLATE; t[S_GATE_SHUT] = GATE_SHUT; t[S_GATE_OPEN] = GATE_OPEN; t[S_KEY] = KEY;
    t[S_LOCK] = LOCK; t[S_GRATE] = GRATE; t[S_LIGHT_OFF] = LIGHT_OFF; t[S_LIGHT_ON] = LIGHT_ON; t[S_SENSOR] = SENSOR; t[S_SIGN] = SIGN;
    t[S_KEYPAD] = KEYPAD; t[S_BUTTON] = BUTTON;
    for (int sd = 0; sd < 4; sd++) { t[S_ARROW + sd] = Turned(ARROW, sd); t[S_ONEWAY + sd] = Turned(ONEWAY, sd); }
    return t;
  }
  static void MakeMasks() {
    for (int k = 1; k < NSPR; k++) {
      byte[] b = SPR[k]; long m = 0, mt = 0;
      for (int i = 0; i < b.Length; i++) { m |= 1L << b[i]; mt |= 1L << ((b[i] >> 3) | (b[i] & 7) << 3); }
      MSK[k] = m; MSKT[k] = mt;
    }
    for (int v = 1; v < 128; v++) {
      int r = 0, n = 0;
      for (int i = 0; i < 7; i++) if ((v >> i & 1) != 0 && (i == 0 || (v >> (i - 1) & 1) == 0)) {
        int e = i; while (e < 7 && (v >> e & 1) != 0) e++;
        r |= (i | e << 3) << (3 + 6 * n); n++;
      }
      RUNS[v] = r | n;
    }
  }
  static byte[] Turned(byte[] b, int sd) {         // the sprite turned to point sd (it points up)
    byte[] o = new byte[b.Length];
    for (int k = 0; k < b.Length; k++) {
      int i = b[k] & 7, j = b[k] >> 3;
      int ax = sd == 0 ? i : sd == 1 ? 6 - j : sd == 2 ? 6 - i : j, ay = sd == 0 ? j : sd == 1 ? i : sd == 2 ? 6 - j : 6 - i;
      o[k] = (byte)(ax | ay << 3);
    }
    return o;
  }
  static void Sprite(Screen s, int x, int y, byte[] b) { for (int k = 0; k < b.Length; k++) s.Set(x + (b[k] & 7), y + (b[k] >> 3), true); }
  static void Rect(Screen s, int x0, int y0, int x1, int y1, bool v) { if (x1 >= x0 && y1 >= y0) s.Fill(x0, y0, x1 - x0 + 1, y1 - y0 + 1, v); }

  // the FOLD renderer's 3x5 font (core/surf.c's FONT; an unknown character is blank, as its gfx_glyph)
  static readonly string FONTCH = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 .-?!:/%()+><*";
  static readonly string[] FONTG = { "010101111101101", "110101110101110", "011100100100011", "110101101101110", "111100110100111",
    "111100110100100", "011100101101011", "101101111101101", "111010010010111", "001001001101010", "101101110101101", "100100100100111",
    "101111111101101", "110101101101101", "010101101101010", "110101110100100", "010101101110011", "110101110101101", "011100010001110",
    "111010010010010", "101101101101111", "101101101101010", "101101111111101", "101101010101101", "101101010010010", "111001010100111",
    "111101101101111", "010110010010111", "110001010100111", "110001010001110", "101101111001001", "111100110001110", "011100111101111",
    "111001010010010", "111101111101111", "111101111001110", "000000000000000", "000000000000010", "000000111000000", "110001010000010",
    "010010010000010", "000010000010000", "001001010100100", "101001010100101", "010100100100010", "010001001001010", "000010111010000",
    "100010001010100", "001010100010001", "000101010101000" };
  static int[] glyph;
  static void MakeFont() {
    MakeMasks();
    glyph = new int[256];
    for (int k = 0; k < FONTCH.Length; k++) {
      int m = 0; string gs = FONTG[k]; for (int i = 0; i < 15; i++) if (gs[i] == '1') m |= 1 << i;
      glyph[FONTCH[k]] = m;
    }
    for (int c = 'a'; c <= 'z'; c++) glyph[c] = glyph[c - 32];
  }
  static void Glyph(Screen s, int x, int y, int ch) { GlyphInk(s, x, y, ch, true); }
  static void GlyphInk(Screen s, int x, int y, int ch, bool on) {
    int m = glyph[ch & 255];
    for (int i = 0; i < 15; i++) if ((m >> i & 1) != 0) s.Set(x + i % 3, y + i / 3, on);
  }
  static void Text(Screen s, int x, int y, string t) { TextInk(s, x, y, t, true); }
  static void TextInk(Screen s, int x, int y, string t, bool on) { for (int i = 0; i < t.Length; i++, x += 4) GlyphInk(s, x, y, t[i], on); }

  void DrawGame(Screen s, int lvl, string line2) {
    if (glyph == null) MakeFont();
    s.Clear();
    if (anT >= 0) {
      if (anDirty) { Cells(cmNew, cmtNew, g.camf, g.camrot, g.p); anDirty = false; }
      DrawTurn(s);
    } else DrawRoom(s, g.camf, g.camrot, g.p);
    int left = g.left;
    string ls = lvl.ToString(); if (ls.Length < 2) ls = "0" + ls;
    Text(s, 1, 1, ls);
    Rect(s, 26, 1, 30, 5, true); Rect(s, 27, 2, 29, 4, false); s.Set(28, 3, true);
    Text(s, 33, 1, left.ToString());
    if (g.keys[0] != 0) {
      Rect(s, 40, 2, 42, 4, true); s.Set(41, 3, false); Rect(s, 43, 3, 45, 3, true); s.Set(45, 4, true);
      Text(s, 47, 1, (g.keys[0] > 9 ? 9 : (int)g.keys[0]).ToString());
    }
    string ms = (g.moves > 9999 ? 9999 : g.moves).ToString(); Text(s, 64 - 4 * ms.Length, 1, ms);
    if (line2 != null) {
      Rect(s, 6, 23, 57, 43, false); Rect(s, 6, 23, 57, 23, true); Rect(s, 6, 43, 57, 43, true); Rect(s, 6, 23, 6, 43, true); Rect(s, 57, 23, 57, 43, true);
      Text(s, 32 - 2 * 6 + 1, 27, "A ROOM"); Text(s, 32 - 2 * line2.Length + 1, 35, line2);
    }
  }
  void DrawRoom(Screen s, int f, int rot, int player) {
    for (int fy = 0; fy < N; fy++) for (int fx = 0; fx < N; fx++) {
      int sx, sy;                                // fold_to_screen
      switch (rot & 3) { case 0: sx = fx; sy = fy; break; case 1: sx = fy; sy = N - 1 - fx; break; case 2: sx = N - 1 - fx; sy = N - 1 - fy; break; default: sx = N - 1 - fy; sy = fx; break; }
      int x = OX + sx * CELL, y = OY + sy * CELL, code = CellCode(f * NN + fy * N + fx, fx, fy, rot, player);
      if (code == S_WALL) { s.Fill(x, y, 6, 3, true); s.Fill(x, y + 4, 2, 2, true); s.Fill(x + 3, y + 4, 4, 2, true); continue; }   // bricks
      for (; code != 0; code >>= 8) Sprite(s, x, y, SPR[code & 255]);
    }
  }
  // what cell c (at fx, fy of its side) shows: its sprites (S_*), 8 bits each, the first drawn first
  int CellCode(int c, int fx, int fy, int rot, int player) {
    if (g.wall[c] != 0) return S_WALL;
    bool hasBox = g.box[c] != 0;
    int k = g.kind[c], a = g.aux[c], t = g.target[c], bt = g.btn[c], code = 0, sh = 0;
    if (t == 0 && (bt == 0 || k == FoldLevel.FK_LIGHT) && !hasBox && c != player) {
      int outd = fy == 0 ? 0 : fx == N - 1 ? 1 : fy == N - 1 ? 2 : fx == 0 ? 3 : -1;
      if (k == FoldLevel.FK_ONEWAY) code = S_ONEWAY + ((a - rot) & 3);
      else if (k == FoldLevel.FK_PIT) code = S_PIT;
      else if (k == FoldLevel.FK_TELE) code = S_TELE;
      else if (k == FoldLevel.FK_GATE) code = (a & 0x80) != 0 ? S_GATE_OPEN : S_GATE_SHUT;
      else if (k == FoldLevel.FK_KEY) code = S_KEY;
      else if (k == FoldLevel.FK_LOCK) code = S_LOCK;
      else if (k == FoldLevel.FK_GRATE) code = S_GRATE;
      else if (k == FoldLevel.FK_LIGHT) code = (a & 0x80) != 0 ? S_LIGHT_ON : S_LIGHT_OFF;
      else if (k == FoldLevel.FK_SENSOR) code = S_SENSOR;
      else if (k == FoldLevel.FK_SIGN) code = S_SIGN;
      else if (k == FoldLevel.FK_KEYPAD) code = S_KEYPAD;
      else if (outd >= 0 && Open(c, outd)) code = S_ARROW + ((outd - rot) & 3);
      else code = k == FoldLevel.FK_ICE ? S_ICE : S_FLOOR;
      sh = 8;
    }
    if (bt != 0 && !hasBox && c != player) { code |= (k == FoldLevel.FK_PLATE ? S_PLATE : S_BUTTON) << sh; sh += 8; }
    if (t != 0 && !hasBox) { code |= S_TARGET << sh; sh += 8; }
    if (hasBox) { code |= (t != 0 ? S_BOXON : S_BOX) << sh; sh += 8; }
    if (c == player) code |= S_MAN << sh;
    return code;
  }

  // ---- the turn into the next room ----
  // the room's cells as 7x7 masks (cm: bit x + 8 y; cmt: bit y + 8 x), in screen order; from the same CellCode as DrawRoom
  void Cells(long[] cm, long[] cmt, int f, int rot, int player) {
    if (glyph == null) MakeFont();
    for (int fy = 0; fy < N; fy++) for (int fx = 0; fx < N; fx++) {
      int sx, sy;
      switch (rot & 3) { case 0: sx = fx; sy = fy; break; case 1: sx = fy; sy = N - 1 - fx; break; case 2: sx = N - 1 - fx; sy = N - 1 - fy; break; default: sx = N - 1 - fy; sy = fx; break; }
      int code = CellCode(f * NN + fy * N + fx, fx, fy, rot, player); long m = 0, mt = 0;
      for (; code != 0; code >>= 8) { m |= MSK[code & 255]; mt |= MSKT[code & 255]; }
      cm[sy * N + sx] = m; cmt[sy * N + sx] = mt;
    }
  }
  // over what the move went from side f0 (turned r0) to f1, going sd on screen: 0 flat, 1 outside corner (f1 faces the
  // way you went), 2 inside corner (it faces back at you); from the cells' world axes (FoldGame.sq: o u v n, the back
  // side's u and n reversed)
  static readonly int[] RP = { 1, 0, -1, 0 }, RQ = { 0, 1, 0, -1 }, DP = { 0, -1, 0, 1 }, DQ = { 1, 0, -1, 0 };   // screen right / down in the side's x, y
  int Rel(int f0, int r0, int sd, int f1) {
    sbyte[] q = g.sq; int a = (f0 >> 1) * 12, b = (f1 >> 1) * 12, sa = (f0 & 1) != 0 ? -1 : 1, sb = (f1 & 1) != 0 ? -1 : 1;
    int px = sd == 1 ? RP[r0] : sd == 3 ? -RP[r0] : sd == 2 ? DP[r0] : -DP[r0], py = sd == 1 ? RQ[r0] : sd == 3 ? -RQ[r0] : sd == 2 ? DQ[r0] : -DQ[r0];
    int nn = 0, nd = 0;
    for (int i = 0; i < 3; i++) {
      int nb = sb * q[b + 9 + i];
      nn += sa * q[a + 9 + i] * nb; nd += (px * sa * q[a + 3 + i] + py * q[a + 6 + i]) * nb;
    }
    return nn > 0 ? 0 : nd > 0 ? 1 : nd < 0 ? 2 : 0;
  }
  // the eased turn at each tick (smoothstep of (T + 1) / 12): cos and sin of its angle (0..90 degrees) and its fraction, x16384
  static readonly int[] TC = { 16376, 16273, 15893, 15044, 13606, 11585, 9127, 6489, 3981, 1902, 506, 0 };
  static readonly int[] TS = { 506, 1902, 3981, 6489, 9127, 11585, 13606, 15044, 15893, 16273, 16376, 16384 };
  static readonly int[] TE = { 322, 1214, 2560, 4248, 6163, 8192, 10221, 12136, 13824, 15170, 16062, 16384 };
  static readonly int[] BAYER = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
  const int D = 146;                              // the eye's distance from the room, in pixels (the playtest's 2.6 rooms)
  int[] colTa = new int[64], colSc = new int[64];
  void DrawTurn(Screen s) {
    int T = anT, C = TC[T], S = TS[T];
    bool hor = anSd == 1 || anSd == 3;
    long[] o = hor ? cmtOld : cmOld, n = hor ? cmtNew : cmNew;
    if (anRel == 0) { Face(s, o, 16384, 0, -56L * TE[T], 0); Face(s, n, 16384, 0, 56L * (16384 - TE[T]), 0); }
    else if (anRel == 1) { Face(s, o, C, S, -28L * S, -28L * (16384 - C)); Face(s, n, S, -C, 28L * C, -28L * (16384 - S)); }
    else { Face(s, o, C, -S, -28L * S, 28L * (16384 - C)); Face(s, n, S, C, 28L * C, 28L * (16384 - S)); }
  }
  // a room picture on a square turned about the screen axis across the move: a point sv (-28..28 px along the move from
  // the room's middle) is at along = (sv c + a) / 16384, depth z = (sv sn + b) / 16384 (towards the eye), seen from D.
  // Each screen line across the move shows one line of the picture (ta along the move), scaled, read 7 pixels (a cell)
  // at a time from the cell masks (cmt across a sideways move, cm across an up / down one) as runs of lit pixels.
  // Turned-away sides are dithered; the outline is drawn.
  void Face(Screen s, long[] cells, int c, int sn, long a, long b) {
    const long DQ14 = D * 16384L;
    long s2l = -28L * c + a, z2l = -28L * sn + b, s2r = 28L * c + a, z2r = 28L * sn + b;
    if (s2l * (DQ14 - z2r) >= s2r * (DQ14 - z2l)) return;   // seen from behind (or edge on)
    int sh = (4915 + (11469 * (c > 0 ? c : 0) >> 14)) * 40 >> 14, mask = 0;
    for (int k = 0; k < 16; k++) if (2 * BAYER[k] + 1 < sh) mask |= 1 << k;
    bool hor = anSd == 1 || anSd == 3;
    int sg = anSd == 1 || anSd == 2 ? 1 : -1, lo = hor ? 0 : OY, cA = hor ? 64 : 72, cP = hor ? 72 : 64, plo = hor ? OY : 0;
    for (int i = lo; i < 64; i++) {
      long A = sg * (2 * i + 1 - cA), num = A * (DQ14 - b) - 2L * D * a, den = 2L * D * c + A * sn;
      if (den < 0) { num = -num; den = -den; }
      if (den == 0 || num < -28 * den || num >= 28 * den) { colSc[i] = 0; continue; }
      colTa[i] = (int)((num + 28 * den) / den);
      colSc[i] = (int)((DQ14 << 16) / (DQ14 - (num * sn / den + b)));   // screen pixels a picture pixel, x65536
    }
    int pbase = (cP - 1) * 32768 + 65535;
    for (int i = lo; i < 64; i++) {
      int sc = colSc[i]; if (sc == 0) continue;
      int bs = pbase - 28 * sc, top = bs >> 16, bot = (56 * sc + bs) >> 16;   // picture pixel tp is on screen [(tp sc + bs) >> 16, next)
      int q0 = top < plo ? plo : top, q1 = bot > 64 ? 64 : bot;
      if ((i > lo && colSc[i - 1] == 0) || (i < 63 && colSc[i + 1] == 0)) {   // the side's edge
        if (q1 > q0) { if (hor) s.Fill(i, q0, 1, q1 - q0, true); else s.Fill(q0, i, q1 - q0, 1, true); }
        continue;
      }
      if (top >= plo) { if (hor) s.Set(i, top, true); else s.Set(top, i, true); }
      if (bot - 1 < 64) { if (hor) s.Set(i, bot - 1, true); else s.Set(bot - 1, i, true); }
      int ta = colTa[i], tt = anSd == 1 || anSd == 2 ? ta : 55 - ta, ct = tt / 7, sl0 = 8 * (tt - 7 * ct), ix = hor ? ct : 8 * ct, ixs = hor ? 8 : 1;
      int dm = hor ? (mask >> (i & 3) & 1) | (mask >> (4 + (i & 3)) & 1) << 1 | (mask >> (8 + (i & 3)) & 1) << 2 | (mask >> (12 + (i & 3)) & 1) << 3 : mask >> ((i & 3) << 2) & 15;
      for (int t0 = 0; t0 < 56; t0 += 7, ix += ixs) {
        int sl = (int)(cells[ix] >> sl0) & 127; if (sl == 0) continue;
        int rr = RUNS[sl], n = rr & 7, b0 = t0 * sc + bs;
        for (rr >>= 3; n > 0; n--, rr >>= 6) {
          int q = ((rr & 7) * sc + b0) >> 16, qe = ((rr >> 3 & 7) * sc + b0) >> 16;
          if (q < q0) q = q0; if (qe > q1) qe = q1;
          if (qe <= q) continue;
          if (dm == 15) { if (hor) s.Fill(i, q, 1, qe - q, true); else s.Fill(q, i, qe - q, 1, true); }
          else for (; q < qe; q++) if ((dm >> (q & 3) & 1) != 0) { if (hor) s.Set(i, q, true); else s.Set(q, i, true); }
        }
      }
    }
  }

  bool Open(int c, int d) { int e = g.Step(c, d); return e >= 0 && g.wall[e] == 0; }   // an edge cell you can walk out of

  // ---- talk boxes: words wrapped to 14 characters (vmx.c wrap), 7 lines a page ----
  byte[] lines = new byte[40 * 15]; int[] lineLen = new int[40];
  int Wrap() {
    int n = 0, len = 0, t = 0; lineLen[0] = 0;
    while (talk[t] != 0 && n < 40) {
      while (talk[t] == ' ') t++;
      int w = 0; while (talk[t + w] != 0 && talk[t + w] != ' ') w++;
      if (w == 0) break;
      if (len != 0 && len + 1 + w > 14) { n++; len = 0; if (n == 40) break; lineLen[n] = 0; }
      if (len != 0) lines[n * 15 + len++] = (byte)' ';
      for (int i = 0; i < w && len < 14; i++) lines[n * 15 + len++] = talk[t + i];
      lineLen[n] = len; t += w;
    }
    return len != 0 ? n + 1 : n;
  }
  int Pages() { return (Wrap() + 6) / 7; }
  void DrawTalk(Screen s, int page) {
    if (glyph == null) MakeFont();
    int n = Wrap(), pages = (n + 6) / 7;
    Rect(s, 1, 9, 62, 62, false); Rect(s, 1, 9, 62, 9, true); Rect(s, 1, 62, 62, 62, true); Rect(s, 1, 9, 1, 62, true); Rect(s, 62, 9, 62, 62, true);
    for (int k = 0; k < 7 && page * 7 + k < n; k++) {
      int l = page * 7 + k;
      for (int i = 0; i < lineLen[l]; i++) Glyph(s, 4 + 4 * i, 12 + k * 7, lines[l * 15 + i]);
    }
    if (page + 1 < pages) { s.Set(58, 59, true); s.Set(59, 59, true); s.Set(60, 59, true); s.Set(59, 60, true); }
  }
}
