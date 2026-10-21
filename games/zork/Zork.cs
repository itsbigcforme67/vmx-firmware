// ZORK I for the VMX: our Z-machine (ZMachine.cs) running Zork I (MIT-licensed by Microsoft, 2025:
// github.com/historicalsource/zork1, COMPILED/zork1.z3, packed in GAME.VXP). A line-for-line port of
// microgames/ui/games/zork_core.js plus the web simulator's input rules (POCKET ARCADE vmx/games.js "zork"), so it
// plays exactly like the simulator; tools/parity checks that frame by frame. No keyboard, so commands are built from
// word lists:
//   MOVE  directions (one pick = go)          VERB  actions (LOOK etc. go at once, the rest wait for a noun)
//   SEEN  nouns + adjectives the game printed lately, newest first (the dictionary's own noun/adjective flags)
//   LINK  WITH, IN, ON, TO ...                A-Z   the whole dictionary by first letter
//   MISC  WAIT, AGAIN, SCORE, SAVE ...
// Screen: room name (inverted bar), 8 lines x 16 columns of text, the command line. Reading: up/down scroll (held:
// auto-repeat), A pages on, then A opens the word picker. Picker: left/right category, up/down word, A adds it
// (<ENTER> sends), B takes the last word back (or closes the picker).
// The game's SAVE command keeps a snapshot in memory (RESTORE goes back to it) and also writes it with Storage.Save,
// so it survives power-off; Start loads it back into the saved slot (the simulator page does the same with
// localStorage). Snapshot layout: ZMachine.Snapshot().
// Memory (128 KB heap): the story itself is the game's memory (87 KB), the save slot 12 KB, the 600 lines of text
// 10 KB as bytes; the word lists are built from the dictionary in the story instead of being copied out as strings.
using System;
using Microsoft.Vmx;

public class Zork : Game {
  const int W = 64, COLS = 16, ROWS = 8, TY = 8, LH = 6, PICK_ROWS = 7, SEEN_MAX = 40, KEEP = 600, LCAP = KEEP + 2;
  const int C_MOVE = 0, C_VERB = 1, C_SEEN = 2, C_LINK = 3, C_AZ = 4, C_MISC = 5, NCATS = 6;
  static readonly string[] MOVE = { "NORTH", "SOUTH", "EAST", "WEST", "NE", "NW", "SE", "SW", "UP", "DOWN", "IN", "OUT" };
  static readonly string[] VERB = { "LOOK", "INVENTORY", "EXAMINE", "TAKE", "DROP", "OPEN", "CLOSE", "READ", "PUT", "TURN ON", "TURN OFF",
    "MOVE", "PUSH", "PULL", "ATTACK", "THROW", "GIVE", "CLIMB", "ENTER", "EAT", "DRINK", "TIE", "UNLOCK", "LOCK", "DIG",
    "FILL", "POUR", "INFLATE", "WAVE", "RING", "PRAY", "LISTEN", "SMELL", "SEARCH", "WIND", "KNOCK", "BURN", "LIGHT" };
  static readonly string[] SOLO = { "LOOK", "INVENTORY", "LISTEN", "PRAY" };   // ... and MOVE
  static readonly string[] LINK = { "WITH", "IN", "ON", "TO", "AT", "FROM", "UNDER", "OFF", "ALL", "IT", "AND", "EXCEPT" };
  static readonly string[] MISC = { "WAIT", "AGAIN", "SCORE", "DIAGNOSE", "SAVE", "RESTORE", "VERBOSE", "BRIEF", "RESTART", "YES", "NO", "HELLO" };
  static readonly string[] CATS = { "MOVE", "VERB", "SEEN", "LINK", "A-Z", "MISC" };
  const string ENTER = "<ENTER>";
  static readonly string[] STOP = { "the", "a", "an", "you", "your", "it", "is", "are", "there", "here", "of", "to", "and", "with", "in" };
  // Demo: plays a list of commands through the same picker, a key at a time, reading each answer at a human pace.
  static readonly string[][] DEMO = { new string[] { "OPEN", "MAILBOX" }, new string[] { "READ", "LEAFLET" }, new string[] { "NORTH" },
    new string[] { "EAST" }, new string[] { "OPEN", "WINDOW" }, new string[] { "WEST" }, new string[] { "WEST" },
    new string[] { "TAKE", "LANTERN" }, new string[] { "MOVE", "RUG" }, new string[] { "OPEN", "TRAP", "DOOR" },
    new string[] { "TURN ON", "LANTERN" }, new string[] { "DOWN" } };

