// ags4mac: room background import/delete for libagsnative.
//
// Port of the upstream editor's ImportBackground()/DeleteBackground() (AGS.Native/agsnative.cpp) with the
// conversion steps of CreateBlockFromBitmap(), Convert8BitToHiColor() and remap_background(), working on
// images loaded by upstream's own image loaders instead of System.Drawing bitmaps. The padding of small
// images is RoomSettingsEditor.ExtendBitmapIfSmallerThanScreen().
#include <allegro.h>
#include <algorithm>
#include <cstring>
#include <memory>
#include "agsn_common.h"
#include "agsn_room_internal.h"
#include "ac/gamestructdefines.h"
#include "game/roomdata.h"
#include "game/room_file.h"
#include "gfx/bitmap.h"
#include "gfx/image_file.h"
#include "util/file.h"
#include "util/path.h"
#include "util/stream.h"

using namespace AGS::Common;


namespace
{
    PixelBuffer LoadImageFile(const char *path, PALETTE pal)
    {
        String ext = Path::GetFileExtension(path).Lower();
        std::unique_ptr<Stream> in = File::OpenFileRead(path);
        if (!in)
            return {};
        PixelFormat fmt = kPxFmt_Undefined;
        if (ext == "bmp") return ImageFile::LoadBMP(in.get(), &fmt, pal);
        if (ext == "pcx") return ImageFile::LoadPCX(in.get(), &fmt, pal);
        if (ext == "png") return ImageFile::LoadPNG(in.get(), &fmt, pal);
        return {};
    }

    // ExtendBitmapIfSmallerThanScreen: pad with black (palette index 0) up to the game's resolution.
    PixelBuffer ExtendToMinimum(PixelBuffer &&src, int min_width, int min_height)
    {
        if (src.GetWidth() >= min_width && src.GetHeight() >= min_height)
            return std::move(src);
        const int w = std::max(src.GetWidth(), min_width), h = std::max(src.GetHeight(), min_height);
        PixelBuffer dst(w, h, src.GetFormat());
        std::memset(dst.GetData(), 0, dst.GetDataSize());
        if (src.GetFormat() == kPxFmt_A8R8G8B8)
        {
            // Graphics.Clear(Color.Black) on an ARGB bitmap: opaque black
            for (int y = 0; y < h; ++y)
            {
                uint32_t *row = reinterpret_cast<uint32_t*>(dst.GetLine(y));
                std::fill(row, row + w, 0xFF000000u);
            }
        }
        const size_t row_bytes = std::min(src.GetStride(), dst.GetStride());
        for (int y = 0; y < src.GetHeight(); ++y)
            std::memcpy(dst.GetLine(y), src.GetLine(y), row_bytes);
        return dst;
    }

    // Convert8BitToHiColor with keep_transparency = false (backgrounds are opaque).
    void Convert8BitToHiColor(const Bitmap *src, const RGB *imgpal, Bitmap *dst)
    {
        const int dst_depth = dst->GetColorDepth();
        for (int x = 0; x < src->GetWidth(); ++x)
        {
            for (int y = 0; y < src->GetHeight(); ++y)
            {
                const RGB pal_color = imgpal[src->GetPixel(x, y)];
                dst->PutPixel(x, y, makecol_depth(dst_depth, pal_color.r, pal_color.g, pal_color.b));
            }
        }
    }

