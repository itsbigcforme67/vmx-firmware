// VMX firmware on the M5Stack CoreS3 + Faces Bottom3 + Gamepad3. The firmware is ../core (vmxfw.h); this file is
// the hardware: the 64x64 screen drawn 3x in the middle of the CoreS3's panel, the pad, the speaker as the beeper,
// the clock for score dates, and the VMX's storage: a memory-unit volume in flash, which is also a USB memory unit
// (mu.cpp; games, saves, settings and records all live on it).
//   Gamepad3: D-pad, A, B as the VMX's; SELECT or START = the VMX's side button (short = back, hold = power).
//   Without the pad: touch the screen's left half for the D-pad (by where you touch), right top = B, right bottom = A.
#include <M5Unified.h>
#include <Preferences.h>
#include <esp_heap_caps.h>
#include <WiFi.h>
#include <WiFiManager.h>
#include <ArduinoOTA.h>
#include <WebServer.h>
#include <StreamString.h>
#include "../../core/vmxfw.h"
#include "games_builtin.h"
#include "mu.h"
#include "../../core/vmxvol.h"
extern "C" uint32_t (*vmx_micros)(void);
extern "C" void vmx_stats_text(char *b, int n);
extern "C" volatile uint16_t vx_curop, vx_curm;
extern "C" const char *vmx_method_name(int m);
static uint32_t profM[1024];
// sampling profiler on core 0: which IL op is running (hook letters 'P' start/stop+print)
static volatile bool profOn = false; static uint32_t profHist[0x200];
static void profTask(void *) { for (;;) { if (profOn) { profHist[vx_curop & 0x1FF]++; if (vx_curop != 0x1FF) profM[vx_curm & 1023]++; vTaskDelay(1); } else vTaskDelay(10); } }

// ------------------------------------------------------------------ platform calls for the core
static Preferences prefs;
extern "C" void *plat_alloc(size_t n) {
  void *p = heap_caps_calloc(1, n, MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT);
  if (!p) p = calloc(1, n);
  if (!p) Serial.printf("plat_alloc(%u) failed\n", (unsigned)n);
  return p;
}
extern "C" void *plat_alloc_fast(size_t n) {
  if (heap_caps_get_free_size(MALLOC_CAP_INTERNAL) < n + 50 * 1024 || heap_caps_get_largest_free_block(MALLOC_CAP_INTERNAL | MALLOC_CAP_8BIT) < n) return NULL;   // leave Wi-Fi 50 KB
  return heap_caps_calloc(1, n, MALLOC_CAP_INTERNAL | MALLOC_CAP_8BIT);
}
// everything the firmware keeps: files on the memory-unit volume (56580001\VMX\<key>.BIN; core/vmxvol.h)
extern "C" int plat_load(const char *key, void *buf, int max) { return vmx_linked() ? 0 : vol_load(mu_vol(), key, buf, max); }
extern "C" void plat_save(const char *key, const void *buf, int n) { if (!vmx_linked()) { vol_save(mu_vol(), key, buf, n); mu_flush(); } }
extern "C" int plat_day(void) {
  auto dt = M5.Rtc.getDateTime();
  if (dt.date.year < 2001 || dt.date.year > 2100) return -1;
  struct tm t = {}; t.tm_year = dt.date.year - 1900; t.tm_mon = dt.date.month - 1; t.tm_mday = dt.date.date; t.tm_hour = 12;
  struct tm e = {}; e.tm_year = 101; e.tm_mon = 10; e.tm_mday = 15; e.tm_hour = 12;
  return (int)((mktime(&t) - mktime(&e)) / 86400);
}
extern "C" const char *plat_name(void) { return "CORES3 RIG"; }

