// ags4mac: room file access for libagsnative, using ags/Common/game (RoomData + room_file).
//
// Mirrors load_room_file()/convert_room_from_native() of the upstream editor (AGS.Native/agsnative.cpp):
// the room is read with LoadRoom(), which also upgrades old room versions. Converting to the managed
// Room model happens in AGS.Native.Interop, like convert_room_from_native did in C++/CLI.
#include <cstring>
#include <iterator>
#include <memory>
#include <vector>
#include "agsn_common.h"
#include "agsn_room_internal.h"
#include "ac/gamestructdefines.h"
#include "game/interactions.h"
#include "game/room_file.h"
#include "game/roomdata.h"
#include "script/cc_script.h"
#include "util/file.h"
#include "util/memorystream.h"
#include "util/string_utils.h"

using namespace AGS::Common;


namespace
{
    const StringIMap *GetProperties(const agsn_room *room, int32_t owner, int32_t index)
    {
        const RoomData &rd = room->data;
        switch (owner)
        {
        case AGSN_OWNER_ROOM:
            return &rd.Properties;
        case AGSN_OWNER_HOTSPOT:
            return (index >= 0 && static_cast<uint32_t>(index) < rd.HotspotCount) ? &rd.Hotspots[index].Properties : nullptr;
        case AGSN_OWNER_OBJECT:
            return (index >= 0 && static_cast<size_t>(index) < rd.Objects.size()) ? &rd.Objects[index].Properties : nullptr;
        case AGSN_OWNER_REGION:
            return (index >= 0 && static_cast<uint32_t>(index) < rd.RegionCount) ? &rd.Regions[index].Properties : nullptr;
        default:
            return nullptr;
        }
    }

    const InteractionEvents *GetEvents(const agsn_room *room, int32_t owner, int32_t index)
    {
        const RoomData &rd = room->data;
        switch (owner)
        {
        case AGSN_OWNER_ROOM:
            return rd.EventHandlers.get();
        case AGSN_OWNER_HOTSPOT:
            return (index >= 0 && static_cast<uint32_t>(index) < rd.HotspotCount) ? rd.Hotspots[index].EventHandlers.get() : nullptr;
        case AGSN_OWNER_OBJECT:
            return (index >= 0 && static_cast<size_t>(index) < rd.Objects.size()) ? rd.Objects[index].EventHandlers.get() : nullptr;
        case AGSN_OWNER_REGION:
            return (index >= 0 && static_cast<uint32_t>(index) < rd.RegionCount) ? rd.Regions[index].EventHandlers.get() : nullptr;
        default:
            return nullptr;
        }
    }

    StringIMap *GetMutableProperties(agsn_room *room, int32_t owner, int32_t index)
    {
        return const_cast<StringIMap*>(GetProperties(room, owner, index));
    }

    UInteractionEvents *GetMutableEvents(agsn_room *room, int32_t owner, int32_t index)
    {
        RoomData &rd = room->data;
        switch (owner)
        {
        case AGSN_OWNER_ROOM:
            return &rd.EventHandlers;
        case AGSN_OWNER_HOTSPOT:
            return (index >= 0 && index < MAX_ROOM_HOTSPOTS) ? &rd.Hotspots[index].EventHandlers : nullptr;
        case AGSN_OWNER_OBJECT:
            return (index >= 0 && static_cast<size_t>(index) < rd.Objects.size()) ? &rd.Objects[index].EventHandlers : nullptr;
        case AGSN_OWNER_REGION:
            return (index >= 0 && index < MAX_ROOM_REGIONS) ? &rd.Regions[index].EventHandlers : nullptr;
        default:
            return nullptr;
        }
    }

    // TextConverter::ConvertTextProperty() of the upstream editor, after encoding.
    String ConvertTextProperty(const char *bytes, size_t length)
    {
        String str(bytes, length);
        // Escape backslashes before brackets for '\[' support, since Unescape drops '\' in unknown sequences.
        str.Replace("\\[", "\\\\[");
        return StrUtil::Unescape(str);
    }