    // remap_background(): `palette` is the global (game) palette in Allegro 6-bit values.
    void RemapBackground(Bitmap *scene, const RGB *oldpale, RGB *bgpal, bool exact_pal,
                         const uint8_t *paluses, const RGB *palette, int &colours_lost)
    {
        if (exact_pal)
        {
            for (int a = 0; a < 256; a++)
                if (paluses[a] == PAL_BACKGROUND)
                    bgpal[a] = oldpale[a];
            return;
        }

        RGB pal[256];
        std::copy(palette, palette + 256, pal);
        int numbgslots = 0;
        for (int a = 0; a < 256; a++)
            if (paluses[a] != PAL_GAMEWIDE) numbgslots++;
        int imgpalcnt[256] = {};
        for (int y = 0; y < scene->GetHeight(); y++)
            for (int x = 0; x < scene->GetWidth(); x++)
                imgpalcnt[scene->GetScanLine(y)[x]]++;
        int numclr = 0, bb;
        RGB tpal[256];
        for (int a = 0; a < 256; a++)
        {
            if (paluses[a] == PAL_BACKGROUND)
                PaletteOp::SetRGB(pal, a, 0, 0, 0); // black out the bg slots before starting
            if ((oldpale[a].r == 0) & (oldpale[a].g == 0) & (oldpale[a].b == 0))
            {
                imgpalcnt[a] = 0;
                continue;
            }
            for (bb = 0; bb < numclr; bb++)
            {
                if ((oldpale[a].r == tpal[bb].r) & (oldpale[a].g == tpal[bb].g) & (oldpale[a].b == tpal[bb].b))
                    bb = 1000;
            }
            if (bb > 300)
            {
                imgpalcnt[a] = 0;
                continue;
            }
            if (imgpalcnt[a] == 0)
                continue;
            tpal[numclr] = oldpale[a];
            numclr++;
        }
        colours_lost = std::max(0, numclr - numbgslots);

        int palslt = 255;
        for (int a = 0; a < numclr; a++)
        {
            while (paluses[palslt] != PAL_BACKGROUND)
            {
                palslt--;
                if (palslt < 0) break;
            }
            if (palslt < 0) break;
            pal[palslt] = tpal[a];
            palslt--;
            if (palslt < 0) break;
        }
        for (int a = 0; a < 256; a++)
        {
            if (paluses[a] == PAL_GAMEWIDE)
            {
                tpal[a].r = 0; tpal[a].g = 0; tpal[a].b = 0;
            }
            else
                tpal[a] = pal[a];
        }
        BitmapData bm_data = scene->GetBitmapData();
        PaletteOp::Remap(bm_data, oldpale, tpal, false);
        // upstream remaps into the global palette, which is the frame's palette (bg_frame.Palette)
        std::copy(pal, pal + 256, bgpal);
    }

    PixelBuffer ToPixelBuffer(const Bitmap *bmp)
    {
        PixelBuffer buf(bmp->GetWidth(), bmp->GetHeight(), ColorDepthToPixelFormat(bmp->GetColorDepth()));
        for (int y = 0; y < bmp->GetHeight(); ++y)
            std::memcpy(buf.GetLine(y), bmp->GetScanLine(y), static_cast<size_t>(bmp->GetWidth()) * bmp->GetBPP());
        return buf;
    }

    PixelBuffer ClearMask(int width, int height)
    {
        PixelBuffer buf(std::max(1, width), std::max(1, height), kPxFmt_Indexed8);
        std::memset(buf.GetData(), 0, buf.GetDataSize());
        return buf;
    }
}

