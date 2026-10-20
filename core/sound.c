#include "sound.h"
#include "vmxfw.h"
#include <math.h>
#include <string.h>

#define AMP 26000.0f                     // full scale for a vol of 1 (the simulator's WebAudio gain)
typedef struct { uint32_t at, len; float f, f2, vol, ph; uint8_t decay; } Note;
static Note notes[24];
static uint32_t now;                     // samples played so far
static int muted;

void snd_mute(int m) { muted = m; }
static void add(float f, float t0, float dur, float vol, float slideTo, int decay) {
  if (muted) return;
  for (unsigned i = 0; i < sizeof notes / sizeof notes[0]; i++) if (!notes[i].len) {
    notes[i] = (Note){ now + (uint32_t)(t0 * VMX_RATE), (uint32_t)(dur * VMX_RATE), f, slideTo > 0 ? slideTo : f, vol, 0, (uint8_t)decay };
    return;
  }
}
void snd_tone(float f, float t0, float dur, float vol, float slideTo) { add(f, t0, dur, vol, slideTo, 0); }
void snd_chime(float f, float t0, float dur, float vol) { add(f, t0, dur, vol, 0, 1); }

// ---- the call: a stream worked out sample by sample (no big buffer)
enum { C_OFF, C_DIAL, C_DATA };
static struct { int mode; uint32_t at; char digits[24]; const uint8_t *msg; int len; float ph1, ph2; } call;
static const float ROW[4] = { 697, 770, 852, 941 }, COL[3] = { 1209, 1336, 1477 };
static const char *KEYS = "123456789*0#";
#define BAUD 1200
#define PRE_BITS 360                     // 0.3 s of marks
static int nbits(void) { return PRE_BITS + (2 + call.len) * 10 + 4; }
static float copy_len(void) { return (float)nbits() / BAUD + 0.05f; }
float snd_dial(const char *d) {
  memset(&call, 0, sizeof call); strncpy(call.digits, d, sizeof call.digits - 1);
  call.mode = C_DIAL; call.at = now;
  return 0.2f + 0.16f * (float)strlen(call.digits);
}
float snd_send(const uint8_t *msg, int len) {
  call.mode = C_DATA; call.at = now; call.msg = msg; call.len = len; call.ph1 = 0;
  return 0.2f + 3 * (copy_len() + 0.25f);
}
void snd_hangup(void) { call.mode = C_OFF; }
static int bit_at(int k) {               // bit k of one copy: marks, then 7E 7E msg framed, then marks
  if (k < PRE_BITS) return 1;
  k -= PRE_BITS; int byte = k / 10, b = k % 10;
  if (byte >= 2 + call.len) return 1;
  int v = byte < 2 ? 0x7E : call.msg[byte - 2];
  return b == 0 ? 0 : b == 9 ? 1 : (v >> (b - 1)) & 1;
}
static float sq(float ph) { return sinf(ph) >= 0 ? 1.0f : -1.0f; }
static float call_sample(void) {
  float t = (float)(now - call.at) / VMX_RATE;
  if (call.mode == C_DIAL) {
    if (t < 0.2f) return 0;
    int i = (int)((t - 0.2f) / 0.16f); float u = t - 0.2f - i * 0.16f;
    if (i >= (int)strlen(call.digits)) return 0;
    if (u >= 0.09f) return 0;
    const char *p = strchr(KEYS, call.digits[i]); if (!p) return 0;
    int k = (int)(p - KEYS);
    float n = u * VMX_RATE;
    return 0.25f * (sq(6.2831853f * ROW[k / 3] * n / VMX_RATE) + sq(6.2831853f * COL[k % 3] * n / VMX_RATE));
  }
  if (call.mode == C_DATA) {
    float s = t - 0.2f; if (s < 0) return 0;
    float per = copy_len() + 0.25f; int c = (int)(s / per); if (c >= 3) { call.mode = C_OFF; return 0; }
    float u = s - c * per; int k = (int)(u * BAUD);
    if (k >= nbits()) return 0;
    call.ph1 += 6.2831853f * (bit_at(k) ? 1200.0f : 2200.0f) / VMX_RATE;
    if (call.ph1 > 6.2831853f) call.ph1 -= 6.2831853f;
    return 0.5f * sq(call.ph1);
  }
  return 0;
}

void vmx_audio(int16_t *out, int n) {
  for (int i = 0; i < n; i++, now++) {
    float v = 0;
    for (unsigned k = 0; k < sizeof notes / sizeof notes[0]; k++) {
      Note *q = &notes[k]; if (!q->len || now < q->at) continue;
      uint32_t e = now - q->at; if (e >= q->len) { q->len = 0; continue; }
      float u = (float)e / q->len, f = q->f + (q->f2 - q->f) * u, g;
      if (q->decay) g = q->vol * powf(0.0008f / q->vol, u);
      else g = u < 0.85f ? q->vol : q->vol * (1 - u) / 0.15f;
      q->ph += f / VMX_RATE; if (q->ph >= 1) q->ph -= 1;
      v += q->ph < 0.5f ? g : -g;
    }
    v *= AMP;
    if (call.mode) v += call_sample() * 0.3f * AMP;    // the call is the data: it plays with SOUND off too
    out[i] = (int16_t)(v > 32767 ? 32767 : v < -32768 ? -32768 : v);
  }
}