    // calculate_walkable_areas() of the upstream editor.
    void CalculateWalkableAreas(RoomData &rd)
    {
        for (int i = 0; i < MAX_WALK_AREAS; ++i)
        {
            rd.WalkAreas[i].Top = rd.Height;
            rd.WalkAreas[i].Bottom = 0;
        }
        const PixelBuffer &mask = rd.WalkAreaMaskBuf;
        if (!mask)
            return;
        for (int x = 0; x < mask.GetWidth(); ++x)
        {
            for (int y = 0; y < mask.GetHeight(); ++y)
            {
                int area = mask.GetPixel(x, y);
                if (area >= MAX_WALK_AREAS)
                    continue;
                if (rd.WalkAreas[area].Top > y)
                    rd.WalkAreas[area].Top = y;
                if (rd.WalkAreas[area].Bottom < y)
                    rd.WalkAreas[area].Bottom = y;
            }
        }
    }

    // Copies a pixel buffer into a new image handle.
    agsn_image *MakeImage(const PixelBuffer &src)
    {
        std::unique_ptr<agsn_image> image(new agsn_image());
        image->pixels = PixelBuffer(src.GetWidth(), src.GetHeight(), src.GetFormat());
        for (int y = 0; y < src.GetHeight(); ++y)
            std::memcpy(image->pixels.GetLine(y), src.GetLine(y), std::min(src.GetStride(), image->pixels.GetStride()));
        image->sourceFormat = src.GetFormat();
        image->sourceBits = PixelFormatToPixelBits(src.GetFormat());
        return image.release();
    }
}

extern "C" {

agsn_room *agsn_room_load(const char *path, int32_t game_is_hires,
                          const agsn_sprite_metric *sprites, int32_t sprite_count)
{
    agsn::ApiLock lock;
    agsn::EnsureInitialized();
    agsn::ClearError();
    if (!path)
    {
        agsn::Fail(AGSN_E_INVALID_ARG, "path is null");
        return nullptr;
    }

    std::unique_ptr<Stream> in = File::OpenFileRead(path);
    if (!in)
    {
        agsn::Fail(AGSN_E_IO, std::string("Failed to open room file ") + path);
        return nullptr;
    }

    std::vector<SpriteInfo> spriteInfos;
    if (sprites && sprite_count > 0)
    {
        spriteInfos.reserve(sprite_count);
        for (int32_t i = 0; i < sprite_count; ++i)
            spriteInfos.emplace_back(sprites[i].width, sprites[i].height, sprites[i].flags);
    }

    std::unique_ptr<agsn_room> room(new agsn_room());
    HRoomFileError err = LoadRoom(&room->data, std::move(in), game_is_hires != 0, spriteInfos.empty() ? nullptr : &spriteInfos);
    if (!err)
    {
        agsn::Fail(AGSN_E_FORMAT, std::string(err->FullMessage().GetCStr()));
        return nullptr;
    }
    return room.release();
}

void agsn_room_free(agsn_room *room)
{
    agsn::ApiLock lock;
    delete room;
}

int agsn_room_get_info(const agsn_room *room, agsn_room_info *info)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    if (!room || !info)
        return agsn::Fail(AGSN_E_INVALID_ARG, "room or info is null");
    const RoomData &rd = room->data;
    *info = {};
    info->game_id = rd.GameID;
    info->data_version = rd.DataVersion;
    info->width = rd.Width;
    info->height = rd.Height;
    info->mask_resolution = rd.MaskResolution;
    info->legacy_resolution = rd.GetLegacyResolution();
    info->background_bpp = rd.BackgroundBPP;
    info->bg_frame_count = static_cast<int32_t>(rd.BgFrameCount);
    info->bg_anim_speed = rd.BgAnimSpeed;
    info->edge_left = rd.Edges.Left;
    info->edge_right = rd.Edges.Right;
    info->edge_top = rd.Edges.Top;
    info->edge_bottom = rd.Edges.Bottom;
    info->startup_music = rd.Options.StartupMusic;
    info->save_load_disabled = rd.Options.SaveLoadDisabled ? 1 : 0;
    info->player_char_off = rd.Options.PlayerCharOff ? 1 : 0;
    info->player_view = rd.Options.PlayerView;
    info->music_volume = rd.Options.MusicVolume;
    info->flags = rd.Options.Flags;
    info->hotspot_count = static_cast<int32_t>(rd.HotspotCount);
    info->object_count = static_cast<int32_t>(rd.Objects.size());
    info->region_count = static_cast<int32_t>(rd.RegionCount);
    info->walkarea_count = static_cast<int32_t>(rd.WalkAreaCount);
    info->walkbehind_count = static_cast<int32_t>(rd.WalkBehindCount);
    info->message_count = static_cast<int32_t>(rd.MessageCount);
    info->local_var_count = static_cast<int32_t>(rd.LocalVariables.size());
    info->has_legacy_interactions = rd.DataVersion < kRoomVersion_300a ? 1 : 0;
    return AGSN_OK;
}

