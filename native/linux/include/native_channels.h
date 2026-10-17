#pragma once
#include <stddef.h>
#include <stdint.h>

// Language-neutral ABI v1. Each library exports the same four entrypoints.
// Requests are serialized by the host. Replies may arrive on any thread,
// exactly once. All input pointers are borrowed for the call; reply bytes are
// borrowed for the callback. Payloads use StandardMessageCodec. status:
// 0=value, 1=error map, 2=not implemented. cancel must finish a pending
// request; destroy joins workers and finishes all requests before returning. No
// callback may occur after destroy returns. No exception crosses ABI.
#ifdef __cplusplus
extern "C" {
#endif
typedef void (*nc_reply)(uint64_t request, int status, const uint8_t *data,
                         size_t size);
void *nc_create(nc_reply reply);
void nc_request(void *module, uint64_t request, const char *channel,
                const char *method, const uint8_t *data, size_t size,
                const char *locale);
void nc_cancel(void *module, uint64_t request);
void nc_destroy(void *module);
#ifdef __cplusplus
}
#endif