extern "C" {

int agsn_room_import_background(agsn_room *room, int32_t frame, const char *image_path,
                                int32_t game_color_depth, int32_t min_width, int32_t min_height,
                                const agsn_rgba *game_palette, const uint8_t *paluses,
                                int32_t use_exact_palette, int32_t *colours_lost)
{
    agsn::ApiLock lock;
    agsn::EnsureInitialized();
    agsn::ClearError();
    if (colours_lost) *colours_lost = 0;
    if (!room || !image_path || (game_color_depth != 8 && game_color_depth != 16 && game_color_depth != 32))
        return agsn::Fail(AGSN_E_INVALID_ARG, "invalid arguments");
    RoomData &rd = room->data;
    if (frame < 0 || frame >= MAX_ROOM_BGFRAMES || static_cast<uint32_t>(frame) > rd.BgFrameCount)
        return agsn::Fail(AGSN_E_INVALID_ARG, "Invalid background number " + std::to_string(frame));
    if (game_color_depth == 8 && (!game_palette || !paluses))
        return agsn::Fail(AGSN_E_INVALID_ARG, "8-bit games need the game palette");

    PALETTE filepal = {};
    PixelBuffer pixels = LoadImageFile(image_path, filepal);
    if (!pixels)
        return agsn::Fail(AGSN_E_FORMAT, std::string("unable to load image data from: ") + image_path);
    pixels = ExtendToMinimum(std::move(pixels), min_width, min_height);

    // CreateBlockFromBitmap(bmp, oldpale, nullptr, fixColourDepth = true, importAlpha = false,
    //                       keepTransparency = false, fixPalette = false)
    int src_depth = PixelFormatToPixelBits(pixels.GetFormat());
    if (src_depth < 8)
    {
        PixelBuffer converted;
        if (!PixelOp::CopyConvert(pixels, converted, kPxFmt_Indexed8))
            return agsn::Fail(AGSN_E_UNSUPPORTED, "unsupported pixel format");
        pixels = std::move(converted);
        src_depth = 8;
    }
    if (game_color_depth == 8 && src_depth > 8)
        return agsn::Fail(AGSN_E_UNSUPPORTED, "You cannot import a hi-colour or true-colour image into a 256-colour game.");

    RGB oldpale[256];
    for (int i = 0; i < 256; i++)
    {
        if (game_color_depth == 8) // allegro palette is 0-63
            oldpale[i] = { static_cast<unsigned char>(filepal[i].r / 4), static_cast<unsigned char>(filepal[i].g / 4),
                           static_cast<unsigned char>(filepal[i].b / 4), 255 };
        else
            oldpale[i] = filepal[i];
    }

    const int width = pixels.GetWidth(), height = pixels.GetHeight();
    std::unique_ptr<Bitmap> newbg(BitmapHelper::CreateBitmap(std::move(pixels)));
    if (!newbg)
        return agsn::Fail(AGSN_E_FORMAT, "Failed to convert bitmap to the native format");
    if (newbg->GetColorDepth() != game_color_depth)
    {
        std::unique_ptr<Bitmap> at_depth(BitmapHelper::CreateBitmap(width, height, game_color_depth));
        if (newbg->GetColorDepth() == 8)
        {
            Convert8BitToHiColor(newbg.get(), oldpale, at_depth.get());
        }
        else
        {
            const int old_conv = get_color_conversion();
            set_color_conversion(old_conv & ~COLORCONV_KEEP_TRANS);
            at_depth->Blit(newbg.get(), 0, 0, 0, 0, width, height);
            set_color_conversion(old_conv);
        }
        newbg = std::move(at_depth);
    }

    // theRoom->Width/Height come from the managed room, which the editor sets to the image size.
    const bool size_changed = (rd.Width != width) || (rd.Height != height);
    const bool resolution_changed = frame == 0 && rd.GetLegacyResolution() != kRoomResolution_Real;
    if (frame > 0 && size_changed)
        return agsn::Fail(AGSN_E_INVALID_ARG, "All backgrounds for the room must be of the same size.");
    rd.Width = width;
    rd.Height = height;
    if (frame == 0)
        rd.SetLegacyResolution(kRoomResolution_Real);
    if (rd.MaskResolution < 1)
        rd.MaskResolution = 1;

    if (rd.BgFrames.size() < static_cast<size_t>(frame) + 1)
        rd.BgFrames.resize(frame + 1);
    rd.BgFrameCount = std::max<uint32_t>(rd.BgFrameCount, static_cast<uint32_t>(frame) + 1);
    RoomBgFrame &bg_frame = rd.BgFrames[frame];

    if (newbg->GetColorDepth() == 8)
    {
        RGB palette[256];
        for (int i = 0; i < 256; i++)
            palette[i] = { static_cast<unsigned char>(game_palette[i].r / 4), static_cast<unsigned char>(game_palette[i].g / 4),
                           static_cast<unsigned char>(game_palette[i].b / 4), 0 };
        for (int aa = 0; aa < 256; aa++)
            if (paluses[aa] == PAL_LOCKED)
                bg_frame.Palette[aa] = palette[aa];
        bg_frame.IsPaletteShared = 0;
        int lost = 0;
        RemapBackground(newbg.get(), oldpale, bg_frame.Palette, use_exact_palette != 0, paluses, palette, lost);
        if (colours_lost) *colours_lost = lost;
    }
    bg_frame.GraphicBuf = ToPixelBuffer(newbg.get());
    if (frame == 0)
        rd.BackgroundBPP = newbg->GetBPP();

    // if size or resolution has changed, reset masks
    if (!rd.WalkBehindMaskBuf || size_changed || rd.WalkBehindMaskBuf.GetWidth() != width ||
        rd.WalkBehindMaskBuf.GetHeight() != height || resolution_changed)
    {
        rd.WalkAreaMaskBuf = ClearMask(width / rd.MaskResolution, height / rd.MaskResolution);
        rd.HotspotMaskBuf = ClearMask(width / rd.MaskResolution, height / rd.MaskResolution);
        rd.WalkBehindMaskBuf = ClearMask(width, height);
        rd.RegionMaskBuf = ClearMask(width / rd.MaskResolution, height / rd.MaskResolution);
    }
    return AGSN_OK;
}

agsn_room *agsn_room_create(int32_t width, int32_t height, int32_t mask_resolution, int32_t color_depth,
                            int32_t background_count)
{
    agsn::ApiLock lock;
    agsn::EnsureInitialized();
    agsn::ClearError();
    if (width <= 0 || height <= 0 || mask_resolution < 1 || background_count < 1 || background_count > MAX_ROOM_BGFRAMES ||
        (color_depth != 8 && color_depth != 16 && color_depth != 32))
    {
        agsn::Fail(AGSN_E_INVALID_ARG, "invalid room size, mask resolution, colour depth or background count");
        return nullptr;
    }
    // save_default_crm_file(): clear backgrounds and masks
    std::unique_ptr<agsn_room> room(new agsn_room());
    RoomData &rd = room->data;
    rd.Width = width;
    rd.Height = height;
    rd.MaskResolution = mask_resolution;
    rd.BackgroundBPP = color_depth / 8;
    rd.BgFrameCount = static_cast<uint32_t>(background_count);
    rd.BgFrames.resize(background_count);
    for (RoomBgFrame &frame : rd.BgFrames)
    {
        frame.GraphicBuf = PixelBuffer(width, height, ColorDepthToPixelFormat(color_depth));
        std::memset(frame.GraphicBuf.GetData(), 0, frame.GraphicBuf.GetDataSize());
    }
    rd.WalkAreaMaskBuf = ClearMask(width / mask_resolution, height / mask_resolution);
    rd.HotspotMaskBuf = ClearMask(width / mask_resolution, height / mask_resolution);
    rd.RegionMaskBuf = ClearMask(width / mask_resolution, height / mask_resolution);
    rd.WalkBehindMaskBuf = ClearMask(width, height);
    return room.release();
}

int agsn_room_delete_background(agsn_room *room, int32_t frame)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    if (!room)
        return agsn::Fail(AGSN_E_INVALID_ARG, "room is null");
    RoomData &rd = room->data;
    if (frame < 0 || static_cast<uint32_t>(frame) >= rd.BgFrameCount || frame >= static_cast<int32_t>(rd.BgFrames.size()))
        return AGSN_OK; // upstream ignores invalid numbers
    rd.BgFrameCount--;
    rd.BgFrames.erase(rd.BgFrames.begin() + frame);
    return AGSN_OK;
}

