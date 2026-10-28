using System;
using System.Runtime.InteropServices;

namespace AGS.Native
{
    /// <summary>Raw bindings to libagsnative (native/shim/agsnative.h). Use the wrappers instead.</summary>
    internal static unsafe partial class NativeMethods
    {
        private const string Library = "agsnative";

        public const int AGSN_OK = 0;
        public const int AGSN_E_INVALID_ARG = -1;
        public const int AGSN_E_IO = -2;
        public const int AGSN_E_FORMAT = -3;
        public const int AGSN_E_BUFFER_TOO_SMALL = -4;
        public const int AGSN_E_UNSUPPORTED = -5;

        public const int AGSN_PXFMT_UNDEFINED = 0;
        public const int AGSN_PXFMT_INDEXED8 = 1;
        public const int AGSN_PXFMT_R5G5B5 = 2;
        public const int AGSN_PXFMT_R5G6B5 = 3;
        public const int AGSN_PXFMT_R8G8B8 = 4;
        public const int AGSN_PXFMT_A8R8G8B8 = 5;

        [StructLayout(LayoutKind.Sequential)]
        public struct Rgba
        {
            public byte R, G, B, A;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ImageInfo
        {
            public int Width;
            public int Height;
            public int Format;
            public int SourceFormat;
            public int SourceBits;
        }

        [LibraryImport(Library, EntryPoint = "agsn_api_version")]
        public static partial int ApiVersion();

        [LibraryImport(Library, EntryPoint = "agsn_initialize")]
        public static partial int Initialize();

        [LibraryImport(Library, EntryPoint = "agsn_ags_version")]
        public static partial IntPtr AgsVersion();

        [LibraryImport(Library, EntryPoint = "agsn_last_error")]
        public static partial IntPtr LastError();

        public const int AGSN_CONST_NONE = 0;
        public const int AGSN_CONST_INT = 1;
        public const int AGSN_CONST_STRING = 2;
        public const int AGSN_CONST_BYTES = 3;

        [LibraryImport(Library, EntryPoint = "agsn_get_constant", StringMarshalling = StringMarshalling.Utf8)]
        public static partial int GetConstant(string name, out int intValue, out IntPtr data, out int dataLength);

        [LibraryImport(Library, EntryPoint = "agsn_image_load", StringMarshalling = StringMarshalling.Utf8)]
        public static partial IntPtr ImageLoad(string path);

        [LibraryImport(Library, EntryPoint = "agsn_image_free")]
        public static partial void ImageFree(IntPtr image);

        [LibraryImport(Library, EntryPoint = "agsn_image_get_info")]
        public static partial int ImageGetInfo(IntPtr image, out ImageInfo info);

        [LibraryImport(Library, EntryPoint = "agsn_image_copy_pixels")]
        public static partial int ImageCopyPixels(IntPtr image, byte* dst, nuint dstSize, nuint dstStride);

        [LibraryImport(Library, EntryPoint = "agsn_image_copy_palette")]
        public static partial int ImageCopyPalette(IntPtr image, Rgba* palette256);

        [LibraryImport(Library, EntryPoint = "agsn_image_save", StringMarshalling = StringMarshalling.Utf8)]
        public static partial int ImageSave(string path, int width, int height, int format,
            byte* pixels, nuint srcStride, Rgba* palette256, int skipAlpha);

        [LibraryImport(Library, EntryPoint = "agsn_spritewriter_create", StringMarshalling = StringMarshalling.Utf8)]
        public static partial IntPtr SpriteWriterCreate(string path, int storeFlags, int compression);

        [LibraryImport(Library, EntryPoint = "agsn_spritewriter_write_image")]
        public static partial int SpriteWriterWriteImage(IntPtr writer, int width, int height, int format, byte* pixels, nuint stride);

        [LibraryImport(Library, EntryPoint = "agsn_spritewriter_write_empty")]
        public static partial int SpriteWriterWriteEmpty(IntPtr writer);

        [LibraryImport(Library, EntryPoint = "agsn_spritewriter_copy_from")]
        public static partial int SpriteWriterCopyFrom(IntPtr writer, IntPtr source, int index);