// ------------------------------------------------------------------ the pad (Gamepad3 on the Faces bottom, I2C 0x08)
enum : uint8_t { P_UP = 0x01, P_DOWN = 0x02, P_LEFT = 0x04, P_RIGHT = 0x08, P_A = 0x10, P_B = 0x20, P_SELECT = 0x40, P_START = 0x80 };
static const uint8_t GP_ADDR = 0x08;
static bool gpPresent = false;
static uint8_t gpLast = 0xFF, gpState = 0;
static bool gpRead(uint8_t &raw) { return M5.In_I2C.readRegister(GP_ADDR, 0x00, &raw, 1, 400000); }
static void gpInit() {
  uint8_t raw = 0xFF;
  gpPresent = gpRead(raw);
  if (gpPresent) gpLast = raw;                     // the key byte is an event: start from the first value (as PocketStation)
  Serial.printf("Gamepad3 %s (first byte %02x)\n", gpPresent ? "found" : "not found - touch controls", raw);
}
static int readPad() {
  uint8_t raw;
  if (gpPresent && gpRead(raw) && raw != gpLast) { gpLast = raw; gpState = (uint8_t)~raw; }
  int h = 0;
  if (gpState & P_UP) h |= B_UP;
  if (gpState & P_DOWN) h |= B_DOWN;
  if (gpState & P_LEFT) h |= B_LEFT;
  if (gpState & P_RIGHT) h |= B_RIGHT;
  if (gpState & P_A) h |= B_A;
  if (gpState & P_B) h |= B_B;
  if (gpState & (P_SELECT | P_START)) h |= B_SIDE;
  // touch: left of the screen = D-pad by direction from its centre, right top = B, right bottom = A, the strip at
  // the very top = side button
  for (int i = 0, n = M5.Touch.getCount(); i < n; i++) {
    auto t = M5.Touch.getDetail(i);
    if (!t.isPressed()) continue;
    if (t.y < 20) h |= B_SIDE;
    else if (t.x < 64) { int dx = t.x - 32, dy = t.y - 120; if (abs(dx) > abs(dy)) h |= dx < 0 ? B_LEFT : B_RIGHT; else h |= dy < 0 ? B_UP : B_DOWN; }
    else if (t.x >= 256) h |= t.y < 120 ? B_B : B_A;
  }
  return h;
}

// ------------------------------------------------------------------ the screen
static const int SC = 3, LW = 64 * SC, LX = (320 - LW) / 2, LY = (240 - LW) / 2;
static M5Canvas lcd(&M5.Display);
static uint8_t shown[64 * 64];
static int shownBright = -1, shownOn = -1;
static const uint16_t CASE = 0x18E3;              // the VMX's matte black (#1b1d1b)
static uint16_t onColour(int b) {
  static const uint8_t C[3][3] = { {62, 128, 29}, {94, 192, 44}, {125, 255, 58} };
  return M5.Display.color565(C[b][0], C[b][1], C[b][2]);
}
static void drawFrame() {
  M5.Display.fillScreen(CASE);
  M5.Display.fillRoundRect(LX - 10, LY - 10, LW + 20, LW + 20, 10, 0x0000);   // the screen's black surround
  M5.Display.drawRoundRect(LX - 11, LY - 11, LW + 22, LW + 22, 11, 0x3186);
  if (!gpPresent) {
    M5.Display.setTextColor(0x8C51, CASE); M5.Display.setTextDatum(middle_center); M5.Display.setFont(&fonts::Font2);
    M5.Display.drawString("D", 32, 120); M5.Display.drawString("B", 288, 60); M5.Display.drawString("A", 288, 180);
  }
}
static void drawScreen() {
  const uint8_t *s = vmx_screen();
  int b = vmx_bright(), on = vmx_on();
  if (b == shownBright && on == shownOn && !memcmp(s, shown, sizeof shown)) return;
  uint16_t ink = onColour(b), off = M5.Display.color565(10, 16, 8);
  lcd.fillScreen(off);
  if (on) for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) if (s[y * 64 + x]) lcd.fillRect(x * SC, y * SC, SC, SC, ink);
  lcd.pushSprite(&M5.Display, LX, LY);
  memcpy(shown, s, sizeof shown); shownBright = b; shownOn = on;
}

// ------------------------------------------------------------------ the beeper (CoreS3 speaker)
static int16_t chunks[4][256];
static int chunk = 0;
static void pumpAudio() {
  while (M5.Speaker.isPlaying(0) < 2) {
    vmx_audio(chunks[chunk], 256);
    M5.Speaker.playRaw(chunks[chunk], 256, VMX_RATE, false, 1, 0);
    chunk = (chunk + 1) & 3;
  }
}