int agsn_room_get_object(const agsn_room *room, int32_t index, agsn_room_object *out)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    if (!room || !out || index < 0 || static_cast<size_t>(index) >= room->data.Objects.size())
        return agsn::Fail(AGSN_E_INVALID_ARG, "invalid room object");
    const RoomObjectInfo &o = room->data.Objects[index];
    out->x = o.X;
    out->y = o.Y;
    out->sprite = o.Sprite;
    out->is_on = o.IsOn ? 1 : 0;
    out->baseline = o.Baseline;
    out->transparency = o.Transparency;
    out->flags = o.Flags;
    out->block_x = o.BlockingRect.Left;
    out->block_y = o.BlockingRect.Top;
    out->block_width = o.BlockingRect.GetWidth();
    out->block_height = o.BlockingRect.GetHeight();
    return AGSN_OK;
}

int agsn_room_get_hotspot(const agsn_room *room, int32_t index, agsn_room_hotspot *out)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    if (!room || !out || index < 0 || index >= MAX_ROOM_HOTSPOTS)
        return agsn::Fail(AGSN_E_INVALID_ARG, "invalid room hotspot");
    const RoomHotspot &h = room->data.Hotspots[index];
    out->walk_to_x = h.WalkTo.X;
    out->walk_to_y = h.WalkTo.Y;
    return AGSN_OK;
}

int agsn_room_get_region(const agsn_room *room, int32_t index, agsn_room_region *out)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    if (!room || !out || index < 0 || index >= MAX_ROOM_REGIONS)
        return agsn::Fail(AGSN_E_INVALID_ARG, "invalid room region");
    const RoomData &rd = room->data;
    out->light_raw = rd.Regions[index].Light;
    out->tint_raw = rd.Regions[index].Tint;
    out->light_level = rd.GetRegionLightLevel(index);
    out->has_tint = rd.HasRegionTint(index) ? 1 : 0;
    out->tint_luminance = rd.GetRegionTintLuminance(index);
    return AGSN_OK;
}

int agsn_room_get_walkarea(const agsn_room *room, int32_t index, agsn_room_walkarea *out)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    if (!room || !out || index < 0 || index >= MAX_WALK_AREAS)
        return agsn::Fail(AGSN_E_INVALID_ARG, "invalid walkable area");
    const WalkArea &w = room->data.WalkAreas[index];
    out->scaling_far = w.ScalingFar;
    out->scaling_near = w.ScalingNear;
    out->player_view = w.PlayerView;
    out->top = w.Top;
    out->bottom = w.Bottom;
    return AGSN_OK;
}

int agsn_room_get_walkbehind_baseline(const agsn_room *room, int32_t index, int32_t *baseline)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    if (!room || !baseline || index < 0 || index >= MAX_WALK_BEHINDS)
        return agsn::Fail(AGSN_E_INVALID_ARG, "invalid walk-behind");
    *baseline = room->data.WalkBehinds[index].Baseline;
    return AGSN_OK;
}

int agsn_room_get_message_info(const agsn_room *room, int32_t index, agsn_room_message_info *out)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    if (!room || !out || index < 0 || static_cast<uint32_t>(index) >= room->data.MessageCount)
        return agsn::Fail(AGSN_E_INVALID_ARG, "invalid room message");
    out->display_as = room->data.MessageInfos[index].DisplayAs;
    out->flags = room->data.MessageInfos[index].Flags;
    return AGSN_OK;
}

int agsn_room_get_local_var_value(const agsn_room *room, int32_t index, int32_t *value)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    if (!room || !value || index < 0 || static_cast<size_t>(index) >= room->data.LocalVariables.size())
        return agsn::Fail(AGSN_E_INVALID_ARG, "invalid local variable");
    *value = room->data.LocalVariables[index].Value;
    return AGSN_OK;
}

