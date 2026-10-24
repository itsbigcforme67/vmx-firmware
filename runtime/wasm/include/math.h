// the browser build's maths: floor ceil sqrt fabs are WebAssembly instructions; the rest come from JavaScript's Math
#ifndef _MATH_H
#define _MATH_H
#define floor __builtin_floor
#define ceil __builtin_ceil
#define sqrt __builtin_sqrt
#define fabs __builtin_fabs
#define floorf __builtin_floorf
#define sqrtf __builtin_sqrtf
#define fabsf __builtin_fabsf
#define JSMATH(n) __attribute__((import_module("env"), import_name(n)))
JSMATH("sin") double sin(double); JSMATH("cos") double cos(double); JSMATH("atan2") double atan2(double, double);
JSMATH("pow") double pow(double, double);
JSMATH("fmod") double fmod(double, double);       // JavaScript % is the exact IEEE remainder fmod gives
long lroundf(float x);
float hypotf(float x, float y);
#endif
