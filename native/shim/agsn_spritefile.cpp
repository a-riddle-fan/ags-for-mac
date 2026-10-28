// ags4mac: sprite file reading for libagsnative, using ags/Common/ac/spritefile.
#include <memory>
#include <vector>
#include "agsn_common.h"
#include "ac/spritefile.h"
#include "util/file.h"

using namespace AGS::Common;

struct agsn_sprite_file
{
    SpriteFile file;
    std::vector<SpriteDatHeader> metrics;
};

struct agsn_sprite_writer
{
    std::unique_ptr<SpriteFileWriter> writer;
    bool finished = false;
};

extern "C" {

agsn_sprite_file *agsn_spritefile_open(const char *sprite_path, const char *index_path)
{
    agsn::ApiLock lock;
    agsn::EnsureInitialized();
    agsn::ClearError();
    if (!sprite_path)
    {
        agsn::Fail(AGSN_E_INVALID_ARG, "sprite_path is null");
        return nullptr;
    }

    std::unique_ptr<Stream> spriteStream = File::OpenFileRead(sprite_path);
    if (!spriteStream)
    {
        agsn::Fail(AGSN_E_IO, std::string("unable to open sprite file: ") + sprite_path);
        return nullptr;
    }
    std::unique_ptr<Stream> indexStream;
    if (index_path && File::IsFile(index_path))
        indexStream = File::OpenFileRead(index_path);

    std::unique_ptr<agsn_sprite_file> result(new agsn_sprite_file());
    HError err = result->file.OpenFile(std::move(spriteStream), std::move(indexStream), result->metrics);
    if (!err)
    {
        agsn::Fail(AGSN_E_FORMAT, std::string("unable to read sprite file ") + sprite_path + ": " + err->FullMessage().GetCStr());
        return nullptr;
    }
    return result.release();
}

void agsn_spritefile_close(agsn_sprite_file *file)
{
    agsn::ApiLock lock;
    delete file;
}

int32_t agsn_spritefile_get_topmost(const agsn_sprite_file *file)
{
    agsn::ApiLock lock;
    return file ? file->file.GetTopmostSprite() : -1;
}

int32_t agsn_spritefile_get_count(const agsn_sprite_file *file)
{
    agsn::ApiLock lock;
    return file ? static_cast<int32_t>(file->file.GetSpriteCount()) : 0;
}

int32_t agsn_spritefile_get_store_flags(const agsn_sprite_file *file)
{
    agsn::ApiLock lock;
    return file ? file->file.GetStoreFlags() : 0;
}

int32_t agsn_spritefile_get_compression(const agsn_sprite_file *file)
{
    agsn::ApiLock lock;
    return file ? static_cast<int32_t>(file->file.GetSpriteCompression()) : 0;
}

int agsn_spritefile_get_info(const agsn_sprite_file *file, int32_t index, agsn_sprite_info *info)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    if (!file || !info)
        return agsn::Fail(AGSN_E_INVALID_ARG, "file or info is null");
    *info = {};
    if (index < 0 || static_cast<size_t>(index) >= file->metrics.size())
        return AGSN_OK; // out of range: reported as not existing
    const SpriteDatHeader &hdr = file->metrics[index];
    info->exists = (hdr.Width > 0 && hdr.Height > 0) ? 1 : 0;
    info->width = hdr.Width;
    info->height = hdr.Height;
    info->bytes_per_pixel = hdr.BPP;
    return AGSN_OK;
}

agsn_image *agsn_spritefile_load(agsn_sprite_file *file, int32_t index)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    if (!file)
    {
        agsn::Fail(AGSN_E_INVALID_ARG, "file is null");
        return nullptr;
    }

    std::unique_ptr<agsn_image> image(new agsn_image());
    HError err = file->file.LoadSprite(index, image->pixels);
    if (!err)
    {
        agsn::Fail(AGSN_E_FORMAT, std::string("unable to load sprite: ") + err->FullMessage().GetCStr());
        return nullptr;
    }
    if (!image->pixels)
        return nullptr; // empty slot; no error
    if (agsn::FromAgsFormat(image->pixels.GetFormat()) == AGSN_PXFMT_UNDEFINED)
    {
        agsn::Fail(AGSN_E_UNSUPPORTED, "unsupported sprite pixel format");
        return nullptr;
    }
    image->sourceFormat = image->pixels.GetFormat();
    image->sourceBits = PixelFormatToPixelBits(image->sourceFormat);
    return image.release();
}

