// The VMX's storage on the CoreS3: a FATX memory-unit volume (../../core/fatx.h, vmxvol.h) in the flash partition
// that held LittleFS ("spiffs" in default_16MB.csv, 3.4 MB; the real VMX's 8 MB flash leaves it about as much), and
// that volume as a USB memory unit (TinyUSB mass storage on the S3's own USB, the CoreS3's USB-C): plugged into an
// Xbox controller (through an adapter until the VMX's board exists) or a PC, the host gets the volume and the VMX
// shows MEMORY UNIT (vmx_link) until the host ejects it, the cable comes out or B is pressed. If the host wrote
// anything the VMX restarts after, so everything it keeps in memory (games, records, settings) is read again.
// First start after LittleFS: its games and saves are moved onto the new volume (and the NVS records).
#include <Arduino.h>
#include <USB.h>
#include <USBMSC.h>
#include <LittleFS.h>
#include <Preferences.h>
#include <esp_partition.h>
#include "../../core/vmxfw.h"
#include "../../core/vmxvol.h"
#include "mu.h"

static const esp_partition_t *part;
static Fatx vol;
static SemaphoreHandle_t lock;

// ---- the flash under it: 512-byte sectors over 4 KB erase blocks, one block cached and written back once
static uint8_t blk[4096]; static int32_t blkNo = -1; static bool blkDirty;
static uint32_t lastIoMs;
static bool blkFlush() {
  if (!blkDirty) return true;
  bool ok = esp_partition_erase_range(part, (size_t)blkNo * 4096, 4096) == ESP_OK && esp_partition_write(part, (size_t)blkNo * 4096, blk, 4096) == ESP_OK;
  blkDirty = false; return ok;
}
static bool blkLoad(int32_t b) {
  if (b == blkNo) return true;
  if (!blkFlush()) return false;
  blkNo = b; return esp_partition_read(part, (size_t)b * 4096, blk, 4096) == ESP_OK;
}
static int rdSec(void *, uint32_t s, int n, uint8_t *buf) {
  for (int i = 0; i < n; i++, s++, buf += 512) {
    if ((int32_t)(s >> 3) == blkNo) memcpy(buf, blk + (s & 7) * 512, 512);
    else if (esp_partition_read(part, (size_t)s * 512, buf, 512) != ESP_OK) return 0;
  }
  return 1;
}
static int wrSec(void *, uint32_t s, int n, const uint8_t *buf) {
  for (int i = 0; i < n; i++, s++, buf += 512) {
    if (!blkLoad((int32_t)(s >> 3))) return 0;
    uint8_t *p = blk + (s & 7) * 512;
    if (memcmp(p, buf, 512)) { memcpy(p, buf, 512); blkDirty = true; }
  }
  return 1;
}

// ---- USB
static USBMSC msc;
static volatile bool hostUp, hostWrote, hostEject, declined;
static bool linkedNow;
static int32_t onRead(uint32_t lba, uint32_t off, void *buf, uint32_t n) {
  if (!linkedNow) return -1;
  xSemaphoreTake(lock, portMAX_DELAY);
  int ok = (off % 512 == 0 && n % 512 == 0) ? rdSec(NULL, lba + off / 512, (int)(n / 512), (uint8_t *)buf) : 0;
  lastIoMs = millis(); xSemaphoreGive(lock);
  return ok ? (int32_t)n : -1;
}
static int32_t onWrite(uint32_t lba, uint32_t off, uint8_t *buf, uint32_t n) {
  if (!linkedNow) return -1;
  xSemaphoreTake(lock, portMAX_DELAY);
  int ok = (off % 512 == 0 && n % 512 == 0) ? wrSec(NULL, lba + off / 512, (int)(n / 512), buf) : 0;
  hostWrote = true; lastIoMs = millis(); xSemaphoreGive(lock);
  return ok ? (int32_t)n : -1;
}
static bool onStartStop(uint8_t, bool start, bool loadEject) { if (loadEject && !start) hostEject = true; return true; }
static void onUsb(void *, esp_event_base_t base, int32_t id, void *) {
  if (base != ARDUINO_USB_EVENTS) return;
  if (id == ARDUINO_USB_STARTED_EVENT) hostUp = true;
  if (id == ARDUINO_USB_STOPPED_EVENT) { hostUp = false; declined = false; }
}