const char *agsn_room_get_text(const agsn_room *room, int32_t kind, int32_t index)
{
    agsn::ApiLock lock;
    if (!room || index < 0)
        return nullptr;
    const RoomData &rd = room->data;
    switch (kind)
    {
    case AGSN_TEXT_OBJECT_NAME:
        return static_cast<size_t>(index) < rd.Objects.size() ? rd.Objects[index].Name.GetCStr() : nullptr;
    case AGSN_TEXT_OBJECT_SCRIPTNAME:
        return static_cast<size_t>(index) < rd.Objects.size() ? rd.Objects[index].ScriptName.GetCStr() : nullptr;
    case AGSN_TEXT_HOTSPOT_NAME:
        return index < MAX_ROOM_HOTSPOTS ? rd.Hotspots[index].Name.GetCStr() : nullptr;
    case AGSN_TEXT_HOTSPOT_SCRIPTNAME:
        return index < MAX_ROOM_HOTSPOTS ? rd.Hotspots[index].ScriptName.GetCStr() : nullptr;
    case AGSN_TEXT_MESSAGE:
        return static_cast<uint32_t>(index) < rd.MessageCount ? rd.Messages[index].GetCStr() : nullptr;
    case AGSN_TEXT_LOCALVAR_NAME:
        return static_cast<size_t>(index) < rd.LocalVariables.size() ? rd.LocalVariables[index].Name.GetCStr() : nullptr;
    default:
        return nullptr;
    }
}

int32_t agsn_room_get_property_count(const agsn_room *room, int32_t owner, int32_t index)
{
    agsn::ApiLock lock;
    if (!room)
        return -1;
    const StringIMap *props = GetProperties(room, owner, index);
    return props ? static_cast<int32_t>(props->size()) : -1;
}

int agsn_room_get_property(const agsn_room *room, int32_t owner, int32_t index, int32_t property,
                           const char **name, const char **value)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    if (!room || !name || !value)
        return agsn::Fail(AGSN_E_INVALID_ARG, "room, name or value is null");
    const StringIMap *props = GetProperties(room, owner, index);
    if (!props || property < 0 || static_cast<size_t>(property) >= props->size())
        return agsn::Fail(AGSN_E_INVALID_ARG, "invalid property");
    // Iteration order of an unchanged unordered_map is stable, so an index walk is consistent.
    auto it = std::next(props->begin(), property);
    *name = it->first.GetCStr();
    *value = it->second.GetCStr();
    return AGSN_OK;
}

const char *agsn_room_get_script_module(const agsn_room *room, int32_t owner, int32_t index)
{
    agsn::ApiLock lock;
    if (!room)
        return nullptr;
    const InteractionEvents *events = GetEvents(room, owner, index);
    return events ? events->ScriptModule.GetCStr() : nullptr;
}

int32_t agsn_room_get_event_count(const agsn_room *room, int32_t owner, int32_t index)
{
    agsn::ApiLock lock;
    if (!room)
        return -1;
    const InteractionEvents *events = GetEvents(room, owner, index);
    return events ? static_cast<int32_t>(events->Events.size()) : 0;
}

const char *agsn_room_get_event_function(const agsn_room *room, int32_t owner, int32_t index, int32_t event)
{
    agsn::ApiLock lock;
    if (!room)
        return nullptr;
    const InteractionEvents *events = GetEvents(room, owner, index);
    if (!events || event < 0 || static_cast<size_t>(event) >= events->Events.size())
        return nullptr;
    return events->Events[event].FunctionName.GetCStr();
}

const char *agsn_room_get_str_option(const agsn_room *room, const char *key)
{
    agsn::ApiLock lock;
    if (!room || !key)
        return nullptr;
    auto it = room->data.StrOptions.find(key);
    return it != room->data.StrOptions.end() ? it->second.GetCStr() : nullptr;
}

agsn_image *agsn_room_get_background(const agsn_room *room, int32_t frame)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    if (!room || frame < 0 || static_cast<size_t>(frame) >= room->data.BgFrames.size() ||
        static_cast<uint32_t>(frame) >= room->data.BgFrameCount)
    {
        agsn::Fail(AGSN_E_INVALID_ARG, "invalid background frame");
        return nullptr;
    }
    const RoomBgFrame &bg = room->data.BgFrames[frame];
    if (!bg.GraphicBuf)
    {
        agsn::Fail(AGSN_E_FORMAT, "background frame has no image");
        return nullptr;
    }
    agsn_image *image = MakeImage(bg.GraphicBuf);
    // Room palettes are Allegro 6-bit values; the editor displays them multiplied by 4.
    for (int i = 0; i < PAL_SIZE; ++i)
    {
        image->palette[i].r = static_cast<unsigned char>(bg.Palette[i].r * 4);
        image->palette[i].g = static_cast<unsigned char>(bg.Palette[i].g * 4);
        image->palette[i].b = static_cast<unsigned char>(bg.Palette[i].b * 4);
        image->palette[i].a = 255;
    }
    return image;
}