agsn_sprite_writer *agsn_spritewriter_create(const char *path, int32_t store_flags, int32_t compression)
{
    agsn::ApiLock lock;
    agsn::EnsureInitialized();
    agsn::ClearError();
    if (!path || compression < 0 || compression >= kNumSprCompressTypes)
    {
        agsn::Fail(AGSN_E_INVALID_ARG, "invalid path or compression");
        return nullptr;
    }
    std::unique_ptr<Stream> out = File::CreateFile(path);
    if (!out)
    {
        agsn::Fail(AGSN_E_IO, std::string("unable to create sprite file: ") + path);
        return nullptr;
    }
    std::unique_ptr<agsn_sprite_writer> result(new agsn_sprite_writer());
    result->writer.reset(new SpriteFileWriter(std::move(out)));
    result->writer->Begin(store_flags, static_cast<SpriteCompression>(compression));
    return result.release();
}

int agsn_spritewriter_write_image(agsn_sprite_writer *writer, int32_t width, int32_t height, int32_t format,
                                  const uint8_t *pixels, size_t stride)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    if (!writer || writer->finished || !pixels || width <= 0 || height <= 0)
        return agsn::Fail(AGSN_E_INVALID_ARG, "invalid writer or image");
    PixelFormat fmt = agsn::ToAgsFormat(format);
    if (fmt != kPxFmt_Indexed8 && fmt != kPxFmt_R5G6B5 && fmt != kPxFmt_A8R8G8B8)
        return agsn::Fail(AGSN_E_UNSUPPORTED, "sprites must be 8-bit indexed, R5G6B5 or A8R8G8B8");
    const size_t rowBytes = static_cast<size_t>(width) * agsn::BytesPerPixel(format);
    if (stride < rowBytes)
        return agsn::Fail(AGSN_E_INVALID_ARG, "stride is smaller than a pixel row");
    PixelBuffer packed;
    writer->writer->WriteBitmap(agsn::NativeLayout(pixels, stride, width, height, fmt, packed));
    return AGSN_OK;
}

int agsn_spritewriter_write_empty(agsn_sprite_writer *writer)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    if (!writer || writer->finished)
        return agsn::Fail(AGSN_E_INVALID_ARG, "invalid writer");
    writer->writer->WriteEmptySlot();
    return AGSN_OK;
}

int agsn_spritewriter_copy_from(agsn_sprite_writer *writer, agsn_sprite_file *source, int32_t index)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    if (!writer || writer->finished || !source)
        return agsn::Fail(AGSN_E_INVALID_ARG, "invalid writer or source");
    if (index < 0 || static_cast<size_t>(index) >= source->metrics.size() ||
        source->metrics[index].Width <= 0 || source->metrics[index].Height <= 0)
    {
        writer->writer->WriteEmptySlot();
        return AGSN_OK;
    }
    SpriteDatHeader hdr;
    std::vector<uint8_t> data;
    HError err = source->file.LoadRawData(index, hdr, data);
    if (!err)
        return agsn::Fail(AGSN_E_FORMAT, std::string("unable to read sprite: ") + err->FullMessage().GetCStr());
    writer->writer->WriteRawData(hdr, data);
    return AGSN_OK;
}

int agsn_spritewriter_finish(agsn_sprite_writer *writer, const char *index_path)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    if (!writer || writer->finished)
        return agsn::Fail(AGSN_E_INVALID_ARG, "invalid writer");
    writer->writer->Finalize();
    writer->finished = true;
    SpriteFileIndex index = writer->writer->GetIndex();
    writer->writer.reset(); // closes the file
    if (index_path)
    {
        HError err = SaveSpriteIndex(index_path, index);
        if (!err)
            return agsn::Fail(AGSN_E_IO, std::string("unable to write sprite index: ") + err->FullMessage().GetCStr());
    }
    return AGSN_OK;
}

void agsn_spritewriter_free(agsn_sprite_writer *writer)
{
    agsn::ApiLock lock;
    delete writer;
}

} // extern "C"
