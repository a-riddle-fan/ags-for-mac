// ags4mac: shared helpers for the libagsnative shim (internal).
#ifndef AGSN_COMMON_H
#define AGSN_COMMON_H

#include <allegro.h>
#include <string>
#include "agsnative.h"
#include "gfx/bitmapdata.h"

// Image handle shared by image files and sprite files.
struct agsn_image
{
    AGS::Common::PixelBuffer pixels;
    AGS::Common::PixelFormat sourceFormat = AGS::Common::kPxFmt_Undefined;
    int sourceBits = 0;
    PALETTE palette = {};
};

#include <mutex>

namespace agsn
{
    // Upstream's native code is not thread-safe (e.g. the LZW codec uses global buffers), so every
    // exported function holds this process-wide lock. Recursive, as API functions may call each other.
    std::recursive_mutex &ApiMutex();
    struct ApiLock
    {
        std::lock_guard<std::recursive_mutex> guard{ApiMutex()};
    };

    // Initializes Allegro and color conversions once (see agsn_initialize). Thread-safe.
    void EnsureInitialized();

    // Records an error for agsn_last_error() and returns code.
    int Fail(int code, const std::string &message);
    void ClearError();

    // Wraps caller pixel rows as BitmapData in AGS's native row layout. Several upstream writers
    // (SavePNG for 24-bit, sprite compression) assume tightly packed rows and ignore the stride, so
    // rows are copied into `holder` when the caller's stride differs from the native one.
    AGS::Common::BitmapData NativeLayout(const uint8_t *pixels, size_t stride, int width, int height,
                                         AGS::Common::PixelFormat fmt, AGS::Common::PixelBuffer &holder);

    // Maps between the public enum and AGS::Common::PixelFormat; undefined on unknown values.
    AGS::Common::PixelFormat ToAgsFormat(int32_t format);
    int32_t FromAgsFormat(AGS::Common::PixelFormat format);
    int BytesPerPixel(int32_t format);
}

#endif // AGSN_COMMON_H
