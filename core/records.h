// Score records and the call's message, as vmx-world/mockup/modem.js (DESIGN.md "High scores", "Calling in").
#ifndef RECORDS_H
#define RECORDS_H
#include <stdint.h>
#define REC_LEN 20
typedef struct { uint32_t tid; uint8_t board, flags; uint16_t date; int32_t score; uint16_t f[3]; } ScoreRec;
uint16_t crc16(const uint8_t *b, int n);
void rec_pack(const ScoreRec *r, uint8_t *out);
int  msg_pack(uint32_t serial, const char *name, const ScoreRec *r, int n, uint8_t *out);   // returns its length
void check_code(const uint8_t *msg, int len, char out[5]);
const char *keypad(const char *s, char *out);       // "1-800-VMX-WRLD" -> "18008699753"
#endif
