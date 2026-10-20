// The beeper: square-wave notes (the simulator's tone() and chime()) and the score call's sound (DTMF digits, then
// Bell 202 data three times), mixed into the platform's sample stream by vmx_audio().
#ifndef SOUND_H
#define SOUND_H
#include <stdint.h>
void snd_tone(float f, float t0, float dur, float vol, float slideTo);   // flat, then a quick fade (tone())
void snd_chime(float f, float t0, float dur, float vol);                 // bell-like decay (chime())
void snd_mute(int m);
float snd_dial(const char *digits);                 // starts the call's dialling; returns its length in seconds
float snd_send(const uint8_t *msg, int len);        // starts the data (3 copies); returns its length
void snd_hangup(void);
#endif
