// the browser build's setjmp: clang lowers setjmp/longjmp to WebAssembly exceptions (-mllvm -wasm-enable-sjlj);
// libc.c has the helpers it calls
#ifndef _SETJMP_H
#define _SETJMP_H
typedef long jmp_buf[8];
int setjmp(jmp_buf env);
_Noreturn void longjmp(jmp_buf env, int val);
#endif
