// ags4mac: image load/save for libagsnative, using ags/Common/gfx/image_file.
//
// Calls the per-format LoadBMP/LoadPCX/LoadPNG and Save* functions directly rather than
// ImageFile::LoadImage/SaveImage: those convert the palette to Allegro's 6-bit range, which would
// lose precision. Here palettes stay 8 bits per channel.
#include <allegro.h>
#include <cstring>
#include <memory>
#include "agsn_common.h"
#include "gfx/image_file.h"
#include "util/file.h"
#include "util/path.h"
#include "util/stream.h"

using namespace AGS::Common;

namespace
{
    enum class FileKind { Unknown, Bmp, Pcx, Png };

    FileKind KindFromPath(const char *path)
    {
        String ext = Path::GetFileExtension(path).Lower();
        if (ext == "bmp") return FileKind::Bmp;
        if (ext == "pcx") return FileKind::Pcx;
        if (ext == "png") return FileKind::Png;
        return FileKind::Unknown;
    }

    int SourceBitsFor(PixelFormat fmt)
    {
        return PixelFormatToPixelBits(fmt);
    }
}

extern "C" {

agsn_image *agsn_image_load(const char *path)
{
    agsn::ApiLock lock;
    agsn::EnsureInitialized();
    agsn::ClearError();
    if (!path)
    {
        agsn::Fail(AGSN_E_INVALID_ARG, "path is null");
        return nullptr;
    }

    FileKind kind = KindFromPath(path);
    if (kind == FileKind::Unknown)
    {
        agsn::Fail(AGSN_E_UNSUPPORTED, std::string("unsupported image file type: ") + path);
        return nullptr;
    }

    std::unique_ptr<Stream> in = File::OpenFileRead(path);
    if (!in)
    {
        agsn::Fail(AGSN_E_IO, std::string("unable to open file: ") + path);
        return nullptr;
    }

    std::unique_ptr<agsn_image> image(new agsn_image());
    PixelFormat srcFmt = kPxFmt_Undefined;
    switch (kind)
    {
    case FileKind::Bmp: image->pixels = ImageFile::LoadBMP(in.get(), &srcFmt, image->palette); break;
    case FileKind::Pcx: image->pixels = ImageFile::LoadPCX(in.get(), &srcFmt, image->palette); break;
    case FileKind::Png: image->pixels = ImageFile::LoadPNG(in.get(), &srcFmt, image->palette); break;
    default: break;
    }

    if (!image->pixels)
    {
        agsn::Fail(AGSN_E_FORMAT, std::string("unable to load image data from: ") + path);
        return nullptr;
    }
    if (agsn::FromAgsFormat(image->pixels.GetFormat()) == AGSN_PXFMT_UNDEFINED ||
        PixelFormatToPixelBits(image->pixels.GetFormat()) < 8)
    {
        agsn::Fail(AGSN_E_UNSUPPORTED, std::string("unsupported pixel format in: ") + path);
        return nullptr;
    }

    image->sourceFormat = srcFmt != kPxFmt_Undefined ? srcFmt : image->pixels.GetFormat();
    image->sourceBits = SourceBitsFor(image->sourceFormat);
    return image.release();
}

void agsn_image_free(agsn_image *image)
{
    agsn::ApiLock lock;
    delete image;
}

int agsn_image_get_info(const agsn_image *image, agsn_image_info *info)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    if (!image || !info)
        return agsn::Fail(AGSN_E_INVALID_ARG, "image or info is null");
    info->width = image->pixels.GetWidth();
    info->height = image->pixels.GetHeight();
    info->format = agsn::FromAgsFormat(image->pixels.GetFormat());
    info->source_format = agsn::FromAgsFormat(image->sourceFormat);
    info->source_bits = image->sourceBits;
    return AGSN_OK;
}

