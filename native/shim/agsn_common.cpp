// ags4mac: shared helpers and library-level functions for libagsnative.
#include <allegro.h>
#include <cerrno>
#include <cstdlib>
#include <cstring>
#include <mutex>
#include "agsn_common.h"
#include "ac/def_version.h"

using namespace AGS::Common;

namespace
{
    thread_local std::string g_lastError;
}

namespace agsn
{
    std::recursive_mutex &ApiMutex()
    {
        static std::recursive_mutex mutex;
        return mutex;
    }

    void EnsureInitialized()
    {
        static std::once_flag once;
        std::call_once(once, []()
        {
            // Same as initialize_native()/setup_color_conversions() in the upstream editor's
            // AGS.Native (agsnative.cpp): pixel data in memory is B,G,R(,A) for 15/16/24/32-bit.
            set_uformat(U_UTF8);
            install_allegro(SYSTEM_NONE, &errno, atexit);
            set_rgb_shifts(10, 5, 0, 11, 5, 0, 16, 8, 0, 16, 8, 0, 24);
        });
    }

    int Fail(int code, const std::string &message)
    {
        g_lastError = message;
        return code;
    }

    void ClearError()
    {
        g_lastError.clear();
    }

    BitmapData NativeLayout(const uint8_t *pixels, size_t stride, int width, int height,
                            PixelFormat fmt, PixelBuffer &holder)
    {
        if (stride == GetStrideForPixelFormat(fmt, width))
            return BitmapData(pixels, stride * static_cast<size_t>(height), stride, width, height, fmt);
        holder = PixelBuffer(width, height, fmt);
        const size_t rowBytes = static_cast<size_t>(width) * PixelFormatToPixelBits(fmt) / 8;
        for (int y = 0; y < height; ++y)
            std::memcpy(holder.GetLine(y), pixels + y * stride, rowBytes);
        return BitmapData(holder.GetData(), holder.GetDataSize(), holder.GetStride(), width, height, fmt);
    }

    PixelFormat ToAgsFormat(int32_t format)
    {
        switch (format)
        {
        case AGSN_PXFMT_INDEXED8: return kPxFmt_Indexed8;
        case AGSN_PXFMT_R5G5B5: return kPxFmt_R5G5B5;
        case AGSN_PXFMT_R5G6B5: return kPxFmt_R5G6B5;
        case AGSN_PXFMT_R8G8B8: return kPxFmt_R8G8B8;
        case AGSN_PXFMT_A8R8G8B8: return kPxFmt_A8R8G8B8;
        default: return kPxFmt_Undefined;
        }
    }

    int32_t FromAgsFormat(PixelFormat format)
    {
        switch (format)
        {
        case kPxFmt_Indexed1:
        case kPxFmt_Indexed4:
        case kPxFmt_Indexed8: return AGSN_PXFMT_INDEXED8;
        case kPxFmt_R5G5B5: return AGSN_PXFMT_R5G5B5;
        case kPxFmt_R5G6B5: return AGSN_PXFMT_R5G6B5;
        case kPxFmt_R8G8B8: return AGSN_PXFMT_R8G8B8;
        case kPxFmt_A8R8G8B8: return AGSN_PXFMT_A8R8G8B8;
        default: return AGSN_PXFMT_UNDEFINED;
        }
    }

    int BytesPerPixel(int32_t format)
    {
        switch (format)
        {
        case AGSN_PXFMT_INDEXED8: return 1;
        case AGSN_PXFMT_R5G5B5:
        case AGSN_PXFMT_R5G6B5: return 2;
        case AGSN_PXFMT_R8G8B8: return 3;
        case AGSN_PXFMT_A8R8G8B8: return 4;
        default: return 0;
        }
    }
}

extern "C" {

int agsn_initialize(void)
{
    agsn::ApiLock lock;
    agsn::EnsureInitialized();
    return AGSN_OK;
}

int agsn_api_version(void)
{
    agsn::ApiLock lock;
    return AGSN_API_VERSION;
}

const char *agsn_ags_version(void)
{
    agsn::ApiLock lock;
    return ACI_VERSION_STR;
}

const char *agsn_last_error(void)
{
    agsn::ApiLock lock;
    return g_lastError.c_str();
}

} // extern "C"
