/*
 * ags4mac: libagsnative public C API.
 *
 * Conventions
 *  - Strings are UTF-8, zero-terminated.
 *  - Functions returning int return AGSN_OK (0) on success, a negative AGSN_E_* code on failure;
 *    agsn_last_error() then describes the failure (thread-local, valid until the next call).
 *  - Opaque handles are created by *_load/*_open functions and released by the matching *_free/*_close.
 *  - Pixel and palette data are copied into caller-owned buffers.
 */
#ifndef AGSNATIVE_H
#define AGSNATIVE_H

#include <stddef.h>
#include <stdint.h>

#if defined(AGSN_BUILDING_LIBRARY)
#  define AGSN_API __attribute__((visibility("default")))
#else
#  define AGSN_API
#endif

#ifdef __cplusplus
extern "C" {
#endif

#define AGSN_API_VERSION 1

enum agsn_result
{
    AGSN_OK = 0,
    AGSN_E_INVALID_ARG = -1,
    AGSN_E_IO = -2,
    AGSN_E_FORMAT = -3,
    AGSN_E_BUFFER_TOO_SMALL = -4,
    AGSN_E_UNSUPPORTED = -5,
};

/* Pixel formats; identical meaning to AGS::Common::PixelFormat (little-endian memory order). */
enum agsn_pixel_format
{
    AGSN_PXFMT_UNDEFINED = 0,
    AGSN_PXFMT_INDEXED8 = 1,  /* 8-bit palette index */
    AGSN_PXFMT_R5G5B5 = 2,    /* 15-bit */
    AGSN_PXFMT_R5G6B5 = 3,    /* 16-bit */
    AGSN_PXFMT_R8G8B8 = 4,    /* 24-bit, bytes B,G,R */
    AGSN_PXFMT_A8R8G8B8 = 5,  /* 32-bit, bytes B,G,R,A */
};

/* Palette entry, 8 bits per channel. Alpha is only meaningful where the source format has it (PNG tRNS). */
typedef struct agsn_rgba
{
    uint8_t r, g, b, a;
} agsn_rgba;

AGSN_API int agsn_api_version(void);

/*
 * Initializes the library the same way the upstream editor's native DLL does (Allegro in
 * SYSTEM_NONE mode, UTF-8 text, the editor's RGB channel shifts). Called implicitly by every
 * other function; calling it explicitly just surfaces failures early. Returns AGSN_OK.
 */
AGSN_API int agsn_initialize(void);
AGSN_API const char *agsn_ags_version(void);
AGSN_API const char *agsn_last_error(void);

/* ---- Native constants (upstream NativeMethods::GetNativeConstant) ---- */

enum agsn_constant_type
{
    AGSN_CONST_NONE = 0,   /* unknown name */
    AGSN_CONST_INT = 1,
    AGSN_CONST_STRING = 2, /* zero-terminated, in *data */
    AGSN_CONST_BYTES = 3,  /* *data_length bytes in *data */
};

/* Looks up an engine/editor constant by name; returns an agsn_constant_type. Out pointers may be NULL. */
AGSN_API int32_t agsn_get_constant(const char *name, int32_t *int_value, const char **data, int32_t *data_length);

/* ---- Images (BMP, PCX, PNG via ags/Common/gfx/image_file) ---- */

typedef struct agsn_image agsn_image;

typedef struct agsn_image_info
{
    int32_t width;
    int32_t height;
    int32_t format;        /* agsn_pixel_format of the pixel data */
    int32_t source_format; /* agsn_pixel_format the file was stored in (e.g. 4-bit files load as INDEXED8) */
    int32_t source_bits;   /* bits per pixel in the file: 1, 4, 8, 15, 16, 24, 32 */
} agsn_image_info;

/* Loads an image; the format is chosen by the file extension (.bmp, .pcx, .png). NULL on failure. */
AGSN_API agsn_image *agsn_image_load(const char *path);
AGSN_API void agsn_image_free(agsn_image *image);
AGSN_API int agsn_image_get_info(const agsn_image *image, agsn_image_info *info);
/* Copies pixel rows into dst, dst_stride bytes apart. dst_stride must be >= width * bytes per pixel. */
AGSN_API int agsn_image_copy_pixels(const agsn_image *image, uint8_t *dst, size_t dst_size, size_t dst_stride);
/* Copies the 256-entry palette (indexed images; zeros otherwise). */
AGSN_API int agsn_image_copy_palette(const agsn_image *image, agsn_rgba *palette256);

/*
 * Saves pixel rows (src_stride bytes apart) to path; the format is chosen by extension.
 * palette256 is required for INDEXED8 and ignored otherwise. skip_alpha writes 32-bit images without alpha.
 */
AGSN_API int agsn_image_save(const char *path, int32_t width, int32_t height, int32_t format,
                             const uint8_t *pixels, size_t src_stride,
                             const agsn_rgba *palette256, int32_t skip_alpha);

/* ---- Sprite files (acsprset.spr + sprindex.dat via ags/Common/ac/spritefile) ---- */

typedef struct agsn_sprite_file agsn_sprite_file;

typedef struct agsn_sprite_info
{
    int32_t exists;          /* nonzero if the slot holds a sprite */
    int32_t width;
    int32_t height;
    int32_t bytes_per_pixel; /* 1 (palette index into the game palette), 2 (R5G6B5), 4 (A8R8G8B8) */
} agsn_sprite_info;

/* Opens a sprite file; index_path (sprindex.dat) is optional and speeds up opening. NULL on failure. */
AGSN_API agsn_sprite_file *agsn_spritefile_open(const char *sprite_path, const char *index_path);
AGSN_API void agsn_spritefile_close(agsn_sprite_file *file);
/* Highest sprite slot number in the file, or -1 if it holds no sprites. */
AGSN_API int32_t agsn_spritefile_get_topmost(const agsn_sprite_file *file);
/* Number of slots that hold a sprite. */
AGSN_API int32_t agsn_spritefile_get_count(const agsn_sprite_file *file);
/* Storage flags and compression (AGS::Common::SpriteStorage / SpriteCompression values). */
AGSN_API int32_t agsn_spritefile_get_store_flags(const agsn_sprite_file *file);
AGSN_API int32_t agsn_spritefile_get_compression(const agsn_sprite_file *file);
AGSN_API int agsn_spritefile_get_info(const agsn_sprite_file *file, int32_t index, agsn_sprite_info *info);
/*
 * Loads a sprite as an image (free it with agsn_image_free). 8-bit sprites have no palette of their
 * own; they index the game palette. NULL on failure or if the slot is empty (then agsn_last_error is "").
 */
AGSN_API agsn_image *agsn_spritefile_load(agsn_sprite_file *file, int32_t index);

/* Sprite file writing (AGS::Common::SpriteFileWriter), slot by slot from 0 upwards. */
typedef struct agsn_sprite_writer agsn_sprite_writer;

/* Creates the file and starts it; store_flags = SpriteStorage, compression = SpriteCompression. */
AGSN_API agsn_sprite_writer *agsn_spritewriter_create(const char *path, int32_t store_flags, int32_t compression);
/* Writes the next slot from pixel rows (INDEXED8, R5G6B5 or A8R8G8B8; stride bytes apart). */
AGSN_API int agsn_spritewriter_write_image(agsn_sprite_writer *writer, int32_t width, int32_t height, int32_t format,
                                           const uint8_t *pixels, size_t stride);
AGSN_API int agsn_spritewriter_write_empty(agsn_sprite_writer *writer);
/* Writes the next slot by copying sprite `index` of an open sprite file without decoding it. */
AGSN_API int agsn_spritewriter_copy_from(agsn_sprite_writer *writer, agsn_sprite_file *source, int32_t index);
/* Finalizes and closes the file; writes the sprite index (sprindex.dat) too if index_path is given. */
AGSN_API int agsn_spritewriter_finish(agsn_sprite_writer *writer, const char *index_path);
/* Frees the writer (closes the file unfinished if agsn_spritewriter_finish wasn't called). */
AGSN_API void agsn_spritewriter_free(agsn_sprite_writer *writer);

/* ---- Rooms (*.crm via ags/Common/game/room_file, loaded the way the upstream editor does) ---- */

typedef struct agsn_room agsn_room;

/* Same values as AGS::Common::RoomAreaMask. */
enum agsn_room_mask
{
    AGSN_MASK_HOTSPOTS = 1,
    AGSN_MASK_WALKBEHINDS = 2,
    AGSN_MASK_WALKABLE = 3,
    AGSN_MASK_REGIONS = 4,
};

/* Owner of custom properties and event handlers. */
enum agsn_room_owner
{
    AGSN_OWNER_ROOM = 0,
    AGSN_OWNER_HOTSPOT = 1,
    AGSN_OWNER_OBJECT = 2,
    AGSN_OWNER_REGION = 3,
};

/* Text fields. Texts are raw bytes in the room's/game's text encoding, except script names (ASCII). */
enum agsn_room_text
{
    AGSN_TEXT_OBJECT_NAME = 1,       /* description */
    AGSN_TEXT_OBJECT_SCRIPTNAME = 2,
    AGSN_TEXT_HOTSPOT_NAME = 3,      /* description */
    AGSN_TEXT_HOTSPOT_SCRIPTNAME = 4,
    AGSN_TEXT_MESSAGE = 5,
    AGSN_TEXT_LOCALVAR_NAME = 6,
};

/* Width/height/flags of a game sprite, used to upgrade legacy rooms like the editor does. */
typedef struct agsn_sprite_metric
{
    int32_t width;
    int32_t height;
    uint32_t flags; /* SPF_* */
} agsn_sprite_metric;

typedef struct agsn_room_info
{
    int32_t game_id;
    int32_t data_version;          /* RoomFileVersion */
    int32_t width;
    int32_t height;
    int32_t mask_resolution;
    int32_t legacy_resolution;     /* RoomResolutionType */
    int32_t background_bpp;        /* bytes per pixel */
    int32_t bg_frame_count;
    int32_t bg_anim_speed;
    int32_t edge_left, edge_right, edge_top, edge_bottom;
    int32_t startup_music;
    int32_t save_load_disabled;
    int32_t player_char_off;
    int32_t player_view;
    int32_t music_volume;          /* RoomVolumeMod */
    int32_t flags;                 /* RoomFlags */
    int32_t hotspot_count;
    int32_t object_count;
    int32_t region_count;
    int32_t walkarea_count;
    int32_t walkbehind_count;
    int32_t message_count;
    int32_t local_var_count;
    int32_t has_legacy_interactions; /* pre-3.0 interaction scripts instead of event handlers */
} agsn_room_info;

typedef struct agsn_room_object
{
    int32_t x, y;
    int32_t sprite;
    int32_t is_on;
    int32_t baseline;
    int32_t transparency;          /* legacy 0..255 value, as stored */
    int32_t flags;                 /* OBJF_* */
    int32_t block_x, block_y, block_width, block_height;
} agsn_room_object;

typedef struct agsn_room_hotspot
{
    int32_t walk_to_x, walk_to_y;
} agsn_room_hotspot;

typedef struct agsn_room_region
{
    int32_t light_raw;             /* as stored */
    int32_t tint_raw;              /* as stored: R | G<<8 | B<<16 | saturation<<24 */
    int32_t light_level;           /* RoomData::GetRegionLightLevel */
    int32_t has_tint;              /* RoomData::HasRegionTint */
    int32_t tint_luminance;        /* RoomData::GetRegionTintLuminance */
} agsn_room_region;

typedef struct agsn_room_walkarea
{
    int32_t scaling_far;
    int32_t scaling_near;          /* NOT_VECTOR_SCALED (-10000) if not continuous */
    int32_t player_view;
    int32_t top, bottom;
} agsn_room_walkarea;

typedef struct agsn_room_message_info
{
    int32_t display_as;            /* 0 = message window, >= 1 = speech of character (display_as - 1) */
    int32_t flags;                 /* MSG_* */
} agsn_room_message_info;

/*
 * Loads a room file as the editor does (LoadRoom: reads and upgrades old data). game_is_hires and
 * sprites (may be NULL) are only used to upgrade legacy rooms. NULL on failure.
 */
AGSN_API agsn_room *agsn_room_load(const char *path, int32_t game_is_hires,
                                   const agsn_sprite_metric *sprites, int32_t sprite_count);
AGSN_API void agsn_room_free(agsn_room *room);
AGSN_API int agsn_room_get_info(const agsn_room *room, agsn_room_info *info);
AGSN_API int agsn_room_get_object(const agsn_room *room, int32_t index, agsn_room_object *out);
AGSN_API int agsn_room_get_hotspot(const agsn_room *room, int32_t index, agsn_room_hotspot *out);
AGSN_API int agsn_room_get_region(const agsn_room *room, int32_t index, agsn_room_region *out);
AGSN_API int agsn_room_get_walkarea(const agsn_room *room, int32_t index, agsn_room_walkarea *out);
AGSN_API int agsn_room_get_walkbehind_baseline(const agsn_room *room, int32_t index, int32_t *baseline);
AGSN_API int agsn_room_get_message_info(const agsn_room *room, int32_t index, agsn_room_message_info *out);
AGSN_API int agsn_room_get_local_var_value(const agsn_room *room, int32_t index, int32_t *value);
/* Text field (see agsn_room_text); valid until the room is freed. NULL if out of range. */
AGSN_API const char *agsn_room_get_text(const agsn_room *room, int32_t kind, int32_t index);
/* Custom properties of an owner (see agsn_room_owner); -1 if the owner doesn't exist. */
AGSN_API int32_t agsn_room_get_property_count(const agsn_room *room, int32_t owner, int32_t index);
AGSN_API int agsn_room_get_property(const agsn_room *room, int32_t owner, int32_t index, int32_t property,
                                    const char **name, const char **value);
/* Event handlers (3.0+ rooms): script module and function name per event slot. */
AGSN_API const char *agsn_room_get_script_module(const agsn_room *room, int32_t owner, int32_t index);
AGSN_API int32_t agsn_room_get_event_count(const agsn_room *room, int32_t owner, int32_t index);
AGSN_API const char *agsn_room_get_event_function(const agsn_room *room, int32_t owner, int32_t index, int32_t event);
/* Room string options (e.g. "textencoding"); NULL if not set. */
AGSN_API const char *agsn_room_get_str_option(const agsn_room *room, const char *key);
/* Background frame as an image (free with agsn_image_free). 8-bit backgrounds carry the frame
   palette scaled to 8 bits per channel (the editor shows Allegro's 6-bit values times 4). */
AGSN_API agsn_image *agsn_room_get_background(const agsn_room *room, int32_t frame);
/* Area mask as an 8-bit image of area numbers (free with agsn_image_free). */
AGSN_API agsn_image *agsn_room_get_mask(const agsn_room *room, int32_t mask);

/*
 * Editing, as convert_room_to_native() in the upstream editor: the managed side pushes all
 * editable fields into the loaded room, then saves it. Binary content (backgrounds, masks)
 * stays as loaded unless replaced.
 *
 * agsn_room_set_info uses: game_id, width, height, mask_resolution, legacy_resolution,
 * bg_anim_speed, edges, startup_music, save_load_disabled, player_char_off, player_view,
 * music_volume, flags, and the counts hotspot/object/region/walkarea/walkbehind/message
 * (object_count resizes the object list). Other fields are ignored.
 */
AGSN_API int agsn_room_set_info(agsn_room *room, const agsn_room_info *info);
AGSN_API int agsn_room_set_object(agsn_room *room, int32_t index, const agsn_room_object *obj);
AGSN_API int agsn_room_set_hotspot(agsn_room *room, int32_t index, const agsn_room_hotspot *hotspot);
/* Uses light_raw and tint_raw only. */
AGSN_API int agsn_room_set_region(agsn_room *room, int32_t index, const agsn_room_region *region);
/* Uses scaling_far, scaling_near and player_view; top/bottom are recalculated on save. */
AGSN_API int agsn_room_set_walkarea(agsn_room *room, int32_t index, const agsn_room_walkarea *area);
AGSN_API int agsn_room_set_walkbehind_baseline(agsn_room *room, int32_t index, int32_t baseline);
AGSN_API int agsn_room_set_message_info(agsn_room *room, int32_t index, const agsn_room_message_info *info);
/*
 * Sets a text field from raw bytes. With text_property nonzero the value is unescaped like the
 * editor's TextConverter::ConvertTextProperty (StrUtil::Unescape, keeping "\[" intact).
 */
AGSN_API int agsn_room_set_text(agsn_room *room, int32_t kind, int32_t index,
                                const char *bytes, size_t length, int32_t text_property);
AGSN_API int agsn_room_clear_properties(agsn_room *room, int32_t owner, int32_t index);
/* Adds or replaces a custom property; value_bytes are unescaped like a text property. */
AGSN_API int agsn_room_set_property(agsn_room *room, int32_t owner, int32_t index, const char *name,
                                    const char *value_bytes, size_t value_length);
/* Replaces the event handlers of an owner: script module plus one function name per event slot. */
AGSN_API int agsn_room_set_events(agsn_room *room, int32_t owner, int32_t index, const char *script_module,
                                  const char *const *functions, int32_t function_count);
AGSN_API int agsn_room_set_str_option(agsn_room *room, const char *key, const char *value);
/* Sets the compiled room script from a serialized ccScript (as written by agscc); NULL clears it. */
AGSN_API int agsn_room_set_compiled_script(agsn_room *room, const uint8_t *data, size_t size);
/* Replaces an area mask with 8-bit area numbers (rows `stride` bytes apart). The size must match the
   mask's current size (room size, divided by the mask resolution except for walk-behinds). */
AGSN_API int agsn_room_set_mask(agsn_room *room, int32_t mask, int32_t width, int32_t height,
                                const uint8_t *pixels, size_t stride);
/* Imports an image file (BMP/PCX/PNG) as background `frame` (0..count; count appends a frame), as the
   upstream editor's ImportBackground(): converted to the game's colour depth (8/16/32), padded with
   black up to min_width x min_height, 8-bit images remapped into the background palette slots
   (game_palette: 256 entries, 8 bits per channel; paluses: 0 gamewide, 1 locked, 2 background) unless
   use_exact_palette. Frame 0 sets the room size and real resolution; masks are cleared when the size
   changes. Other frames must match the room size. colours_lost (optional) receives the number of
   colours that did not fit into the background slots. */
AGSN_API int agsn_room_import_background(agsn_room *room, int32_t frame, const char *image_path,
                                         int32_t game_color_depth, int32_t min_width, int32_t min_height,
                                         const agsn_rgba *game_palette, const uint8_t *paluses,
                                         int32_t use_exact_palette, int32_t *colours_lost);
/* Creates a new room with black backgrounds and empty masks (save_default_crm_file()); set its data with
   agsn_room_set_info etc. and save it. color_depth in bits (8, 16, 32). Free with agsn_room_free. */
AGSN_API agsn_room *agsn_room_create(int32_t width, int32_t height, int32_t mask_resolution, int32_t color_depth,
                                     int32_t background_count);
/* Script source embedded in an old (pre-3.0) room file, as load_room_script(): *found is 0 when the
   file has none. Call with buffer NULL to get *length (bytes, ANSI, not NUL-terminated). */
AGSN_API int agsn_room_extract_script_text(const char *room_path, char *buffer, size_t buffer_size,
                                           size_t *length, int32_t *found);
/* Removes background `frame` (later frames move down), as DeleteBackground(). */
AGSN_API int agsn_room_delete_background(agsn_room *room, int32_t frame);
/* ---- GUI preview (upstream editor's drawGUI) ----------------------------------------------- */

enum agsn_font_height_def { AGSN_FONT_HEIGHT_NOMINAL = 0, AGSN_FONT_HEIGHT_PIXEL = 1, AGSN_FONT_HEIGHT_CUSTOM = 2 };
enum agsn_font_outline { AGSN_FONT_OUTLINE_NONE = 0, AGSN_FONT_OUTLINE_FONT = 1, AGSN_FONT_OUTLINE_AUTOMATIC = 2 };

/* A game font, as AGS.Types.Font (see GameFontUpdated in the upstream editor). */
typedef struct agsn_font_info
{
    int32_t point_size;          /* 0: size_multiplier applies */
    int32_t size_multiplier;
    int32_t vertical_offset;
    int32_t line_spacing;
    int32_t character_spacing;
    int32_t height_defined_by;   /* agsn_font_height_def */
    int32_t ascender_fixup;      /* FontMetricsFixup.SetAscenderToHeight */
    int32_t outline_style;       /* agsn_font_outline */
    int32_t outline_font;        /* for AGSN_FONT_OUTLINE_FONT */
    int32_t auto_outline_style;  /* 0 squared, 1 rounded */
    int32_t auto_outline_thickness;
} agsn_font_info;

enum agsn_gui_control_type
{
    AGSN_GUI_BUTTON = 0, AGSN_GUI_LABEL = 1, AGSN_GUI_TEXTBOX = 2, AGSN_GUI_LISTBOX = 3,
    AGSN_GUI_SLIDER = 4, AGSN_GUI_INVWINDOW = 5, AGSN_GUI_TEXTWINDOWEDGE = 6
};

typedef struct agsn_gui_desc
{
    int32_t is_text_window;
    int32_t x, y, width, height;
    int32_t clickable, visible, popup_y, popup_style, z_order, transparency;
    int32_t fg_color;            /* border colour (normal GUI) or text colour (text window) */
    int32_t padding;             /* text window */
    int32_t bg_color, bg_image;
} agsn_gui_desc;

/* One control; fields not used by its type are ignored. Text is UTF-8. */
typedef struct agsn_gui_control
{
    int32_t type;                /* agsn_gui_control_type */
    int32_t id, z_order, x, y, width, height;
    int32_t solid_background, show_border, back_color, border_color, border_width, padding_x, padding_y;
    int32_t font, text_color, text_outline_color, text_alignment, wrap_text, clip_image;
    int32_t image, mouseover_image, pushed_image, border_shade_color;
    int32_t mouseover_back_color, pushed_back_color, mouseover_border_color, pushed_border_color;
    int32_t mouseover_text_color, pushed_text_color, dynamic_colors, flat_style;
    int32_t selected_text_color, selected_bg_color, show_arrows, translated;
    int32_t min_value, max_value, value, handle_image, handle_offset, handle_color, bg_image;
    int32_t item_width, item_height, character_id;
    const char *text;
} agsn_gui_control;

/* Sets up GUI drawing for a game: its folder (fonts agsfntN.*, acsprset.spr), colour depth in bits,
   anti-aliased TTF option and palette (256 entries, 8-bit game only; may be NULL). Call again to reset. */
AGSN_API int agsn_gui_init(const char *game_dir, int32_t color_depth, int32_t antialias_fonts, const agsn_rgba *palette256);
/* (Re)loads game font `font_number` with the given settings. */
AGSN_API int agsn_gui_set_font(int32_t font_number, const agsn_font_info *font);
/* Draws a GUI with its controls like the editor's GUI preview; `highlight_control` is a control index
   or -1. Returns a 32-bit image (transparent where the GUI has no background); free with agsn_image_free. */
AGSN_API agsn_image *agsn_gui_draw(const agsn_gui_desc *gui, const agsn_gui_control *controls, int32_t control_count,
                                   int32_t highlight_control);

typedef struct agsn_font_metrics
{
    int32_t height, real_height, line_spacing, topmost_char, is_bitmap_font;
    char name[128];
} agsn_font_metrics;

/* The point size of a TTF file in the game folder that gives the requested pixel height (upstream
   NativeMethods.FindTTFSizeForHeight / measure_font_height). Needs agsn_gui_init. */
AGSN_API int agsn_font_find_ttf_size(const char *file_name, int32_t pixel_height, int32_t *point_size);

/* Converts a Sierra SCI font file to an AGS bitmap font (WFN) file (upstream NativeMethods.ImportSCIFont /
   import_sci_font, which writes agsfnt<slot>.wfn). Doesn't need agsn_gui_init. */
AGSN_API int agsn_font_import_sci(const char *sci_file, const char *wfn_file);

/* Metrics of a font loaded with agsn_gui_set_font. */
AGSN_API int agsn_font_get_metrics(int32_t font_number, agsn_font_metrics *metrics);
/* Draws UTF-8 text with a game font (wrapped at max_width if > 0): white text, dark grey outline if
   draw_outline and the font has one, transparent background (as the editor's font preview). */
AGSN_API agsn_image *agsn_font_draw_text(int32_t font_number, const char *text, int32_t max_width, int32_t draw_outline);

/* Game template (.agt): extracts all files except template.dta into dest_dir (extract_template_files). */
AGSN_API int agsn_template_extract_game(const char *template_path, const char *dest_dir);
/* Room template (.art): extracts its files as roomN.<ext> into dest_dir (extract_room_template_files). */
AGSN_API int agsn_template_extract_room(const char *template_path, const char *dest_dir, int32_t new_room_number);

/* Saves in the current room format, as save_room_file() in the upstream editor. */
AGSN_API int agsn_room_save(agsn_room *room, const char *path);

#ifdef __cplusplus
}
#endif

#endif /* AGSNATIVE_H */