static void loadGames();
static void netLoop();
void setup() {
  auto cfg = M5.config();
  cfg.serial_baudrate = 115200;
  cfg.internal_spk = true;
  cfg.internal_mic = false;
  M5.begin(cfg);
  M5.Display.setRotation(1);
  prefs.begin("vmx", false);
  lcd.setColorDepth(16);
  if (!lcd.createSprite(LW, LW)) Serial.println("no memory for the screen sprite");
  gpInit();
  M5.Speaker.begin();
  M5.Speaker.setVolume(140);
  drawFrame();
  mu_begin(prefs);
  loadGames();
  WiFi.mode(WIFI_STA); WiFi.setHostname("vmx"); WiFi.begin();        // the network saved by Wi-Fi setup, if any
  xTaskCreatePinnedToCore(profTask, "prof", 2048, NULL, 1, NULL, 0);
  vmx_micros = []() -> uint32_t { return (uint32_t)micros(); };
  vmx_init();
  Serial.printf("VMX firmware: %d games, PSRAM free %u, internal free %u (largest block %u)\n", VMX_NGAMES, (unsigned)ESP.getFreePsram(), (unsigned)heap_caps_get_free_size(MALLOC_CAP_INTERNAL), (unsigned)heap_caps_get_largest_free_block(MALLOC_CAP_INTERNAL));
}

// serial test hook: u d l r a b s = tap that button (s = side), S = hold side 1 s, p = print the screen as text
static int serialHeld = 0; static uint32_t serialUntil = 0;
static void hookCommand(int c, Print &out);
static void serialHook() { while (Serial.available()) hookCommand(Serial.read(), Serial); if (serialHeld && millis() > serialUntil) serialHeld = 0; }
static void hookCommand(int c, Print &out) {
  {
    int b = 0;
    switch (c) { case 'u': b = B_UP; break; case 'd': b = B_DOWN; break; case 'l': b = B_LEFT; break; case 'r': b = B_RIGHT; break;
      case 'a': b = B_A; break; case 'b': b = B_B; break; case 's': case 'S': b = B_SIDE; break;
      case 'i': { char b[200]; vmx_stats_text(b, sizeof b); out.println(b); } break;
      case 'P':
        if (!profOn) { memset(profHist, 0, sizeof profHist); memset(profM, 0, sizeof profM); profOn = true; out.println("profiling"); }
        else { profOn = false; uint32_t tot = 0; for (int i = 0; i < 0x200; i++) tot += profHist[i];
          for (int k = 0; k < 14; k++) { int bi = 0; for (int i = 0; i < 0x200; i++) if (profHist[i] > profHist[bi]) bi = i;
            if (!profHist[bi]) break; out.printf("op %03X %5.1f%%\n", bi, 100.0 * profHist[bi] / (tot ? tot : 1)); profHist[bi] = 0; }
          for (int k = 0; k < 10; k++) { int bi = 0; for (int i = 0; i < 1024; i++) if (profM[i] > profM[bi]) bi = i;
            if (!profM[bi]) break; out.printf("m %5.1f%% %s\n", 100.0 * profM[bi] / (tot ? tot : 1), vmx_method_name(bi)); profM[bi] = 0; } }
        break;
      case 'm': out.printf("internal free %u largest %u, PSRAM free %u\n", (unsigned)heap_caps_get_free_size(MALLOC_CAP_INTERNAL),
                           (unsigned)heap_caps_get_largest_free_block(MALLOC_CAP_INTERNAL), (unsigned)ESP.getFreePsram()); break;
      case 'p': { const uint8_t *q = vmx_screen(); for (int y = 0; y < 64; y += 2) { char line[65];
          for (int x = 0; x < 64; x++) line[x] = q[y * 64 + x] || q[(y + 1) * 64 + x] ? '#' : '.'; line[64] = 0; out.println(line); }
        out.println("--"); } break; }
    if (b) { serialHeld = b; serialUntil = millis() + (c == 'S' ? 1000 : 80); }
  }
}