agsn_image *agsn_room_get_mask(const agsn_room *room, int32_t mask)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    if (!room || mask < AGSN_MASK_HOTSPOTS || mask > AGSN_MASK_REGIONS)
    {
        agsn::Fail(AGSN_E_INVALID_ARG, "invalid mask type");
        return nullptr;
    }
    const PixelBuffer *buf = nullptr;
    switch (static_cast<RoomAreaMask>(mask))
    {
    case kRoomAreaHotspot: buf = &room->data.HotspotMaskBuf; break;
    case kRoomAreaWalkBehind: buf = &room->data.WalkBehindMaskBuf; break;
    case kRoomAreaWalkable: buf = &room->data.WalkAreaMaskBuf; break;
    case kRoomAreaRegion: buf = &room->data.RegionMaskBuf; break;
    default: break;
    }
    if (!buf || !*buf)
    {
        agsn::Fail(AGSN_E_FORMAT, "room has no such mask");
        return nullptr;
    }
    return MakeImage(*buf);
}

int agsn_room_set_info(agsn_room *room, const agsn_room_info *info)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    if (!room || !info)
        return agsn::Fail(AGSN_E_INVALID_ARG, "room or info is null");
    if (info->hotspot_count < 0 || info->hotspot_count > MAX_ROOM_HOTSPOTS ||
        info->region_count < 0 || info->region_count > MAX_ROOM_REGIONS ||
        info->walkarea_count < 0 || info->walkarea_count > MAX_WALK_AREAS ||
        info->walkbehind_count < 0 || info->walkbehind_count > MAX_WALK_BEHINDS ||
        info->message_count < 0 || info->message_count > MAX_MESSAGES ||
        info->object_count < 0)
        return agsn::Fail(AGSN_E_INVALID_ARG, "room entity count out of range");

    RoomData &rs = room->data;
    rs.SetLegacyResolution(static_cast<RoomResolutionType>(info->legacy_resolution));
    rs.MaskResolution = info->mask_resolution;
    rs.GameID = info->game_id;
    rs.Edges.Bottom = info->edge_bottom;
    rs.Edges.Left = info->edge_left;
    rs.Edges.Right = info->edge_right;
    rs.Edges.Top = info->edge_top;
    rs.Options.MusicVolume = static_cast<RoomVolumeMod>(info->music_volume);
    rs.Options.PlayerView = info->player_view;
    rs.Options.StartupMusic = info->startup_music;
    rs.Options.SaveLoadDisabled = info->save_load_disabled != 0;
    rs.Options.PlayerCharOff = info->player_char_off != 0;
    rs.Options.Flags = info->flags;
    rs.Width = info->width;
    rs.Height = info->height;
    // NOTE: BgFrameCount is not set here; it follows the background images.
    rs.BgAnimSpeed = info->bg_anim_speed;
    rs.MessageCount = static_cast<uint32_t>(info->message_count);
    rs.Objects.resize(static_cast<size_t>(info->object_count));
    rs.HotspotCount = static_cast<uint32_t>(info->hotspot_count);
    rs.WalkAreaCount = static_cast<uint32_t>(info->walkarea_count);
    rs.WalkBehindCount = static_cast<uint32_t>(info->walkbehind_count);
    rs.RegionCount = static_cast<uint32_t>(info->region_count);
    return AGSN_OK;
}

int agsn_room_set_object(agsn_room *room, int32_t index, const agsn_room_object *obj)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    if (!room || !obj || index < 0 || static_cast<size_t>(index) >= room->data.Objects.size())
        return agsn::Fail(AGSN_E_INVALID_ARG, "invalid room object");
    RoomObjectInfo &o = room->data.Objects[index];
    o.X = obj->x;
    o.Y = obj->y;
    o.Sprite = obj->sprite;
    o.IsOn = obj->is_on != 0;
    o.Baseline = obj->baseline;
    o.Transparency = obj->transparency;
    o.Flags = obj->flags;
    o.BlockingRect = RectWH(obj->block_x, obj->block_y, obj->block_width, obj->block_height);
    return AGSN_OK;
}