int agsn_room_extract_script_text(const char *room_path, char *buffer, size_t buffer_size, size_t *length, int32_t *found)
{
    agsn::ApiLock lock;
    agsn::EnsureInitialized();
    agsn::ClearError();
    if (!room_path || !length || !found)
        return agsn::Fail(AGSN_E_INVALID_ARG, "invalid arguments");
    *length = 0;
    *found = 0;
    // load_room_script() of the upstream editor
    String scriptText;
    RoomDataSource src;
    HRoomFileError err = OpenRoomFile(room_path, src);
    if (err)
    {
        err = ExtractScriptText(scriptText, std::move(src.InputStream), src.DataVersion);
        if (err.HasError() && err->Code() == kRoomFileErr_BlockNotFound)
            return AGSN_OK; // simply did not find the script text
    }
    if (!err)
        return agsn::Fail(AGSN_E_FORMAT, std::string("Unable to load room script source from '") + room_path +
            "', error was:\n" + err->FullMessage().GetCStr());
    *found = 1;
    *length = scriptText.GetLength();
    if (!buffer || buffer_size < scriptText.GetLength())
        return buffer ? AGSN_E_BUFFER_TOO_SMALL : AGSN_OK;
    std::memcpy(buffer, scriptText.GetCStr(), scriptText.GetLength());
    return AGSN_OK;
}

} // extern "C"