        [LibraryImport(Library, EntryPoint = "agsn_spritewriter_finish", StringMarshalling = StringMarshalling.Utf8)]
        public static partial int SpriteWriterFinish(IntPtr writer, string indexPath);

        [LibraryImport(Library, EntryPoint = "agsn_spritewriter_free")]
        public static partial void SpriteWriterFree(IntPtr writer);

        [StructLayout(LayoutKind.Sequential)]
        public struct SpriteInfo
        {
            public int Exists;
            public int Width;
            public int Height;
            public int BytesPerPixel;
        }

        [LibraryImport(Library, EntryPoint = "agsn_spritefile_open", StringMarshalling = StringMarshalling.Utf8)]
        public static partial IntPtr SpriteFileOpen(string spritePath, string indexPath);

        [LibraryImport(Library, EntryPoint = "agsn_spritefile_close")]
        public static partial void SpriteFileClose(IntPtr file);

        [LibraryImport(Library, EntryPoint = "agsn_spritefile_get_topmost")]
        public static partial int SpriteFileGetTopmost(IntPtr file);

        [LibraryImport(Library, EntryPoint = "agsn_spritefile_get_count")]
        public static partial int SpriteFileGetCount(IntPtr file);

        [LibraryImport(Library, EntryPoint = "agsn_spritefile_get_store_flags")]
        public static partial int SpriteFileGetStoreFlags(IntPtr file);

        [LibraryImport(Library, EntryPoint = "agsn_spritefile_get_compression")]
        public static partial int SpriteFileGetCompression(IntPtr file);

        [LibraryImport(Library, EntryPoint = "agsn_spritefile_get_info")]
        public static partial int SpriteFileGetInfo(IntPtr file, int index, out SpriteInfo info);

        [LibraryImport(Library, EntryPoint = "agsn_spritefile_load")]
        public static partial IntPtr SpriteFileLoad(IntPtr file, int index);

        public const int AGSN_MASK_HOTSPOTS = 1;
        public const int AGSN_MASK_WALKBEHINDS = 2;
        public const int AGSN_MASK_WALKABLE = 3;
        public const int AGSN_MASK_REGIONS = 4;

        public const int AGSN_OWNER_ROOM = 0;
        public const int AGSN_OWNER_HOTSPOT = 1;
        public const int AGSN_OWNER_OBJECT = 2;
        public const int AGSN_OWNER_REGION = 3;

        public const int AGSN_TEXT_OBJECT_NAME = 1;
        public const int AGSN_TEXT_OBJECT_SCRIPTNAME = 2;
        public const int AGSN_TEXT_HOTSPOT_NAME = 3;
        public const int AGSN_TEXT_HOTSPOT_SCRIPTNAME = 4;
        public const int AGSN_TEXT_MESSAGE = 5;
        public const int AGSN_TEXT_LOCALVAR_NAME = 6;

