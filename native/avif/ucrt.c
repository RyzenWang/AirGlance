/*
 * Minimal memory routines for the Windows ARM64 build.
 *
 * The ARM64 shim is linked with /NODEFAULTLIB against the UCRT import library
 * and compiler-rt. The UCRT import library does not provide memset/memcpy for
 * the ARM64 target, so the few routines the compiler and libavif/dav1d call are
 * defined here. Do not compile this file for the MSVC x64/MSVC ARM64 build.
 */
#include <stddef.h>

void * memset(void * destination, int value, size_t count)
{
    unsigned char * bytes = (unsigned char *)destination;
    while (count--)
    {
        *bytes++ = (unsigned char)value;
    }

    return destination;
}

void * memcpy(void * destination, const void * source, size_t count)
{
    unsigned char * to = (unsigned char *)destination;
    const unsigned char * from = (const unsigned char *)source;
    while (count--)
    {
        *to++ = *from++;
    }

    return destination;
}

void * memmove(void * destination, const void * source, size_t count)
{
    unsigned char * to = (unsigned char *)destination;
    const unsigned char * from = (const unsigned char *)source;
    if (to < from)
    {
        while (count--)
        {
            *to++ = *from++;
        }
    }
    else
    {
        to += count;
        from += count;
        while (count--)
        {
            *--to = *--from;
        }
    }

    return destination;
}

int memcmp(const void * left, const void * right, size_t count)
{
    const unsigned char * a = (const unsigned char *)left;
    const unsigned char * b = (const unsigned char *)right;
    while (count--)
    {
        if (*a != *b)
        {
            return (int)*a - (int)*b;
        }

        a++;
        b++;
    }

    return 0;
}