  ZMachine vm; bool demo;
  int t, top;
  // g.lines: a ring of LCAP lines of up to 16 characters (a longer "> command" line only ever shows its first 16)
  byte[] lch = new byte[LCAP * COLS]; byte[] llen = new byte[LCAP]; int lhead, ln, ldropped;
  // g.pick (picking = it isn't null)
  bool picking; int pcat; int[] pcur = new int[NCATS], pscroll = new int[NCATS]; string pletter;
  string[] cmd = new string[16]; int ncmd;          // a null word is the JS's undefined (an A past the list's end)
  string[] seen = new string[SEEN_MAX], seen2 = new string[SEEN_MAX]; int nseen;
  string[] fresh = new string[32]; int nfresh;
  // g.az: dictionary entries by first letter (azAddr[azStart[L]..azStart[L + 1])), letters = the sorted keys
  ushort[] azAddr; int[] azStart = new int[27]; string[] letters = new string[26]; int[] letterIdx = new int[26]; int nletters;
  // g.full: the whole word the game printed for a 6-letter dictionary entry (words up to 6 letters need none), kept
  // only for the A-Z words (the only ones it is asked for), by their index in azAddr
  short[] fullOff; byte[] fullLen; byte[] pool = new byte[512]; int pooln;
  int demoAt, wait, readT; bool demoing;
  int holdUp, holdDown;
  char[] wb = new char[64]; char[] cbuf = new char[64];
  string[] CH = new string[128];

  public override void Start(int seed, bool demo) {
    this.demo = demo;
    vm = new ZMachine("zork1.z3", seed, Storage.Load());   // the game's own SAVE, from before power-off
    BuildAz();
    fullOff = new short[azAddr.Length]; fullLen = new byte[azAddr.Length];
    for (int i = 0; i < azAddr.Length; i++) fullOff[i] = -1;
    vm.Run(); Absorb(0);
  }
  void BuildAz() {                                  // words: noun/verb-ish flags, not a direction, letters only
    int n = vm.dictCount; byte[] let = new byte[n]; int cnt = 0;
    for (int i = 0; i < n; i++) {
      int ad = vm.dictStart + i * vm.dictLen, f = vm.m[ad + 4];
      if ((f & 0xa0) == 0 || (f & 0x04) != 0) continue;
      vm.DecodeTmp(ad); if (vm.ntmp == 0) continue;
      bool ok = true; for (int k = 0; k < vm.ntmp; k++) if (vm.tmp[k] < 'a' || vm.tmp[k] > 'z') ok = false;
      if (!ok) continue;
      let[i] = (byte)(vm.tmp[0] - 'a' + 1); cnt++;
    }
    azAddr = new ushort[cnt]; int o = 0; nletters = 0;
    for (int L = 0; L < 26; L++) {
      azStart[L] = o;
      for (int i = 0; i < n; i++) if (let[i] == L + 1) azAddr[o++] = (ushort)(vm.dictStart + i * vm.dictLen);
      if (o > azStart[L]) { letters[nletters] = ((char)('A' + L)).ToString(); letterIdx[nletters] = L; nletters++; }
    }
    azStart[26] = o;
  }

  // ---- lines ------------------------------------------------------------------------------------------------------
  int LineAt(int i) { return ((lhead + i) % LCAP) * COLS; }
  void PushSlot() { if (ln == LCAP) { lhead = (lhead + 1) % LCAP; ln--; ldropped++; } ln++; }
  void PushLine(char[] c, int n) { PushSlot(); int o = LineAt(ln - 1); if (n > COLS) n = COLS; llen[(o / COLS)] = (byte)n; for (int i = 0; i < n; i++) lch[o + i] = (byte)c[i]; }
  void PushBytes(byte[] b, int s, int n) {          // uppercase
    PushSlot(); int o = LineAt(ln - 1); llen[o / COLS] = (byte)n;
    for (int i = 0; i < n; i++) { int c = b[s + i]; if (c >= 'a' && c <= 'z') c -= 32; lch[o + i] = (byte)c; }
  }
  void PushStr(string s) {
    PushSlot(); int o = LineAt(ln - 1), n = Math.Min(s.Length, COLS); llen[o / COLS] = (byte)n;
    for (int i = 0; i < n; i++) lch[o + i] = (byte)s[i];
  }
  bool More() { return top + ROWS < ln; }

