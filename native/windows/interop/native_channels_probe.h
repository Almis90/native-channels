#pragma once
#include <stdint.h>

// ABI v1: cdecl, fixed-width integers, caller-owned buffers, no retained pointers.
// Status: 0 success, 1 invalid argument, 2 signed addition overflow.
// All functions are synchronous and thread-safe. No exceptions cross the ABI.
#ifdef __cplusplus
#define NC_EXPORT extern "C" __declspec(dllexport)
#else
#define NC_EXPORT __declspec(dllexport)
#endif
NC_EXPORT uint32_t __cdecl nc_abi_version(void);
NC_EXPORT uint32_t __cdecl nc_language(void); // 1 = C++, 2 = Rust
NC_EXPORT uint32_t __cdecl nc_process_id(void);
NC_EXPORT int32_t __cdecl nc_add(int64_t a, int64_t b, int64_t* result);
// Input/output must be separate valid buffers of length/capacity bytes.
// Null pointers are allowed only when length is zero. Output is input XOR 0xa5.
NC_EXPORT int32_t __cdecl nc_transform(const uint8_t* input, uint32_t length,
    uint8_t* output, uint32_t capacity);
