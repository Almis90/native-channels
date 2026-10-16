#include "../native_channels_probe.h"
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <limits>

uint32_t __cdecl nc_abi_version() { return 1; }
uint32_t __cdecl nc_language() { return 1; }
uint32_t __cdecl nc_process_id() { return GetCurrentProcessId(); }

int32_t __cdecl nc_add(int64_t a, int64_t b, int64_t* result)
{
    if (result == nullptr) return 1;
    if ((b > 0 && a > (std::numeric_limits<int64_t>::max)() - b) ||
        (b < 0 && a < (std::numeric_limits<int64_t>::min)() - b)) return 2;
    *result = a + b;
    return 0;
}

int32_t __cdecl nc_transform(const uint8_t* input, uint32_t length,
    uint8_t* output, uint32_t capacity)
{
    if (capacity < length || (length != 0 && (input == nullptr || output == nullptr))) return 1;
    for (uint32_t i = 0; i < length; ++i) output[i] = input[i] ^ 0xa5;
    return 0;
}
