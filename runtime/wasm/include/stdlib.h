#ifndef _STDLIB_H
#define _STDLIB_H
#include <stddef.h>
int abs(int x);
void *malloc(size_t n);
void *realloc(void *p, size_t n);
void free(void *p);
#endif