        [StructLayout(LayoutKind.Sequential)]
        public struct SpriteMetric
        {
            public int Width;
            public int Height;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FontInfo
        {
            public int PointSize, SizeMultiplier, VerticalOffset, LineSpacing, CharacterSpacing;
            public int HeightDefinedBy, AscenderFixup, OutlineStyle, OutlineFont, AutoOutlineStyle, AutoOutlineThickness;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct GuiDesc
        {
            public int IsTextWindow, X, Y, Width, Height;
            public int Clickable, Visible, PopupY, PopupStyle, ZOrder, Transparency;
            public int FgColor, Padding, BgColor, BgImage;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct GuiControl
        {
            public int Type, Id, ZOrder, X, Y, Width, Height;
            public int SolidBackground, ShowBorder, BackColor, BorderColor, BorderWidth, PaddingX, PaddingY;
            public int Font, TextColor, TextOutlineColor, TextAlignment, WrapText, ClipImage;
            public int Image, MouseoverImage, PushedImage, BorderShadeColor;
            public int MouseoverBackColor, PushedBackColor, MouseoverBorderColor, PushedBorderColor;
            public int MouseoverTextColor, PushedTextColor, DynamicColors, FlatStyle;
            public int SelectedTextColor, SelectedBgColor, ShowArrows, Translated;
            public int MinValue, MaxValue, Value, HandleImage, HandleOffset, HandleColor, BgImage;
            public int ItemWidth, ItemHeight, CharacterId;
            public IntPtr Text;
        }

        public const int AGSN_GUI_BUTTON = 0, AGSN_GUI_LABEL = 1, AGSN_GUI_TEXTBOX = 2, AGSN_GUI_LISTBOX = 3,
            AGSN_GUI_SLIDER = 4, AGSN_GUI_INVWINDOW = 5, AGSN_GUI_TEXTWINDOWEDGE = 6;

        [LibraryImport(Library, EntryPoint = "agsn_gui_init", StringMarshalling = StringMarshalling.Utf8)]
        public static partial int GuiInit(string gameDir, int colorDepth, int antialiasFonts, Rgba* palette256);

        [LibraryImport(Library, EntryPoint = "agsn_gui_set_font")]
        public static partial int GuiSetFont(int fontNumber, in FontInfo font);

        [StructLayout(LayoutKind.Sequential)]
        public struct FontMetrics
        {
            public int Height, RealHeight, LineSpacing, TopmostChar, IsBitmapFont;
            public fixed byte Name[128];
        }

        [LibraryImport(Library, EntryPoint = "agsn_font_find_ttf_size", StringMarshalling = StringMarshalling.Utf8)]
        public static partial int FontFindTtfSize(string fileName, int pixelHeight, out int pointSize);

        [LibraryImport(Library, EntryPoint = "agsn_font_import_sci", StringMarshalling = StringMarshalling.Utf8)]
        public static partial int FontImportSci(string sciFile, string wfnFile);

        [LibraryImport(Library, EntryPoint = "agsn_font_get_metrics")]
        public static partial int FontGetMetrics(int fontNumber, out FontMetrics metrics);

        [LibraryImport(Library, EntryPoint = "agsn_font_draw_text", StringMarshalling = StringMarshalling.Utf8)]
        public static partial IntPtr FontDrawText(int fontNumber, string text, int maxWidth, int drawOutline);

        [LibraryImport(Library, EntryPoint = "agsn_gui_draw")]
        public static partial IntPtr GuiDraw(in GuiDesc gui, GuiControl* controls, int controlCount, int highlightControl);

        [StructLayout(LayoutKind.Sequential)]
        public struct RoomInfo
        {
            public int GameId;
            public int DataVersion;
            public int Width;
            public int Height;
            public int MaskResolution;
            public int LegacyResolution;
            public int BackgroundBpp;
            public int BgFrameCount;
            public int BgAnimSpeed;
            public int EdgeLeft, EdgeRight, EdgeTop, EdgeBottom;
            public int StartupMusic;
            public int SaveLoadDisabled;
            public int PlayerCharOff;
            public int PlayerView;
            public int MusicVolume;
            public int Flags;
            public int HotspotCount;
            public int ObjectCount;
            public int RegionCount;
            public int WalkAreaCount;
            public int WalkBehindCount;
            public int MessageCount;
            public int LocalVarCount;
            public int HasLegacyInteractions;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RoomObject
        {
            public int X, Y;
            public int Sprite;
            public int IsOn;
            public int Baseline;
            public int Transparency;
            public int Flags;
            public int BlockX, BlockY, BlockWidth, BlockHeight;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RoomHotspot
        {
            public int WalkToX, WalkToY;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RoomRegion
        {
            public int LightRaw;
            public int TintRaw;
            public int LightLevel;
            public int HasTint;
            public int TintLuminance;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RoomWalkArea
        {
            public int ScalingFar;
            public int ScalingNear;
            public int PlayerView;
            public int Top, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RoomMessageInfo
        {
            public int DisplayAs;
            public int Flags;
        }

        [LibraryImport(Library, EntryPoint = "agsn_room_load", StringMarshalling = StringMarshalling.Utf8)]
        public static partial IntPtr RoomLoad(string path, int gameIsHires, SpriteMetric* sprites, int spriteCount);

        [LibraryImport(Library, EntryPoint = "agsn_room_free")]
        public static partial void RoomFree(IntPtr room);

        [LibraryImport(Library, EntryPoint = "agsn_room_get_info")]
        public static partial int RoomGetInfo(IntPtr room, out RoomInfo info);

        [LibraryImport(Library, EntryPoint = "agsn_room_get_object")]
        public static partial int RoomGetObject(IntPtr room, int index, out RoomObject obj);

        [LibraryImport(Library, EntryPoint = "agsn_room_get_hotspot")]
        public static partial int RoomGetHotspot(IntPtr room, int index, out RoomHotspot hotspot);

        [LibraryImport(Library, EntryPoint = "agsn_room_get_region")]
        public static partial int RoomGetRegion(IntPtr room, int index, out RoomRegion region);

        [LibraryImport(Library, EntryPoint = "agsn_room_get_walkarea")]
        public static partial int RoomGetWalkArea(IntPtr room, int index, out RoomWalkArea area);

        [LibraryImport(Library, EntryPoint = "agsn_room_get_walkbehind_baseline")]
        public static partial int RoomGetWalkBehindBaseline(IntPtr room, int index, out int baseline);

        [LibraryImport(Library, EntryPoint = "agsn_room_get_message_info")]
        public static partial int RoomGetMessageInfo(IntPtr room, int index, out RoomMessageInfo info);

        [LibraryImport(Library, EntryPoint = "agsn_room_get_local_var_value")]
        public static partial int RoomGetLocalVarValue(IntPtr room, int index, out int value);

        [LibraryImport(Library, EntryPoint = "agsn_room_get_text")]
        public static partial IntPtr RoomGetText(IntPtr room, int kind, int index);

        [LibraryImport(Library, EntryPoint = "agsn_room_get_property_count")]
        public static partial int RoomGetPropertyCount(IntPtr room, int owner, int index);

        [LibraryImport(Library, EntryPoint = "agsn_room_get_property")]
        public static partial int RoomGetProperty(IntPtr room, int owner, int index, int property, out IntPtr name, out IntPtr value);

        [LibraryImport(Library, EntryPoint = "agsn_room_get_script_module")]
        public static partial IntPtr RoomGetScriptModule(IntPtr room, int owner, int index);

        [LibraryImport(Library, EntryPoint = "agsn_room_get_event_count")]
        public static partial int RoomGetEventCount(IntPtr room, int owner, int index);

        [LibraryImport(Library, EntryPoint = "agsn_room_get_event_function")]
        public static partial IntPtr RoomGetEventFunction(IntPtr room, int owner, int index, int evt);

        [LibraryImport(Library, EntryPoint = "agsn_room_get_str_option", StringMarshalling = StringMarshalling.Utf8)]
        public static partial IntPtr RoomGetStrOption(IntPtr room, string key);

        [LibraryImport(Library, EntryPoint = "agsn_room_get_background")]
        public static partial IntPtr RoomGetBackground(IntPtr room, int frame);

        [LibraryImport(Library, EntryPoint = "agsn_room_get_mask")]
        public static partial IntPtr RoomGetMask(IntPtr room, int mask);

        [LibraryImport(Library, EntryPoint = "agsn_room_set_info")]
        public static partial int RoomSetInfo(IntPtr room, in RoomInfo info);

        [LibraryImport(Library, EntryPoint = "agsn_room_set_object")]
        public static partial int RoomSetObject(IntPtr room, int index, in RoomObject obj);

        [LibraryImport(Library, EntryPoint = "agsn_room_set_hotspot")]
        public static partial int RoomSetHotspot(IntPtr room, int index, in RoomHotspot hotspot);

        [LibraryImport(Library, EntryPoint = "agsn_room_set_region")]
        public static partial int RoomSetRegion(IntPtr room, int index, in RoomRegion region);

        [LibraryImport(Library, EntryPoint = "agsn_room_set_walkarea")]
        public static partial int RoomSetWalkArea(IntPtr room, int index, in RoomWalkArea area);

        [LibraryImport(Library, EntryPoint = "agsn_room_set_walkbehind_baseline")]
        public static partial int RoomSetWalkBehindBaseline(IntPtr room, int index, int baseline);

        [LibraryImport(Library, EntryPoint = "agsn_room_set_message_info")]
        public static partial int RoomSetMessageInfo(IntPtr room, int index, in RoomMessageInfo info);

        [LibraryImport(Library, EntryPoint = "agsn_room_set_text")]
        public static partial int RoomSetText(IntPtr room, int kind, int index, byte* bytes, nuint length, int textProperty);

        [LibraryImport(Library, EntryPoint = "agsn_room_clear_properties")]
        public static partial int RoomClearProperties(IntPtr room, int owner, int index);

        [LibraryImport(Library, EntryPoint = "agsn_room_set_property", StringMarshalling = StringMarshalling.Utf8)]
        public static partial int RoomSetProperty(IntPtr room, int owner, int index, string name, byte* valueBytes, nuint valueLength);

        [LibraryImport(Library, EntryPoint = "agsn_room_set_events", StringMarshalling = StringMarshalling.Utf8)]
        public static partial int RoomSetEvents(IntPtr room, int owner, int index, string scriptModule, string[] functions, int functionCount);

        [LibraryImport(Library, EntryPoint = "agsn_room_set_str_option", StringMarshalling = StringMarshalling.Utf8)]
        public static partial int RoomSetStrOption(IntPtr room, string key, string value);

        [LibraryImport(Library, EntryPoint = "agsn_room_set_compiled_script")]
        public static partial int RoomSetCompiledScript(IntPtr room, byte* data, nuint size);

        [LibraryImport(Library, EntryPoint = "agsn_room_set_mask")]
        public static partial int RoomSetMask(IntPtr room, int mask, int width, int height, byte* pixels, nuint stride);

        [LibraryImport(Library, EntryPoint = "agsn_room_import_background", StringMarshalling = StringMarshalling.Utf8)]
        public static partial int RoomImportBackground(IntPtr room, int frame, string imagePath, int gameColorDepth,
            int minWidth, int minHeight, Rgba* gamePalette, byte* palUses, int useExactPalette, out int coloursLost);

        [LibraryImport(Library, EntryPoint = "agsn_room_extract_script_text", StringMarshalling = StringMarshalling.Utf8)]
        public static partial int RoomExtractScriptText(string roomPath, byte* buffer, nuint bufferSize, out nuint length, out int found);

        [LibraryImport(Library, EntryPoint = "agsn_template_extract_game", StringMarshalling = StringMarshalling.Utf8)]
        public static partial int TemplateExtractGame(string templatePath, string destDir);

        [LibraryImport(Library, EntryPoint = "agsn_template_extract_room", StringMarshalling = StringMarshalling.Utf8)]
        public static partial int TemplateExtractRoom(string templatePath, string destDir, int newRoomNumber);

        [LibraryImport(Library, EntryPoint = "agsn_room_create")]
        public static partial IntPtr RoomCreate(int width, int height, int maskResolution, int colorDepth, int backgroundCount);

        [LibraryImport(Library, EntryPoint = "agsn_room_delete_background")]
        public static partial int RoomDeleteBackground(IntPtr room, int frame);

        [LibraryImport(Library, EntryPoint = "agsn_room_save", StringMarshalling = StringMarshalling.Utf8)]
        public static partial int RoomSave(IntPtr room, string path);

        /// <summary>Copies a native zero-terminated byte string (any encoding); null for a null pointer.</summary>
        public static byte[] GetBytes(IntPtr text)
        {
            if (text == IntPtr.Zero) return null;
            return System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpanFromNullTerminated((byte*)text).ToArray();
        }

        public static string GetLastError()
        {
            return Marshal.PtrToStringUTF8(LastError()) ?? string.Empty;
        }
    }
}
