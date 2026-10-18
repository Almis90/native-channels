#pragma once
#include <stddef.h>
#include <stdint.h>

// Language-neutral ABI v1. Each library exports the same four entrypoints.
// Requests are serialized by the host. Replies may arrive on any thread,
// with exactly one terminal reply (streams may emit events first). All input
// pointers are borrowed for the call; reply bytes are borrowed for the
// callback. Payloads use StandardMessageCodec. status: 0=value/end, 1=error
// map, 2=not implemented, 3=nonterminal event for listen. Event streams end
// with 0 or 1. cancel must finish a pending request; destroy joins workers and
// finishes all requests before returning. No callback may occur after destroy
// returns. No exception crosses ABI.
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
// Optional native-view extension. View modules are created, called, polled and
// destroyed on the host UI thread. The parent is a borrowed X11 window ID;
// create returns an owned child window ID (zero on failure). destroy also
// destroys that child. Poll processes native toolkit events without waiting.
uintptr_t nc_view_create(void *module, uintptr_t parent, const char *identity);
// Configured variant accepts StandardMessageCodec creation arguments.
uintptr_t nc_view_create_configured(void *module, uintptr_t parent,
                                    const char *identity,
                                    const uint8_t *arguments, size_t size);
void nc_view_poll(void *module);
// Optional bidirectional extension: status 4 emits a StandardMessageCodec map
// with channel + method/arguments or channel + opaque payload bytes. Respond
// asynchronously with the same ID, status 0/value or 1/error map. nc_cancel
// invalidates pending callbacks; nc_destroy must discard them before returning.
void nc_response(void *module, uint64_t request, int status,
                 const uint8_t *data, size_t size);
#ifdef __cplusplus
}
#endif