  // new game text (vm.outb): wrap, remember its nouns, show from `from`
  void Absorb(int from) {
    byte[] b = vm.outb; int s = 0, e = vm.outn; ldropped = 0;
    int q = e; while (q > s && (b[q - 1] == ' ' || b[q - 1] == '\n')) q--;     // text.replace(/\n*>\s*$/, "")
    if (q > s && b[q - 1] == '>') { q--; while (q > s && b[q - 1] == '\n') q--; e = q; }
    while (s < e && b[s] == '\n') s++;                                          // .replace(/^\n+/, "")
    // wrap(): paragraphs at \n, words at runs of spaces, 16 columns, longer words cut
    int ll = 0;
    for (int i = s; ; ) {
      int pe = i; while (pe < e && b[pe] != '\n') pe++;
      ll = 0;
      for (int k = i; k < pe; ) {
        if (b[k] == ' ') { k++; continue; }
        int ws = k; while (k < pe && b[k] != ' ') k++;
        int wl = k - ws;
        while (wl > COLS) { if (ll > 0) { PushLine(wb, ll); ll = 0; } PushBytes(b, ws, COLS); ws += COLS; wl -= COLS; }
        if (ll == 0) { for (int j = 0; j < wl; j++) wb[j] = Up(b[ws + j]); ll = wl; }
        else if (ll + 1 + wl <= COLS) { wb[ll++] = ' '; for (int j = 0; j < wl; j++) wb[ll++] = Up(b[ws + j]); }
        else { PushLine(wb, ll); for (int j = 0; j < wl; j++) wb[j] = Up(b[ws + j]); ll = wl; }
      }
      PushLine(wb, ll);
      if (pe >= e) break;
      i = pe + 1;
    }
    int total = ln + ldropped;
    if (total > KEEP) {
      int k = total - KEEP, more = k - ldropped;
      lhead = (lhead + more) % LCAP; ln -= more; from = Math.Max(0, from - k);
    }
    top = from;
    // the nouns: every run of letters that the dictionary flags as a noun/adjective (not a stop word or a list word)
    nfresh = 0;
    for (int i = s; i < e; ) {
      if (!IsLetter(b[i])) { i++; continue; }
      int ws = i; while (i < e && IsLetter(b[i])) i++;
      int len = i - ws, n6 = Math.Min(len, 6);
      for (int j = 0; j < n6; j++) wb[j] = Lo(b[ws + j]);
      int ent = vm.Lookup(wb, n6);
      if (ent == 0 || InList(STOP, b, ws, len, false)) continue;
      int f = vm.m[ent + 4]; if ((f & 0xa0) == 0 || (f & 0x04) != 0) continue;
      if (InList(MOVE, b, ws, len, true) || InList(VERB, b, ws, len, true) || InList(LINK, b, ws, len, true)) continue;
      int L = Lo(b[ws]) - 'a', ai = -1;                                     // its A-Z index, if it's an A-Z word
      for (int j = azStart[L]; j < azStart[L + 1]; j++) if (azAddr[j] == ent) { ai = j; break; }
      if (ai >= 0) FullSet(ai, b, ws, len);
      if (FindIn(fresh, nfresh, b, ws, len) >= 0) continue;
      string up = null; int si = FindIn(seen, nseen, b, ws, len);
      if (si >= 0) up = seen[si];
      else {
        if (cbuf.Length < len) cbuf = new char[len];
        for (int j = 0; j < len; j++) cbuf[j] = Up(b[ws + j]);
        up = String.FromChars(cbuf, 0, len);
      }
      if (nfresh == fresh.Length) { string[] nf = new string[nfresh * 2]; Array.Copy(fresh, 0, nf, 0, nfresh); fresh = nf; }
      fresh[nfresh++] = up;
    }
    // last mentioned first (a room's objects come at its end), then the older ones
    int c = 0;
    for (int i = nfresh - 1; i >= 0 && c < SEEN_MAX; i--) seen2[c++] = fresh[i];
    for (int i = 0; i < nseen && c < SEEN_MAX; i++) {
      bool inF = false; for (int j = 0; j < nfresh; j++) if (fresh[j] == seen[i]) { inF = true; break; }
      if (!inF) seen2[c++] = seen[i];
    }
    string[] sw = seen; seen = seen2; seen2 = sw; nseen = c;
    for (int i = 0; i < nfresh; i++) fresh[i] = null;
    vm.outn = 0;
  }
  static bool IsLetter(int c) { return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'); }
  static char Up(int c) { return (char)(c >= 'a' && c <= 'z' ? c - 32 : c); }
  static char Lo(int c) { return (char)(c >= 'A' && c <= 'Z' ? c + 32 : c); }
  static bool Same(string w, byte[] b, int s, int len, bool upper) {
    if (w == null || w.Length != len) return false;
    for (int i = 0; i < len; i++) if (w[i] != (upper ? Up(b[s + i]) : Lo(b[s + i]))) return false;
    return true;
  }
  static bool InList(string[] l, byte[] b, int s, int len, bool upper) { for (int i = 0; i < l.Length; i++) if (Same(l[i], b, s, len, upper)) return true; return false; }
  static int FindIn(string[] l, int n, byte[] b, int s, int len) { for (int i = 0; i < n; i++) if (Same(l[i], b, s, len, true)) return i; return -1; }
  static bool Has(string[] l, string w) { for (int i = 0; i < l.Length; i++) if (l[i] == w) return true; return false; }

  void FullSet(int e, byte[] b, int s, int len) {   // g.full.set(w.slice(0, 6), W)
    if (len <= 6) { fullOff[e] = -1; return; }       // the same as no entry: the dictionary's own word
    if (fullOff[e] >= 0 && fullLen[e] == len) {
      bool same = true; for (int i = 0; i < len; i++) if (pool[fullOff[e] + i] != Up(b[s + i])) { same = false; break; }
      if (same) return;
    }
    if (pooln + len > pool.Length) {                 // full: copy the live words into a fresh pool
      int live = len; for (int i = 0; i < fullOff.Length; i++) if (fullOff[i] >= 0) live += fullLen[i];
      byte[] np = new byte[Math.Max(pool.Length, live * 2)]; int o = 0;
      for (int i = 0; i < fullOff.Length; i++) if (fullOff[i] >= 0) {
        Array.Copy(pool, fullOff[i], np, o, fullLen[i]); fullOff[i] = (short)o; o += fullLen[i];
      }
      pool = np; pooln = o;
    }
    fullOff[e] = (short)pooln; fullLen[e] = (byte)len;
    for (int i = 0; i < len; i++) pool[pooln++] = (byte)Up(b[s + i]);
  }
  // an A-Z word as the list shows it (g.full.get(w) || w.toUpperCase()) into wb; its length
  int AzText(int e) {
    int ad = azAddr[e];
    if (fullOff[e] >= 0) { int n = fullLen[e]; for (int i = 0; i < n; i++) wb[i] = (char)pool[fullOff[e] + i]; return n; }
    vm.DecodeTmp(ad); for (int i = 0; i < vm.ntmp; i++) wb[i] = Up(vm.tmp[i]);
    return vm.ntmp;
  }

  void Submit() {
    string c = "";
    for (int i = 0; i < ncmd; i++) { if (i > 0) c = c + " "; if (cmd[i] != null) c = c + cmd[i]; }
    ncmd = 0; picking = false;
    if (demoing) demoAt++;
    int from = ln;
    PushStr("> " + c);
    vm.Input(c); vm.Run();
    Absorb(from);
    if (vm.state == ZMachine.QUIT) { PushStr(""); PushStr("*** THE END ***"); }
  }

  // ---- items(g, cat): the picker's lists, with <ENTER> first once a word is in the command ------------------------
  bool AzWords(int cat) { return cat == C_AZ && pletter != null; }
  int BaseCount(int cat) {
    switch (cat) {
      case C_MOVE: return MOVE.Length;
      case C_VERB: return VERB.Length;
      case C_SEEN: return nseen;
      case C_LINK: return LINK.Length;
      case C_MISC: return MISC.Length;
    }
    if (pletter != null) { int L = pletter[0] - 'A'; return azStart[L + 1] - azStart[L]; }
    return nletters;
  }
  int ItemCount(int cat) { return BaseCount(cat) + (ncmd > 0 ? 1 : 0); }
  string BaseStr(int cat, int i) {                  // every list but the A-Z words
    switch (cat) {
      case C_MOVE: return MOVE[i];
      case C_VERB: return VERB[i];
      case C_SEEN: return seen[i];
      case C_LINK: return LINK[i];
      case C_MISC: return MISC[i];
    }
    return letters[i];
  }
  // list[k]: null past either end
  string ItemStr(int cat, int k) {
    if (k < 0 || k >= ItemCount(cat)) return null;
    if (ncmd > 0) { if (k == 0) return ENTER; k--; }
    if (!AzWords(cat)) return BaseStr(cat, k);
    int n = AzText(azStart[pletter[0] - 'A'] + k);
    return String.FromChars(wb, 0, n);
  }
  bool ItemIs(int cat, int k, string w) {
    if (k < 0 || k >= ItemCount(cat)) return false;
    if (ncmd > 0) { if (k == 0) return w == ENTER; k--; }
    if (!AzWords(cat)) return BaseStr(cat, k) == w;
    int n = AzText(azStart[pletter[0] - 'A'] + k);
    if (n != w.Length) return false;
    for (int i = 0; i < n; i++) if (wb[i] != w[i]) return false;
    return true;
  }
  int IndexOf(int cat, string w) { int n = ItemCount(cat); for (int k = 0; k < n; k++) if (ItemIs(cat, k, w)) return k; return -1; }

  void OpenPick() {
    picking = true; pcat = ncmd > 0 ? 2 : 0; pletter = null;
    for (int i = 0; i < NCATS; i++) { pcur[i] = 0; pscroll[i] = 0; }
  }

  public override void Tick(int held, int pressed) {
    // up/down auto-repeat while held (the simulator page's logic; its tap queue isn't used)
    int inp = pressed & 63;
    holdUp = (held & Buttons.Up) != 0 ? holdUp + 1 : 0; if (holdUp > 12 && holdUp % 3 == 0) inp |= Buttons.Up;
    holdDown = (held & Buttons.Down) != 0 ? holdDown + 1 : 0; if (holdDown > 12 && holdDown % 3 == 0) inp |= Buttons.Down;
    Step(demo ? Demo() : inp);
  }

  void Step(int inp) {
    bool up = (inp & Buttons.Up) != 0, down = (inp & Buttons.Down) != 0, left = (inp & Buttons.Left) != 0,
      right = (inp & Buttons.Right) != 0, a = (inp & Buttons.A) != 0, b = (inp & Buttons.B) != 0;
    t++;
    if (vm.state == ZMachine.QUIT) return;
    if (!picking) {
      if (down && More()) top++;
      if (up && top > 0) top--;
      if (a || b) { if (More()) top = Math.Min(top + ROWS - 1, ln - ROWS); else OpenPick(); }
      return;
    }
    int count = ItemCount(pcat);
    if (left || right) { pcat = (pcat + (right ? 1 : NCATS - 1)) % NCATS; return; }
    if (up) pcur[pcat] = Math.Max(0, pcur[pcat] - 1);
    if (down) pcur[pcat] = Math.Min(count - 1, pcur[pcat] + 1);
    int cur = pcur[pcat];
    if (cur < pscroll[pcat]) pscroll[pcat] = cur;
    if (cur >= pscroll[pcat] + PICK_ROWS) pscroll[pcat] = cur - PICK_ROWS + 1;
    if (b) {
      if (pcat == C_AZ && pletter != null) { pletter = null; pcur[pcat] = pscroll[pcat] = 0; }
      else if (ncmd > 0) { cmd[--ncmd] = null; }
      else picking = false;
      return;
    }
    if (!a || count == 0) return;
    string it = ItemStr(pcat, cur);
    if (it == ENTER) { Submit(); return; }
    if (pcat == C_AZ && pletter == null) { pletter = it; pcur[pcat] = pscroll[pcat] = 0; return; }
    bool solo = ncmd == 0 && (Has(SOLO, it) || Has(MOVE, it));
    if (ncmd == cmd.Length) { string[] nc = new string[ncmd * 2]; Array.Copy(cmd, 0, nc, 0, ncmd); cmd = nc; }
    cmd[ncmd++] = it;
    if (solo) { Submit(); return; }
    if (pcat == C_AZ) pletter = null;
    pcat = pcat == C_VERB || pcat == C_LINK ? 2 : pcat == C_SEEN ? 2 : pcat;   // then a noun
    bool nounNext = Has(VERB, it) || Has(LINK, it);
    for (int i = 0; i < NCATS; i++) { pcur[i] = nounNext ? 1 : 0; pscroll[i] = 0; }   // after a verb: the first noun; after a noun: <ENTER>
  }

  int Key(int k, int w) { wait = w; return k; }
  int Demo() {
    demoing = true;
    if (wait > 0) { wait--; return 0; }
    if (!picking) {
      if (demoAt >= DEMO.Length || vm.state == ZMachine.QUIT) return 0;
      if (readT == 0) { readT = 1; wait = More() ? 70 : 75; return 0; }   // read the page, then A: next page
      readT = 0; return Key(Buttons.A, 5);                                // or, at the end, the picker
    }
    string[] want = DEMO[demoAt];
    string goal = ncmd >= want.Length ? ENTER : want[ncmd];
    int here = IndexOf(pcat, goal);
    if (here >= 0) { int c = pcur[pcat]; return here < c ? Key(Buttons.Up, 5) : here > c ? Key(Buttons.Down, 5) : Key(Buttons.A, 10); }
    for (int k = 1; k < NCATS; k++)                                         // nearest category that has it
      for (int s = 0; s < 2; s++) {
        int d = s == 0 ? k : -k, c = (pcat + d + NCATS * 2) % NCATS;
        if (c != C_AZ && IndexOf(c, goal) >= 0) return Key(d > 0 ? Buttons.Right : Buttons.Left, 5);
      }
    if (pcat != C_AZ) return Key((C_AZ - pcat + NCATS) % NCATS <= NCATS / 2 ? Buttons.Right : Buttons.Left, 5);   // last resort: the dictionary
    if (pletter == null) {
      int i = IndexOf(C_AZ, goal.Substring(0, 1)), c = pcur[C_AZ];
      return i < c ? Key(Buttons.Up, 5) : i > c ? Key(Buttons.Down, 5) : Key(Buttons.A, 5);
    }
    return Key(Buttons.B, 5);
  }

  // ---- drawing (zork_core.js's own 3x5 font: the VMX font's glyphs where they are the same, the rest drawn here) --
  // the JS font's glyphs that differ from core/surf.c's (bits: 3 per row, top row first), -1 = the VMX font's is the same
  static readonly int[] MASK = MakeMasks();
  static int[] MakeMasks() {
    int[] k = new int[128];
    for (int c = 0; c < 128; c++) k[c] = c < 32 || c == 127 || (c >= 'a' && c <= 'z') ? 25218 : -1;   // JS: '?'
    k[' '] = 0; k['"'] = 23040; k['#'] = 24445; k['&'] = 10923; k['\''] = 9216; k['('] = 5265; k[')'] = 17556;
    k[','] = 20; k[';'] = 1044; k['='] = 3640; k['['] = 13459; k[']'] = 25750; k['_'] = 7;
    return k;
  }
  void Glyph(Screen s, int x, int y, int c, bool on) {
    int g = c >= 128 ? 25218 : MASK[c];
    if (g == 0) return;
    if (g < 0) {
      string str = CH[c]; if (str == null) { str = ((char)c).ToString(); CH[c] = str; }
      s.Text(x, y, str, on); return;
    }
    for (int r = 0; r < 5; r++) for (int q = 0; q < 3; q++) if (((g >> (14 - r * 3 - q)) & 1) != 0) s.Set(x + q, y + r, on);
  }
  void Str(Screen s, int x, int y, string str, int start, int n, bool on, bool upper) {
    for (int i = 0; i < n; i++, x += 4) { int c = str[start + i]; if (upper && c >= 'a' && c <= 'z') c -= 32; Glyph(s, x, y, c, on); }
  }
  void Txt(Screen s, int x, int y, string str, bool on) { Str(s, x, y, str, 0, str.Length, on, false); }
  void Chars(Screen s, int x, int y, char[] c, int start, int n, bool on) { for (int i = 0; i < n; i++, x += 4) Glyph(s, x, y, c[start + i], on); }
  static void Bar(Screen s, int y, int h) { s.Fill(0, y, W, h, true); }
  static readonly int[] ARROW = { 0, 0, 1, 0, 2, 0, 3, 0, 4, 0, 1, 1, 2, 1, 3, 1, 2, 2 };

  public override void Draw(Screen s) {
    s.Clear();
    Bar(s, 0, 7);                                   // room bar
    string room = vm.hasStatus && vm.statusRoom.Length > 0 ? vm.statusRoom : "ZORK I";
    int rl = room.Length, r0 = 0;
    if (rl > COLS) { int k = (t / 8) % (rl - COLS + 8); r0 = Math.Max(0, Math.Min(k - 4, rl - COLS)); }
    int vis = Math.Min(rl - r0, COLS);
    Str(s, Math.Max(0, (W - vis * 4 + 1) >> 1), 1, room, r0, vis, false, true);
    for (int i = 0; i < W; i += 2) s.Set(i, 57, true);
    bool blink = ((t >> 3) & 1) != 0;
    if (!picking) {
      for (int r = 0; r < ROWS; r++) {
        int i = top + r; if (i >= ln) continue;
        int o = LineAt(i), n = llen[o / COLS];
        for (int j = 0; j < n; j++) Glyph(s, j * 4, TY + r * LH, lch[o + j], true);
      }
      if (More()) { if (blink) for (int i = 0; i < ARROW.Length; i += 2) s.Set(59 + ARROW[i], 59 + ARROW[i + 1], true); Txt(s, 0, 59, "MORE", true); }
      else if (vm.state == ZMachine.QUIT) Txt(s, 0, 59, "THE END", true);
      else { Txt(s, 0, 59, ">", true); if (blink) Txt(s, 4, 59, "_", true); }
      return;
    }
    int count = ItemCount(pcat);
    Txt(s, 0, TY, "<", true); Txt(s, 60, TY, ">", true);
    if (pcat == C_AZ && pletter != null) { int x = (W - 6 * 4 + 1) >> 1; Txt(s, x, TY, "A-Z: ", true); Txt(s, x + 20, TY, pletter, true); }
    else Txt(s, (W - CATS[pcat].Length * 4 + 1) >> 1, TY, CATS[pcat], true);
    for (int r = 0; r < PICK_ROWS; r++) {
      int k = pscroll[pcat] + r; if (k < 0 || k >= count) break;
      int y = TY + 7 + r * LH; bool sel = k == pcur[pcat];
      if (sel) Bar(s, y - 1, 7);
      int kk = ncmd > 0 ? k - 1 : k;
      if (ncmd > 0 && k == 0) Txt(s, 2, y, ENTER, !sel);
      else if (AzWords(pcat)) { int n = AzText(azStart[pletter[0] - 'A'] + kk); Chars(s, 2, y, wb, 0, Math.Min(n, 15), !sel); }
      else { string it = BaseStr(pcat, kk); Str(s, 2, y, it, 0, Math.Min(it.Length, 15), !sel, false); }
    }
    if (count == 0) Txt(s, 2, TY + 7, "(NOTHING YET)", true);
    if (pscroll[pcat] > 0) { s.Set(62, TY + 7, true); s.Set(61, TY + 8, true); s.Set(63, TY + 8, true); }
    if (pscroll[pcat] + PICK_ROWS < count) {
      s.Set(62, TY + 7 + PICK_ROWS * LH - 2, true); s.Set(61, TY + 6 + PICK_ROWS * LH - 2, true); s.Set(63, TY + 6 + PICK_ROWS * LH - 2, true);
    }
    int cn = 0;                                     // "> " + cmd.join(" ") + blinking "_", its last 16 characters
    cn = Put(cn, "> ");
    for (int i = 0; i < ncmd; i++) { if (i > 0) cn = Put(cn, " "); if (cmd[i] != null) cn = Put(cn, cmd[i]); }
    if (blink) cn = Put(cn, "_");
    int c0 = cn > COLS ? cn - COLS : 0;
    Chars(s, 0, 59, cbuf, c0, cn - c0, true);
  }
  int Put(int n, string str) {
    if (n + str.Length > cbuf.Length) { char[] nb = new char[(n + str.Length) * 2]; Array.Copy(cbuf, 0, nb, 0, n); cbuf = nb; }
    for (int i = 0; i < str.Length; i++) cbuf[n++] = str[i];
    return n;
  }
}