// ------------------------------------------------------------------ Wi-Fi: setup hotspot, firmware over the air, game uploads
// SETTINGS > WIFI > A opens a hotspot "VMX-SETUP": join it with a phone, pick your network, type its password there
// (the VMX keeps it). Once on the network: firmware updates with `pio run -e ota -t upload` (ArduinoOTA, vmx.local),
// and games with tools/push_game.sh (HTTP POST to /game, same password).
static WiFiManager wm;
static WebServer web(80);
static bool portal = false, netUp = false, otaBusy = false;
static uint8_t *upBuf; static size_t upLen; static bool upOk = false; static const size_t UPMAX = 512 * 1024;
static void otaScreen(int pct) {
  M5.Display.fillRect(60, 214, 200, 22, 0x18E3);
  M5.Display.setTextColor(0xFFFF, 0x18E3); M5.Display.setTextDatum(middle_center); M5.Display.setFont(&fonts::Font2);
  char b[32]; snprintf(b, sizeof b, pct < 0 ? "UPDATING..." : "UPDATING %d%%", pct); M5.Display.drawString(b, 160, 225);
}
static bool authed() {
  if (web.header("X-VMX-Key") == OTA_PASSWORD) return true;
  web.send(403, "text/plain", "wrong key\n"); return false;
}
static void netStart() {                         // once connected: OTA + the little web server
  ArduinoOTA.setHostname("vmx"); ArduinoOTA.setPassword(OTA_PASSWORD);
  ArduinoOTA.onStart([]() { otaBusy = true; M5.Speaker.stop(); otaScreen(-1); });
  ArduinoOTA.onProgress([](unsigned a, unsigned b) { static int last = -1; int p = (int)(a * 100ull / (b ? b : 1)); if (p / 5 != last) { last = p / 5; otaScreen(p); } });
  ArduinoOTA.onError([](ota_error_t e) { otaBusy = false; Serial.printf("OTA error %d\n", (int)e); });
  ArduinoOTA.begin();
  const char *hdrs[] = { "X-VMX-Key" }; web.collectHeaders(hdrs, 1);
  web.on("/", HTTP_GET, []() {
    String s = "VMX (CoreS3 rig)\ngames on the memory unit:\n";
    uint32_t ids[32]; int n = vmx_linked() ? 0 : vol_games(mu_vol(), ids, 32);
    for (int i = 0; i < n; i++) { char p[40]; vol_game_path(ids[i], p); s += String("  ") + String(ids[i], HEX) + " " + fatx_size(mu_vol(), p) + "\n"; }
    s += String(vmx_linked() ? "(linked to a USB host)\n" : "") + (fatx_free_bytes(mu_vol()) / 1024) + " KB free\nbuilt in:\n";
    for (int i = 0; i < NBUILTIN; i++) s += String("  ") + BUILTIN[i].name + "\n";
    web.send(200, "text/plain", s);
  });
  // POST /game (multipart "file" = a GAME.VXP): installed on the memory unit by its ID; the VMX restarts to load it
  web.on("/game", HTTP_POST, []() {
    if (!authed()) return;
    web.send(upOk ? 200 : 400, "text/plain", upOk ? "ok, restarting\n" : "upload failed\n");
    if (upOk) { delay(200); ESP.restart(); }
  }, []() {
    HTTPUpload &u = web.upload();
    if (u.status == UPLOAD_FILE_START) { upOk = false; upLen = 0; if (web.header("X-VMX-Key") != OTA_PASSWORD || vmx_linked()) return; if (!upBuf) upBuf = (uint8_t *)ps_malloc(UPMAX); }
    else if (u.status == UPLOAD_FILE_WRITE) { if (upBuf && upLen + u.currentSize <= UPMAX) { memcpy(upBuf + upLen, u.buf, u.currentSize); upLen += u.currentSize; } else upLen = UPMAX + 1; }
    else if (u.status == UPLOAD_FILE_END && upBuf && upLen <= UPMAX) {
      uint32_t id; char t[24]; upOk = vol_install_vxp(mu_vol(), upBuf, (int)upLen, &id, t); mu_flush();
      if (upOk) Serial.printf("installed %08X %s\n", (unsigned)id, t);
    }
  });
  web.on("/game", HTTP_DELETE, []() {               // DELETE /game?id=56581001
    if (!authed()) return;
    uint32_t id = strtoul(web.arg("id").c_str(), NULL, 16); bool ok = id && !vmx_linked() && vol_remove(mu_vol(), id); mu_flush();
    web.send(ok ? 200 : 404, "text/plain", ok ? "deleted, restarting\n" : "no such game\n"); if (ok) { delay(200); ESP.restart(); }
  });
  web.on("/hook", HTTP_POST, []() {                    // the serial test hook over Wi-Fi: c = letters (tools/drive.py)
    if (!authed()) return;
    String cmd = web.arg("c"); StreamString out;
    for (unsigned i = 0; i < cmd.length(); i++) {
      hookCommand(cmd[i], out);
      if (serialHeld) { uint32_t until = serialUntil; while ((int32_t)(millis() - until) < 0) { M5.update(); vmx_frame(millis(), readPad() | serialHeld); drawScreen(); pumpAudio(); delay(5); } serialHeld = 0;
        for (int k = 0; k < 6; k++) { vmx_frame(millis(), readPad()); drawScreen(); pumpAudio(); delay(10); } }
    }
    web.send(200, "text/plain", out);
  });
  web.on("/reboot", HTTP_POST, []() { if (!authed()) return; web.send(200, "text/plain", "restarting\n"); delay(200); ESP.restart(); });
  web.begin();
  Serial.printf("Wi-Fi up: %s (vmx.local)\n", WiFi.localIP().toString().c_str());
}
static void netLoop() {
  if (portal) { wm.process(); if (WiFi.status() == WL_CONNECTED) { portal = false; wm.stopConfigPortal(); } }
  bool up = WiFi.status() == WL_CONNECTED;
  if (up && !netUp) netStart();
  netUp = up;
  if (netUp) { ArduinoOTA.handle(); web.handleClient(); }
}
extern "C" int plat_wifi(char L[4][16]) {
  if (portal) { snprintf(L[0], 16, "JOIN HOTSPOT"); snprintf(L[1], 16, "VMX-SETUP"); snprintf(L[2], 16, "THEN PICK YOUR"); snprintf(L[3], 16, "NETWORK"); return 4; }
  if (WiFi.status() == WL_CONNECTED) {
    snprintf(L[0], 16, "CONNECTED"); snprintf(L[1], 16, "%.15s", WiFi.SSID().c_str()); snprintf(L[2], 16, "%s", WiFi.localIP().toString().c_str());
    snprintf(L[3], 16, "VMX.LOCAL"); return 4;
  }
  if (WiFi.SSID().length()) { snprintf(L[0], 16, "CONNECTING TO"); snprintf(L[1], 16, "%.15s", WiFi.SSID().c_str()); return 2; }
  snprintf(L[0], 16, "NOT SET UP"); return 1;
}
extern "C" void plat_wifi_setup(void) {
  if (portal) return;
  wm.setConfigPortalBlocking(false); wm.setConfigPortalTimeout(600); wm.setTitle("VMX");
  wm.startConfigPortal("VMX-SETUP"); portal = true;
}

// games: built-in packages, then the ones installed on the memory unit, kept in PSRAM for the session
static void loadGames() {
  for (int i = 0; i < NBUILTIN; i++) vmx_add_game(BUILTIN[i].data, BUILTIN[i].len);
  uint32_t ids[32]; int n = vol_games(mu_vol(), ids, 32);
  for (int i = 0; i < n; i++) {
    char p[40]; vol_game_path(ids[i], p); int len = fatx_size(mu_vol(), p); uint8_t *buf = len > 0 ? (uint8_t *)plat_alloc((size_t)len) : NULL;
    if (buf && fatx_read(mu_vol(), p, buf, len) == len && vmx_add_game(buf, (size_t)len) == 0) Serial.printf("game %08X (%d bytes)\n", (unsigned)ids[i], len);
    else Serial.printf("game %08X: not loaded\n", (unsigned)ids[i]);
  }
}


void loop() {
  netLoop();
  mu_loop();
  if (otaBusy) { delay(5); return; }
  M5.update();
  serialHook();
  vmx_frame(millis(), readPad() | serialHeld);
  drawScreen();
  pumpAudio();
  delay(5);
}
