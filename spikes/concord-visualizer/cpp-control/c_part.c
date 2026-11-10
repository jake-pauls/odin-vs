/* The same scalar types in a C translation unit: MSVC reports language C for these frames. */
#include <stdio.h>
#include <intrin.h>

struct pair { int a; int b; };

void c_locals(void) {
    float f = 1.5f;
    double d = 2.5;
    unsigned char byte = 200;
    long long big = 1234567890123LL;
    unsigned long long ubig = 9876543210ULL;
    struct pair p = { 3, 4 };
    int* pointer = &p.a;
    char* text = "c text";
    __debugbreak();
    printf("%f %f %d %lld %llu %d %d %s\n", f, d, byte, big, ubig, *pointer, p.b, text);
}
