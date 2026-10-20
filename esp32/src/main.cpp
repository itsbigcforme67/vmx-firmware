// VMX firmware on the M5Stack CoreS3 + Faces Bottom3 + Gamepad3. The firmware is ../core (vmxfw.h); this file is
// the hardware: the 64x64 screen drawn 3x in the middle of the CoreS3's panel, the pad, the speaker as the beeper,
// saves in flash (NVS), the clock for score dates.
//   Gamepad3: D-pad, A, B as the VMX's; SELECT or START = the VMX's side button (short = back, hold = power).
//   Without the pad: touch the screen's left half for the D-pad (by where you touch), right top = B, right bottom = A.
#include <M5Unified.h>
#include <Preferences.h>
#include <esp_heap_caps.h>
#include "../../core/vmxfw.h"

// ------------------------------------------------------------------ platform calls for the core
static Preferences prefs;
extern "C" void *plat_alloc(size_t n) {
  void *p = heap_caps_calloc(1, n, MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT);
  if (!p) p = calloc(1, n);
  if (!p) Serial.printf("plat_alloc(%u) failed\n", (unsigned)n);
  return p;
}
extern "C" int plat_load(const char *key, void *buf, int max) {
  if (!prefs.isKey(key)) return 0;
  return (int)prefs.getBytes(key, buf, (size_t)max);
}
extern "C" void plat_save(const char *key, const void *buf, int n) { prefs.putBytes(key, buf, (size_t)n); }
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
  vmx_init();
  Serial.printf("VMX firmware: %d games, PSRAM free %u\n", VMX_NGAMES, (unsigned)ESP.getFreePsram());
}

// serial test hook: u d l r a b s = tap that button (s = side), S = hold side 1 s, p = print the screen as text
static int serialHeld = 0; static uint32_t serialUntil = 0;
static void serialHook() {
  while (Serial.available()) {
    int c = Serial.read(), b = 0;
    switch (c) { case 'u': b = B_UP; break; case 'd': b = B_DOWN; break; case 'l': b = B_LEFT; break; case 'r': b = B_RIGHT; break;
      case 'a': b = B_A; break; case 'b': b = B_B; break; case 's': case 'S': b = B_SIDE; break;
      case 'p': { const uint8_t *q = vmx_screen(); for (int y = 0; y < 64; y += 2) { char line[65];
          for (int x = 0; x < 64; x++) line[x] = q[y * 64 + x] || q[(y + 1) * 64 + x] ? '#' : '.'; line[64] = 0; Serial.println(line); }
        Serial.println("--"); } break; }
    if (b) { serialHeld = b; serialUntil = millis() + (c == 'S' ? 1000 : 80); }
  }
  if (serialHeld && millis() > serialUntil) serialHeld = 0;
}

void loop() {
  M5.update();
  serialHook();
  vmx_frame(millis(), readPad() | serialHeld);
  drawScreen();
  pumpAudio();
  delay(5);
}