int agsn_room_set_hotspot(agsn_room *room, int32_t index, const agsn_room_hotspot *hotspot)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    if (!room || !hotspot || index < 0 || index >= MAX_ROOM_HOTSPOTS)
        return agsn::Fail(AGSN_E_INVALID_ARG, "invalid room hotspot");
    room->data.Hotspots[index].WalkTo.X = hotspot->walk_to_x;
    room->data.Hotspots[index].WalkTo.Y = hotspot->walk_to_y;
    return AGSN_OK;
}

int agsn_room_set_region(agsn_room *room, int32_t index, const agsn_room_region *region)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    if (!room || !region || index < 0 || index >= MAX_ROOM_REGIONS)
        return agsn::Fail(AGSN_E_INVALID_ARG, "invalid room region");
    room->data.Regions[index].Light = region->light_raw;
    room->data.Regions[index].Tint = region->tint_raw;
    return AGSN_OK;
}

int agsn_room_set_walkarea(agsn_room *room, int32_t index, const agsn_room_walkarea *area)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    if (!room || !area || index < 0 || index >= MAX_WALK_AREAS)
        return agsn::Fail(AGSN_E_INVALID_ARG, "invalid walkable area");
    room->data.WalkAreas[index].PlayerView = area->player_view;
    room->data.WalkAreas[index].ScalingFar = area->scaling_far;
    room->data.WalkAreas[index].ScalingNear = area->scaling_near;
    return AGSN_OK;
}

int agsn_room_set_walkbehind_baseline(agsn_room *room, int32_t index, int32_t baseline)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    if (!room || index < 0 || index >= MAX_WALK_BEHINDS)
        return agsn::Fail(AGSN_E_INVALID_ARG, "invalid walk-behind");
    room->data.WalkBehinds[index].Baseline = baseline;
    return AGSN_OK;
}

int agsn_room_set_message_info(agsn_room *room, int32_t index, const agsn_room_message_info *info)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    if (!room || !info || index < 0 || index >= MAX_MESSAGES)
        return agsn::Fail(AGSN_E_INVALID_ARG, "invalid room message");
    room->data.MessageInfos[index].DisplayAs = static_cast<char>(info->display_as);
    room->data.MessageInfos[index].Flags = static_cast<char>(info->flags);
    return AGSN_OK;
}

int agsn_room_set_text(agsn_room *room, int32_t kind, int32_t index,
                       const char *bytes, size_t length, int32_t text_property)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    if (!room || (!bytes && length > 0) || index < 0)
        return agsn::Fail(AGSN_E_INVALID_ARG, "invalid text arguments");
    String value = text_property ? ConvertTextProperty(bytes ? bytes : "", length) : String(bytes ? bytes : "", length);
    RoomData &rd = room->data;
    switch (kind)
    {
    case AGSN_TEXT_OBJECT_NAME:
        if (static_cast<size_t>(index) >= rd.Objects.size()) break;
        rd.Objects[index].Name = value;
        return AGSN_OK;
    case AGSN_TEXT_OBJECT_SCRIPTNAME:
        if (static_cast<size_t>(index) >= rd.Objects.size()) break;
        rd.Objects[index].ScriptName = value;
        return AGSN_OK;
    case AGSN_TEXT_HOTSPOT_NAME:
        if (index >= MAX_ROOM_HOTSPOTS) break;
        rd.Hotspots[index].Name = value;
        return AGSN_OK;
    case AGSN_TEXT_HOTSPOT_SCRIPTNAME:
        if (index >= MAX_ROOM_HOTSPOTS) break;
        rd.Hotspots[index].ScriptName = value;
        return AGSN_OK;
    case AGSN_TEXT_MESSAGE:
        if (index >= MAX_MESSAGES) break;
        rd.Messages[index] = value;
        return AGSN_OK;
    default:
        break;
    }
    return agsn::Fail(AGSN_E_INVALID_ARG, "invalid text kind or index");
}

int agsn_room_clear_properties(agsn_room *room, int32_t owner, int32_t index)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    StringIMap *props = room ? GetMutableProperties(room, owner, index) : nullptr;
    if (!props)
        return agsn::Fail(AGSN_E_INVALID_ARG, "invalid property owner");
    props->clear();
    return AGSN_OK;
}