static void endLink() {                               // let go of the host; restart if it changed the volume
  msc.mediaPresent(false);
  xSemaphoreTake(lock, portMAX_DELAY); linkedNow = false; blkFlush(); xSemaphoreGive(lock);
  if (hostWrote) { Serial.println("memory unit: the host wrote, restarting"); delay(100); ESP.restart(); }
  vmx_link(0);
}
extern "C" void plat_link_eject(void) { declined = true; endLink(); }

// ---- moving LittleFS's games and saves over (first start of this firmware)
static void migrate(Preferences &prefs) {
  struct Item { String path; uint8_t *d; size_t n; bool game; };
  Item items[48]; int n = 0;
  if (LittleFS.begin(false)) {
    const char *dirs[2] = { "/games", "/saves" };
    for (int k = 0; k < 2; k++) {
      File d = LittleFS.open(dirs[k]); if (!d) continue;
      for (File f = d.openNextFile(); f && n < 48; f = d.openNextFile()) {
        size_t len = f.size(); uint8_t *b = (uint8_t *)ps_malloc(len ? len : 1);
        if (b && f.read(b, len) == len) items[n++] = { String(f.name()), b, len, k == 0 };
      }
    }
    LittleFS.end();
  }
  if (!fatx_format(&vol, (uint32_t)esp_random()) || !vol_open(&vol)) { Serial.println("memory unit: format failed"); return; }
  for (int i = 0; i < n; i++) {
    if (items[i].game) { uint32_t id; char t[24]; if (vol_install_vxp(&vol, items[i].d, (int)items[i].n, &id, t)) Serial.printf("moved game %s\n", t); }
    else { String k = items[i].path; int dot = k.lastIndexOf('.'); if (dot > 0) k = k.substring(0, dot); vol_save(&vol, k.c_str(), items[i].d, (int)items[i].n); }
    free(items[i].d);
  }
  const char *keys[3] = { "settings", "recs", "casino" };               // the firmware's own saves were in NVS
  static uint8_t b[4096];
  for (int i = 0; i < 3; i++) if (prefs.isKey(keys[i])) { size_t len = prefs.getBytes(keys[i], b, sizeof b); vol_save(&vol, keys[i], b, (int)len); }
  blkFlush();
  Serial.printf("memory unit: made (%d files moved)\n", n);
}

Fatx *mu_vol() { return &vol; }
bool mu_begin(Preferences &prefs) {
  lock = xSemaphoreCreateMutex();
  part = esp_partition_find_first(ESP_PARTITION_TYPE_DATA, ESP_PARTITION_SUBTYPE_ANY, "spiffs");
  if (!part) { Serial.println("memory unit: no partition"); return false; }
  vol = (Fatx){ .read = rdSec, .write = wrSec, .ctx = NULL, .sectors = part->size / 512 };
  vol.stamp = fatx_stamp(2002, 11, 15, 12, 0, 0);
  if (!fatx_mount(&vol)) migrate(prefs);
  if (!vol_open(&vol)) { Serial.println("memory unit: can't open"); return false; }
  blkFlush();
  Serial.printf("memory unit: %u KB, %u KB free\n", (unsigned)(part->size / 1024), (unsigned)(fatx_free_bytes(&vol) / 1024));
  msc.vendorID("VMX"); msc.productID("VMX MEMORY UNIT"); msc.productRevision("1.0");
  msc.onRead(onRead); msc.onWrite(onWrite); msc.onStartStop(onStartStop);
  msc.mediaPresent(false);
  msc.begin(part->size / 512, 512);
  USB.onEvent(onUsb);
  USB.productName("VMX"); USB.manufacturerName("VMX");
  USB.begin();
  return true;
}
void mu_flush() { xSemaphoreTake(lock, portMAX_DELAY); blkFlush(); xSemaphoreGive(lock); }
void mu_loop() {
  if (hostUp && !linkedNow && !declined) {            // a host: become its memory unit
    mu_flush(); hostWrote = hostEject = false; vmx_link(1);
    xSemaphoreTake(lock, portMAX_DELAY); linkedNow = true; xSemaphoreGive(lock);
    msc.mediaPresent(true); Serial.println("memory unit: linked");
  }
  if (!linkedNow) return;
  if (millis() - lastIoMs < 400) vmx_link_busy();
  else if (blkDirty) mu_flush();                      // the host paused: put the block down
  if (hostEject || !hostUp) { Serial.println(hostEject ? "memory unit: ejected" : "memory unit: unplugged"); if (hostEject) declined = true; endLink(); }
}