int agsn_image_copy_pixels(const agsn_image *image, uint8_t *dst, size_t dst_size, size_t dst_stride)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    if (!image || !dst)
        return agsn::Fail(AGSN_E_INVALID_ARG, "image or dst is null");

    const int height = image->pixels.GetHeight();
    const size_t rowBytes = static_cast<size_t>(image->pixels.GetWidth()) *
        agsn::BytesPerPixel(agsn::FromAgsFormat(image->pixels.GetFormat()));
    if (dst_stride < rowBytes)
        return agsn::Fail(AGSN_E_INVALID_ARG, "dst_stride is smaller than a pixel row");
    if (dst_size < dst_stride * static_cast<size_t>(height))
        return agsn::Fail(AGSN_E_BUFFER_TOO_SMALL, "dst buffer is too small");

    const uint8_t *src = image->pixels.GetData();
    const size_t srcStride = image->pixels.GetStride();
    for (int y = 0; y < height; ++y)
        std::memcpy(dst + y * dst_stride, src + y * srcStride, rowBytes);
    return AGSN_OK;
}

int agsn_image_copy_palette(const agsn_image *image, agsn_rgba *palette256)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    if (!image || !palette256)
        return agsn::Fail(AGSN_E_INVALID_ARG, "image or palette is null");
    for (int i = 0; i < PAL_SIZE; ++i)
    {
        palette256[i].r = image->palette[i].r;
        palette256[i].g = image->palette[i].g;
        palette256[i].b = image->palette[i].b;
        palette256[i].a = image->palette[i].a;
    }
    return AGSN_OK;
}

int agsn_image_save(const char *path, int32_t width, int32_t height, int32_t format,
                    const uint8_t *pixels, size_t src_stride,
                    const agsn_rgba *palette256, int32_t skip_alpha)
{
    agsn::ApiLock lock;
    agsn::EnsureInitialized();
    agsn::ClearError();
    if (!path || !pixels || width <= 0 || height <= 0)
        return agsn::Fail(AGSN_E_INVALID_ARG, "invalid path, pixels or size");
    PixelFormat fmt = agsn::ToAgsFormat(format);
    if (fmt == kPxFmt_Undefined)
        return agsn::Fail(AGSN_E_INVALID_ARG, "invalid pixel format");
    if (fmt == kPxFmt_Indexed8 && !palette256)
        return agsn::Fail(AGSN_E_INVALID_ARG, "indexed images need a palette");
    const size_t rowBytes = static_cast<size_t>(width) * agsn::BytesPerPixel(format);
    if (src_stride < rowBytes)
        return agsn::Fail(AGSN_E_INVALID_ARG, "src_stride is smaller than a pixel row");

    FileKind kind = KindFromPath(path);
    if (kind == FileKind::Unknown)
        return agsn::Fail(AGSN_E_UNSUPPORTED, std::string("unsupported image file type: ") + path);

    PALETTE pal = {};
    if (palette256)
    {
        for (int i = 0; i < PAL_SIZE; ++i)
        {
            pal[i].r = palette256[i].r;
            pal[i].g = palette256[i].g;
            pal[i].b = palette256[i].b;
            pal[i].a = palette256[i].a;
        }
    }

    PixelBuffer packed;
    BitmapData data = agsn::NativeLayout(pixels, src_stride, width, height, fmt, packed);

    std::unique_ptr<Stream> out = File::CreateFile(path);
    if (!out)
        return agsn::Fail(AGSN_E_IO, std::string("unable to create file: ") + path);

    bool ok = false;
    switch (kind)
    {
    case FileKind::Bmp: ok = ImageFile::SaveBMP(data, skip_alpha != 0, pal, out.get()); break;
    case FileKind::Pcx: ok = ImageFile::SavePCX(data, skip_alpha != 0, pal, out.get()); break;
    case FileKind::Png: ok = ImageFile::SavePNG(data, skip_alpha != 0, pal, out.get()); break;
    default: break;
    }
    if (!ok)
        return agsn::Fail(AGSN_E_FORMAT, std::string("unable to write image: ") + path);
    return AGSN_OK;
}

} // extern "C"