int agsn_room_set_property(agsn_room *room, int32_t owner, int32_t index, const char *name,
                           const char *value_bytes, size_t value_length)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    StringIMap *props = room ? GetMutableProperties(room, owner, index) : nullptr;
    if (!props || !name || (!value_bytes && value_length > 0))
        return agsn::Fail(AGSN_E_INVALID_ARG, "invalid property arguments");
    (*props)[name] = ConvertTextProperty(value_bytes ? value_bytes : "", value_length);
    return AGSN_OK;
}

int agsn_room_set_events(agsn_room *room, int32_t owner, int32_t index, const char *script_module,
                         const char *const *functions, int32_t function_count)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    UInteractionEvents *slot = room ? GetMutableEvents(room, owner, index) : nullptr;
    if (!slot || function_count < 0 || (!functions && function_count > 0))
        return agsn::Fail(AGSN_E_INVALID_ARG, "invalid event arguments");
    // convert_interaction_scripts() of the upstream editor.
    std::unique_ptr<InteractionEvents> events(new InteractionEvents());
    events->ScriptModule = script_module ? script_module : "";
    for (int32_t i = 0; i < function_count; ++i)
        events->Events.push_back(InteractionEvents::EventHandler(functions[i] ? functions[i] : ""));
    *slot = std::move(events);
    return AGSN_OK;
}

int agsn_room_set_str_option(agsn_room *room, const char *key, const char *value)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    if (!room || !key)
        return agsn::Fail(AGSN_E_INVALID_ARG, "room or key is null");
    if (value)
        room->data.StrOptions[key] = value;
    else
        room->data.StrOptions.erase(key);
    return AGSN_OK;
}

int agsn_room_set_compiled_script(agsn_room *room, const uint8_t *data, size_t size)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    if (!room)
        return agsn::Fail(AGSN_E_INVALID_ARG, "room is null");
    if (!data || size == 0)
    {
        room->data.CompiledScript.reset();
        return AGSN_OK;
    }
    Stream in(std::make_unique<MemoryStream>(data, size));
    ccScript *script = ccScript::CreateFromStream(&in);
    if (!script)
        return agsn::Fail(AGSN_E_FORMAT, "invalid compiled script data");
    room->data.CompiledScript.reset(script);
    return AGSN_OK;
}

int agsn_room_set_mask(agsn_room *room, int32_t mask, int32_t width, int32_t height,
                       const uint8_t *pixels, size_t stride)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    if (!room || !pixels || width <= 0 || height <= 0 || stride < static_cast<size_t>(width))
        return agsn::Fail(AGSN_E_INVALID_ARG, "invalid mask arguments");
    PixelBuffer *buf = nullptr;
    switch (static_cast<RoomAreaMask>(mask))
    {
    case kRoomAreaHotspot: buf = &room->data.HotspotMaskBuf; break;
    case kRoomAreaWalkBehind: buf = &room->data.WalkBehindMaskBuf; break;
    case kRoomAreaWalkable: buf = &room->data.WalkAreaMaskBuf; break;
    case kRoomAreaRegion: buf = &room->data.RegionMaskBuf; break;
    default: return agsn::Fail(AGSN_E_INVALID_ARG, "invalid mask type");
    }
    if (*buf && (buf->GetWidth() != width || buf->GetHeight() != height))
        return agsn::Fail(AGSN_E_INVALID_ARG, "mask size does not match the room");
    PixelBuffer copy(width, height, kPxFmt_Indexed8);
    for (int y = 0; y < height; ++y)
        std::memcpy(copy.GetLine(y), pixels + y * stride, static_cast<size_t>(width));
    *buf = std::move(copy);
    return AGSN_OK;
}

int agsn_room_save(agsn_room *room, const char *path)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    if (!room || !path)
        return agsn::Fail(AGSN_E_INVALID_ARG, "room or path is null");
    RoomData &rs = room->data;
    rs.DataVersion = kRoomVersion_Current;
    CalculateWalkableAreas(rs);

    std::unique_ptr<Stream> out = File::CreateFile(path);
    if (!out)
        return agsn::Fail(AGSN_E_IO, std::string("save_room: unable to open room file for writing: ") + path);
    HRoomFileError err = WriteRoomData(&rs, out.get(), kRoomVersion_Current);
    if (!err)
        return agsn::Fail(AGSN_E_FORMAT, std::string("save_room: unable to write room data, error was:\n") + err->FullMessage().GetCStr());
    return AGSN_OK;
}

} // extern "C"
