// ags4mac: GUI preview rendering for libagsnative, as the upstream editor draws GUIs.
//
// Port of agsnative.cpp's GUI preview: the editor-side globals (thisgame, spriteset, palette), the helper
// functions the Common GUI code expects from its host (draw_gui_sprite, get_adjusted_sprite*, coordinate
// helpers; see gui/guimain.h), font loading (GameFontUpdated/reload_font), ConvertGUIToBinaryFormat,
// drawGUI and drawGUIAt. The editor's GUI drawing glue ags/Editor/Native/acgui_agsnative.cpp (outlined
// text, inventory/text box previews) is compiled unchanged. GUIs are drawn by the Common GUI classes with
// the game's real fonts and sprites; the result is a 32-bit image with the GUI's transparency as alpha.
#include <allegro.h>
#include <algorithm>
#include <cstdio>
#include <cstring>
#include <memory>
#include <vector>
#include "agsn_common.h"
#include "ac/gamesetupstruct.h"
#include "ac/spritecache.h"
#include "data/assetmanager.h"
#include "font/agsfontrenderer.h"
#include "font/fonts.h"
#include "gfx/bitmap.h"
#include "gui/guibutton.h"
#include "gui/guiinv.h"
#include "gui/guilabel.h"
#include "gui/guilistbox.h"
#include "gui/guimain.h"
#include "gui/guislider.h"
#include "gui/guitextbox.h"
#include "util/file.h"

using namespace AGS::Common;
typedef AGS::Common::Bitmap AGSBitmap;

// ---- editor globals used by the Common GUI code and acgui_agsnative.cpp ----

GameSetupStruct thisgame;
static RGB *palette = nullptr;
static int dsc_want_hires = 0;

static AGSBitmap *initialize_sprite(sprkey_t, AGSBitmap *image, uint32_t &)
{
    return image;
}
static SpriteCache::Callbacks spritecallbacks = { nullptr, initialize_sprite, nullptr, nullptr };
static SpriteCache spriteset(thisgame.SpriteInfos, spritecallbacks);

namespace
{
    std::unique_ptr<AssetManager> AssetMgr;
    bool Initialized = false;
    GUIMain TempGui;
    std::vector<AGS::Common::GUIButton> GuiButs;
    std::vector<GUIInvWindow> GuiInv;
    std::vector<AGS::Common::GUILabel> GuiLabels;
    std::vector<AGS::Common::GUIListBox> GuiList;
    std::vector<AGS::Common::GUISlider> GuiSlider;
    std::vector<AGS::Common::GUITextBox> GuiText;
}

// Safely gets a sprite, if the sprite does not exist then returns a placeholder (sprite 0)
static AGSBitmap *get_sprite(int spnr)
{
    if (!spriteset.DoesSpriteExist(spnr))
        return spriteset[0];
    return spriteset[spnr];
}

int data_to_game_coord(int coord)
{
    return coord * thisgame.GetDataUpscaleMult();
}

int game_to_data_coord(int coord)
{
    return coord / thisgame.GetDataUpscaleMult();
}

void data_to_game_coords(int *x, int *y)
{
    const int mul = thisgame.GetDataUpscaleMult();
    *x *= mul;
    *y *= mul;
}

int get_fixed_pixel_size(int coord)
{
    return coord * thisgame.GetDataUpscaleMult();
}

void set_our_eip(int) {}

static int ctx_data_to_game_size(int val, bool hires_ctx)
{
    if (hires_ctx && !thisgame.IsLegacyHiRes())
        return std::max(1, (val / HIRES_COORD_MULTIPLIER));
    if (!hires_ctx && thisgame.IsLegacyHiRes())
        return val * HIRES_COORD_MULTIPLIER;
    return val;
}

int get_adjusted_spritewidth(int spr)
{
    AGSBitmap *tsp = get_sprite(spr);
    if (tsp == nullptr)
        return 0;
    int retval = tsp->GetWidth();
    if (!thisgame.AllowRelativeRes() || !thisgame.SpriteInfos[spr].IsRelativeRes())
        return retval;
    return ctx_data_to_game_size(retval, thisgame.SpriteInfos[spr].IsLegacyHiRes());
}

int get_adjusted_spriteheight(int spr)
{
    AGSBitmap *tsp = get_sprite(spr);
    if (tsp == nullptr)
        return 0;
    int retval = tsp->GetHeight();
    if (!thisgame.AllowRelativeRes() || !thisgame.SpriteInfos[spr].IsRelativeRes())
        return retval;
    return ctx_data_to_game_size(retval, thisgame.SpriteInfos[spr].IsLegacyHiRes());
}

static void wputblock_stretch(AGSBitmap *g, int xpt, int ypt, AGSBitmap *tblock, int nsx, int nsy)
{
    if (tblock->GetBPP() != thisgame.color_depth)
    {
        std::unique_ptr<AGSBitmap> tempst(BitmapHelper::CreateBitmapCopy(tblock, thisgame.color_depth * 8));
        for (int ww = 0; ww < tblock->GetWidth(); ww++)
            for (int vv = 0; vv < tblock->GetHeight(); vv++)
                if (tblock->GetPixel(ww, vv) == tblock->GetMaskColor())
                    tempst->PutPixel(ww, vv, tempst->GetMaskColor());
        g->StretchBlt(tempst.get(), RectWH(xpt, ypt, nsx, nsy), kBitmap_Transparency);
    }
    else
        g->StretchBlt(tblock, RectWH(xpt, ypt, nsx, nsy), kBitmap_Transparency);
}

// draw_gui_sprite is supported formally, without actual blending and other effects (as in the editor)
static void draw_gui_sprite_impl(AGSBitmap *g, int sprnum, AGSBitmap *blptr, int atxp, int atyp)
{
    AGSBitmap *towrite = blptr;
    std::unique_ptr<AGSBitmap> converted;
    const int main_color_depth = thisgame.color_depth * 8;
    if ((blptr->GetBPP() > 1) && (main_color_depth == 8))
    {
        converted.reset(BitmapHelper::CreateBitmap(blptr->GetWidth(), blptr->GetHeight(), 8));
        towrite = converted.get();
        towrite->Clear(towrite->GetMaskColor());
        for (int xxp = 0; xxp < blptr->GetWidth(); xxp++)
        {
            for (int yyp = 0; yyp < blptr->GetHeight(); yyp++)
            {
                int tmv = blptr->GetPixel(xxp, yyp);
                if (tmv != blptr->GetMaskColor())
                    towrite->PutPixel(xxp, yyp, makecol8(getr16(tmv), getg16(tmv), getb16(tmv)));
            }
        }
    }

    int nwid = towrite->GetWidth(), nhit = towrite->GetHeight();
    if ((sprnum >= 0) && thisgame.AllowRelativeRes() && thisgame.SpriteInfos[sprnum].IsRelativeRes())
    {
        if (thisgame.SpriteInfos[sprnum].IsLegacyHiRes())
        {
            if (dsc_want_hires == 0)
            {
                nwid /= 2;
                nhit /= 2;
            }
        }
        else if (dsc_want_hires)
        {
            nwid *= 2;
            nhit *= 2;
        }
    }
    wputblock_stretch(g, atxp, atyp, towrite, nwid, nhit);
}

void draw_gui_sprite(AGSBitmap *g, int sprnum, int atxp, int atyp, bool, BlendMode)
{
    draw_gui_sprite_impl(g, sprnum, get_sprite(sprnum), atxp, atyp);
}

void draw_gui_sprite(AGSBitmap *g, bool, int atxp, int atyp, AGSBitmap *blptr, bool, BlendMode, int)
{
    draw_gui_sprite_impl(g, -1, blptr, atxp, atyp);
}

namespace
{
    void ResetSpriteFile()
    {
        auto sprite_file = AssetMgr->OpenAsset(SpriteFile::DefaultSpriteFileName);
        if (!sprite_file)
            return; // no sprites: GUI images are skipped
        auto index_file = AssetMgr->OpenAsset(SpriteFile::DefaultSpriteIndexName);
        HError err = spriteset.InitFile(std::move(sprite_file), std::move(index_file));
        if (err)
            spriteset.SetMaxCacheSize(100 * 1024 * 1024);
    }

    void ClearControls()
    {
        GuiButs.clear();
        GuiInv.clear();
        GuiLabels.clear();
        GuiList.clear();
        GuiSlider.clear();
        GuiText.clear();
    }
}

extern "C" {

int agsn_gui_init(const char *game_dir, int32_t color_depth, int32_t antialias_fonts, const agsn_rgba *palette256)
{
    agsn::ApiLock lock;
    agsn::EnsureInitialized();
    agsn::ClearError();
    if (!game_dir || (color_depth != 8 && color_depth != 16 && color_depth != 32))
        return agsn::Fail(AGSN_E_INVALID_ARG, "invalid game folder or colour depth");

    if (Initialized)
    {
        free_all_fonts();
        shutdown_font_renderer();
        spriteset.Reset();
    }
    ClearControls();
    TempGui = GUIMain();

    thisgame = GameSetupStruct();
    thisgame.color_depth = color_depth / 8;
    thisgame.options[OPT_ANTIALIASFONTS] = antialias_fonts ? 1 : 0;
    thisgame.numfonts = 0;
    if (palette256)
    {
        for (int i = 0; i < 256; ++i)
            thisgame.defpal[i] = { static_cast<unsigned char>(palette256[i].r / 4), static_cast<unsigned char>(palette256[i].g / 4),
                                   static_cast<unsigned char>(palette256[i].b / 4), 0 };
    }
    palette = &thisgame.defpal[0];

    AssetMgr.reset(new AssetManager());
    AssetMgr->AddLibrary(game_dir);
    ResetSpriteFile();
    init_font_renderer(AssetMgr.get());
    Initialized = true;
    return AGSN_OK;
}

int agsn_gui_set_font(int32_t font_number, const agsn_font_info *font)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    if (!Initialized)
        return agsn::Fail(AGSN_E_INVALID_ARG, "agsn_gui_init was not called");
    if (font_number < 0 || !font)
        return agsn::Fail(AGSN_E_INVALID_ARG, "invalid font");
    if (font_number >= thisgame.numfonts)
    {
        thisgame.numfonts = font_number + 1;
        thisgame.fonts.resize(thisgame.numfonts);
    }
    // GameFontUpdated
    FontInfo &font_info = thisgame.fonts[font_number];
    font_info.Size = font->point_size;
    font_info.SizeMultiplier = font->size_multiplier;
    font_info.YOffset = font->vertical_offset;
    font_info.LineSpacing = font->line_spacing;
    font_info.CharacterSpacing = font->character_spacing;
    int flags = 0;
    if (font->point_size == 0)
        flags |= FFLG_SIZEMULTIPLIER;
    if (font->height_defined_by == AGSN_FONT_HEIGHT_NOMINAL)
        flags |= FFLG_LOGICALNOMINALHEIGHT;
    else if (font->height_defined_by == AGSN_FONT_HEIGHT_CUSTOM)
        flags |= FFLG_LOGICALCUSTOMHEIGHT;
    if (font->ascender_fixup)
        flags |= FFLG_ASCENDERFIXUP;
    font_info.Flags = flags;
    switch (font->outline_style)
    {
    case AGSN_FONT_OUTLINE_AUTOMATIC: font_info.Outline = FONT_OUTLINE_AUTO; break;
    case AGSN_FONT_OUTLINE_FONT: font_info.Outline = font->outline_font; break;
    default: font_info.Outline = FONT_OUTLINE_NONE; break;
    }
    font_info.AutoOutlineStyle = static_cast<enum FontInfo::AutoOutlineStyle>(font->auto_outline_style);
    font_info.AutoOutlineThickness = font->auto_outline_thickness;
    // reload_font
    if (!load_font_size(font_number, font_info))
        return agsn::Fail(AGSN_E_FORMAT, "unable to load font " + std::to_string(font_number));
    return AGSN_OK;
}

agsn_image *agsn_gui_draw(const agsn_gui_desc *gui, const agsn_gui_control *controls, int32_t control_count,
                          int32_t highlight_control)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    if (!Initialized || !gui || control_count < 0 || (control_count > 0 && !controls))
    {
        agsn::Fail(AGSN_E_INVALID_ARG, "invalid arguments (or agsn_gui_init was not called)");
        return nullptr;
    }

    // drawGUI
    ClearControls();
    GUI::DataVersion = kGameVersion_Current;
    GUI::GameGuiVersion = kGuiVersion_Current;
    GUI::Context.Spriteset = &spriteset;

    // ConvertGUIToBinaryFormat (texts are UTF-8)
    GUIMain &g = TempGui;
    g = GUIMain();
    if (!gui->is_text_window)
    {
        g.SetX(gui->x);
        g.SetY(gui->y);
        g.SetWidth(gui->width);
        g.SetHeight(gui->height);
        g.SetClickable(gui->clickable != 0);
        g.SetVisible(gui->visible != 0);
        g.SetPopupAtY(gui->popup_y);
        g.SetPopupStyle(static_cast<GUIPopupStyle>(gui->popup_style));
        g.SetZOrder(gui->z_order);
        g.SetFgColor(gui->fg_color);
        g.SetTransparencyAsPercentage(gui->transparency);
    }
    else
    {
        g.SetWidth(gui->width);
        g.SetHeight(gui->height);
        g.SetTextWindow(true);
        g.SetPopupStyle(kGUIPopupModal);
        g.SetPadding(gui->padding);
        g.SetFgColor(gui->fg_color);
    }
    g.SetBgColor(gui->bg_color);
    g.SetBgImage(gui->bg_image);
    g.RemoveAllControls();

    // Reserve so that the addresses passed to AddControl stay valid.
    GuiButs.reserve(control_count);
    GuiInv.reserve(control_count);
    GuiLabels.reserve(control_count);
    GuiList.reserve(control_count);
    GuiSlider.reserve(control_count);
    GuiText.reserve(control_count);
    for (int32_t i = 0; i < control_count; ++i)
    {
        const agsn_gui_control &c = controls[i];
        const String text = c.text ? String(c.text) : String();
        switch (c.type)
        {
        case AGSN_GUI_BUTTON:
        case AGSN_GUI_TEXTWINDOWEDGE:
        {
            AGS::Common::GUIButton nbut;
            if (c.type == AGSN_GUI_BUTTON)
            {
                nbut.SetBorderShadeColor(c.border_shade_color);
                nbut.SetTextColor(c.text_color);
                nbut.SetTextOutlineColor(c.text_outline_color);
                nbut.SetMouseOverBackColor(c.mouseover_back_color);
                nbut.SetPushedBackColor(c.pushed_back_color);
                nbut.SetMouseOverBorderColor(c.mouseover_border_color);
                nbut.SetPushedBorderColor(c.pushed_border_color);
                nbut.SetMouseOverTextColor(c.mouseover_text_color);
                nbut.SetPushedTextColor(c.pushed_text_color);
                nbut.SetDynamicColors(c.dynamic_colors != 0);
                nbut.SetFlatStyle(c.flat_style != 0);
                nbut.SetFont(c.font);
                nbut.SetMouseOverImage(c.mouseover_image);
                nbut.SetPushedImage(c.pushed_image);
                nbut.SetTextAlignment(static_cast<FrameAlignment>(c.text_alignment));
                nbut.SetWrapText(c.wrap_text != 0);
                nbut.SetClipImage(c.clip_image != 0);
                nbut.SetText(text);
            }
            nbut.SetNormalImage(c.image);
            GuiButs.push_back(nbut);
            g.AddControl(kGUIButton, GuiButs.size() - 1, &GuiButs.back());
            break;
        }
        case AGSN_GUI_LABEL:
        {
            AGS::Common::GUILabel nlabel;
            nlabel.SetTextColor(c.text_color);
            nlabel.SetTextOutlineColor(c.text_outline_color);
            nlabel.SetFont(c.font);
            nlabel.SetTextAlignment(static_cast<FrameAlignment>(c.text_alignment));
            nlabel.SetText(text);
            GuiLabels.push_back(nlabel);
            g.AddControl(kGUILabel, GuiLabels.size() - 1, &GuiLabels.back());
            break;
        }
        case AGSN_GUI_TEXTBOX:
        {
            AGS::Common::GUITextBox ntext;
            ntext.SetTextColor(c.text_color);
            ntext.SetTextOutlineColor(c.text_outline_color);
            ntext.SetFont(c.font);
            ntext.SetTextAlignment(static_cast<FrameAlignment>(c.text_alignment));
            ntext.SetText("Text Box Contents"); // dummy text, let user preview the fonts
            GuiText.push_back(ntext);
            g.AddControl(kGUITextBox, GuiText.size() - 1, &GuiText.back());
            break;
        }
        case AGSN_GUI_LISTBOX:
        {
            AGS::Common::GUIListBox nlist;
            nlist.SetTextColor(c.text_color);
            nlist.SetTextOutlineColor(c.text_outline_color);
            nlist.SetFont(c.font);
            nlist.SetSelectedTextColor(c.selected_text_color);
            nlist.SetSelectedBgColor(c.selected_bg_color);
            nlist.SetTextAlignment(static_cast<FrameAlignment>(c.text_alignment));
            nlist.SetTranslated(c.translated != 0);
            nlist.SetShowArrows(c.show_arrows != 0);
            // dummy items, let user preview the fonts
            nlist.AddItem("Sample selected");
            nlist.AddItem("Sample item");
            nlist.SetSelectedItem(0);
            GuiList.push_back(nlist);
            g.AddControl(kGUIListBox, GuiList.size() - 1, &GuiList.back());
            break;
        }
        case AGSN_GUI_SLIDER:
        {
            AGS::Common::GUISlider nslider;
            nslider.SetMinValue(c.min_value);
            nslider.SetMaxValue(c.max_value);
            nslider.SetValue(c.value);
            nslider.SetHandleImage(c.handle_image);
            nslider.SetHandleOffset(c.handle_offset);
            nslider.SetHandleColor(c.handle_color);
            nslider.SetBorderShadeColor(c.border_shade_color);
            nslider.SetBgImage(c.bg_image);
            GuiSlider.push_back(nslider);
            g.AddControl(kGUISlider, GuiSlider.size() - 1, &GuiSlider.back());
            break;
        }
        case AGSN_GUI_INVWINDOW:
        {
            GUIInvWindow ninv;
            ninv.SetCharacterID(c.character_id);
            ninv.SetItemWidth(c.item_width);
            ninv.SetItemHeight(c.item_height);
            GuiInv.push_back(ninv);
            g.AddControl(kGUIInvWindow, GuiInv.size() - 1, &GuiInv.back());
            break;
        }
        default:
            agsn::Fail(AGSN_E_INVALID_ARG, "unknown GUI control type");
            return nullptr;
        }

        GUIObject *newObj = g.GetControl(g.GetControlCount() - 1);
        newObj->SetX(c.x);
        newObj->SetY(c.y);
        newObj->SetSize(c.width, c.height);
        newObj->SetID(c.id);
        newObj->SetZOrder(c.z_order);
        newObj->SetSolidBackground(c.solid_background != 0);
        newObj->SetShowBorder(c.show_border != 0);
        newObj->SetBackColor(c.back_color);
        newObj->SetBorderColor(c.border_color);
        newObj->SetBorderWidth(c.border_width);
        newObj->SetPaddingX(c.padding_x);
        newObj->SetPaddingY(c.padding_y);
    }
    GUIRefCollection guictrl_refs(GuiButs, GuiInv, GuiLabels, GuiList, GuiSlider, GuiText);
    HError err = g.RebuildArray(guictrl_refs);
    if (!err)
    {
        agsn::Fail(AGSN_E_FORMAT, std::string("unable to set up the GUI: ") + err->FullMessage().GetCStr());
        return nullptr;
    }
    g.SetHighlightControl(highlight_control);

    // drawGUIAt (scale 1, controls opaque)
    if (g.GetWidth() < 1 || g.GetHeight() < 1)
    {
        agsn::Fail(AGSN_E_INVALID_ARG, "GUI has no size");
        return nullptr;
    }
    dsc_want_hires = 1;
    const int w = g.GetWidth(), h = g.GetHeight();
    std::unique_ptr<AGSBitmap> surf32(new AGSBitmap(w, h, 32));
    std::unique_ptr<AGSBitmap> ctrl32(new AGSBitmap(w, h, 32));
    std::unique_ptr<AGSBitmap> mainBmp, ctrlBmp;
    if (thisgame.color_depth != 4)
    {
        mainBmp.reset(new AGSBitmap(w, h, thisgame.color_depth * 8));
        ctrlBmp.reset(new AGSBitmap(w, h, thisgame.color_depth * 8));
    }
    if (thisgame.color_depth == 1)
        set_palette(palette);

    AGSBitmap *guimain_bmp = (thisgame.color_depth == 4) ? surf32.get() : mainBmp.get();
    guimain_bmp->ResetClip();
    guimain_bmp->Clear(guimain_bmp->GetMaskColor());
    g.DrawSelf(guimain_bmp);
    AGSBitmap *guictrl_bmp = (thisgame.color_depth == 4) ? ctrl32.get() : ctrlBmp.get();
    guictrl_bmp->ResetClip();
    guictrl_bmp->Clear(guictrl_bmp->GetMaskColor());
    g.DrawControls(guictrl_bmp);

    if (thisgame.color_depth != 4)
    {
        const int old_conv = get_color_conversion();
        set_color_conversion(old_conv | COLORCONV_KEEP_TRANS);
        surf32->Blit(mainBmp.get());
        ctrl32->Blit(ctrlBmp.get());
        set_color_conversion(old_conv);
    }
    surf32->MaskedBlit(ctrl32.get());
    dsc_want_hires = 0;

    // Mask colour becomes transparent; everything else opaque unless it carries alpha (32-bit games).
    const uint32_t mask = surf32->GetMaskColor();
    std::unique_ptr<agsn_image> image(new agsn_image());
    image->pixels = PixelBuffer(w, h, kPxFmt_A8R8G8B8);
    for (int y = 0; y < h; ++y)
    {
        const uint32_t *src = reinterpret_cast<const uint32_t*>(surf32->GetScanLine(y));
        uint32_t *dst = reinterpret_cast<uint32_t*>(image->pixels.GetLine(y));
        for (int x = 0; x < w; ++x)
        {
            uint32_t px = src[x];
            if ((px & 0x00FFFFFFu) == (mask & 0x00FFFFFFu))
                dst[x] = 0;
            else if (thisgame.color_depth == 4 && (px >> 24) != 0)
                dst[x] = px;
            else
                dst[x] = px | 0xFF000000u;
        }
    }
    image->sourceFormat = kPxFmt_A8R8G8B8;
    image->sourceBits = 32;
    return image.release();
}

int agsn_font_find_ttf_size(const char *file_name, int32_t pixel_height, int32_t *point_size)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    if (!Initialized || !file_name || !point_size || pixel_height <= 0)
        return agsn::Fail(AGSN_E_INVALID_ARG, "invalid arguments (or agsn_gui_init was not called)");
    // measure_font_height (Editor/AGS.Native/agsnative.cpp)
    FontMetrics metrics;
    if (!load_font_metrics(file_name, pixel_height, metrics))
        return agsn::Fail(AGSN_E_FORMAT, std::string("Unable to load font ") + file_name + ". Not a TTF font, or there an error occured while loading it.");
    *point_size = metrics.NominalHeight;
    return AGSN_OK;
}

// import_sci_font (Editor/AGS.Native/agsnative.cpp): converts a Sierra SCI font to the WFN format. Code as
// upstream, except that the WFN file is given (upstream writes agsfnt<slot>.wfn into the current folder)
// and the font is reloaded by the caller (agsn_gui_set_font) instead of load_font_size here.
static int import_sci_font(const String &filename, const String &dst_fn)
{
    // Read SCI font into memory
    std::unique_ptr<Stream> in(File::OpenFileRead(filename));
    if (!in)
        return agsn::Fail(AGSN_E_IO, "File not found");
    if (in->ReadByte() != 0x87) // format check
        return agsn::Fail(AGSN_E_FORMAT, "Not a valid SCI font file");
    in->Seek(3);
    const size_t char_count = in->ReadInt16(); // number of characters
    in->ReadInt16(); // line height (?)
    std::vector<int16_t> sci_offs(char_count); // table of contents
    in->ReadArrayOfInt16(&sci_offs.front(), char_count);
    const soff_t char_data_at = in->GetPosition();
    std::vector<uint8_t> widths(char_count); // char widths
    std::vector<uint8_t> heights(char_count); // char heights
    std::vector<uint8_t> pxbuf; // pixel data (stored in one line)
    for (size_t i = 0; i < char_count; ++i)
    {
        // TODO: find the SCI specs, why offsets have to be shifted by 2 bytes?
        soff_t off = sci_offs[i] + 2;
        if (off < char_data_at)
            return agsn::Fail(AGSN_E_FORMAT, "Invalid character offset found in SCI file");
        in->Seek(off, kSeekBegin);
        int w = in->ReadInt8() - 1; // CHECKME: why has +1 in file?
        int h = in->ReadInt8();
        widths[i] = w;
        heights[i] = h;
        if ((w < 1) || (h < 1))
            continue; // character has zero size
        size_t row_size = (w / 8) + 1;
        size_t px_size = row_size * h;
        pxbuf.resize(pxbuf.size() + px_size);
        in->Read(&pxbuf[0] + (pxbuf.size() - px_size), px_size);
    }
    in.reset();

    // Write as WFN format on disk
    // TODO: merge this code with WFNFont class
    std::unique_ptr<Stream> out(File::CreateFile(dst_fn));
    if (!out)
        return agsn::Fail(AGSN_E_IO, std::string("Unable to create ") + dst_fn.GetCStr());
    out->Write("WGT Font File  ", 15);
    out->WriteInt16(0);  // will be table address
    std::vector<uint16_t> wfn_offs(char_count);
    for (size_t i = 0, buf_off = 0; i < char_count; ++i)
    {
        wfn_offs[i] = out->GetPosition();
        int w = widths[i];
        int h = heights[i];
        out->WriteInt16(w + 1); // CHECKME: why has +1 in file?
        out->WriteInt16(h);
        if ((w < 1) || (h < 1))
            continue; // character has zero size
        size_t row_size = (w / 8) + 1;
        size_t px_size = row_size * h;
        out->Write(&pxbuf[0] + buf_off, px_size);
        buf_off += px_size;
    }
    // Seek back to the header, and write table position
    uint16_t tableat = static_cast<uint16_t>(out->GetPosition());
    out->WriteArrayOfInt16(reinterpret_cast<int16_t*>(&wfn_offs.front()), char_count);
    out->Seek(15, kSeekBegin);
    out->WriteInt16(tableat);
    out.reset();
    return AGSN_OK;
}

int agsn_font_import_sci(const char *sci_file, const char *wfn_file)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    if (!sci_file || !wfn_file)
        return agsn::Fail(AGSN_E_INVALID_ARG, "invalid arguments");
    return import_sci_font(sci_file, wfn_file);
}

int agsn_font_get_metrics(int32_t font_number, agsn_font_metrics *metrics)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    if (!Initialized || !metrics || !is_font_loaded(font_number))
        return agsn::Fail(AGSN_E_INVALID_ARG, "font is not loaded");
    metrics->height = get_font_height(font_number);
    metrics->real_height = get_font_real_height(font_number);
    metrics->line_spacing = get_font_linespacing(font_number);
    metrics->topmost_char = get_font_topmost_char_code(font_number);
    metrics->is_bitmap_font = is_bitmap_font(font_number) ? 1 : 0;
    const char *name = get_font_name(font_number);
    std::snprintf(metrics->name, sizeof(metrics->name), "%s", name ? name : "");
    return AGSN_OK;
}

agsn_image *agsn_font_draw_text(int32_t font_number, const char *text, int32_t max_width, int32_t draw_outline)
{
    agsn::ApiLock lock;
    agsn::ClearError();
    if (!Initialized || !text || !is_font_loaded(font_number))
    {
        agsn::Fail(AGSN_E_INVALID_ARG, "font is not loaded");
        return nullptr;
    }
    // As DrawTextUsingFontAt in the upstream editor: split into lines, white text, dark grey outline.
    SplitLines lines;
    split_lines(text, lines, max_width > 0 ? max_width : INT32_MAX / 2, font_number);
    const int linespacing = std::max(1, get_font_linespacing(font_number));
    const int outline = std::max(0, get_font_outline_thickness(font_number));
    int width = 1;
    for (size_t i = 0; i < lines.Count(); ++i)
        width = std::max(width, get_text_width_outlined(lines[i].GetCStr(), font_number));
    const int height = std::max<int>(1, static_cast<int>(lines.Count() - 1) * linespacing + get_font_surface_height_outlined(font_number));
    width += 2 * outline;

    std::unique_ptr<AGSBitmap> block(new AGSBitmap(width, height, 32));
    block->Clear(block->GetMaskColor());
    const color_t text_color = makeacol32(255, 255, 255, 255);
    const color_t outline_color = makeacol32(64, 64, 64, 255);
    for (size_t i = 0; i < lines.Count(); ++i)
    {
        if (draw_outline)
            wouttext_outline(block.get(), 0, static_cast<int>(i) * linespacing, font_number, text_color, outline_color, lines[i].GetCStr());
        else
            wouttextxy(block.get(), 0, static_cast<int>(i) * linespacing, font_number, text_color, lines[i].GetCStr());
    }

    const uint32_t mask = block->GetMaskColor();
    std::unique_ptr<agsn_image> image(new agsn_image());
    image->pixels = PixelBuffer(width, height, kPxFmt_A8R8G8B8);
    for (int y = 0; y < height; ++y)
    {
        const uint32_t *src = reinterpret_cast<const uint32_t*>(block->GetScanLine(y));
        uint32_t *dst = reinterpret_cast<uint32_t*>(image->pixels.GetLine(y));
        for (int x = 0; x < width; ++x)
            dst[x] = ((src[x] & 0x00FFFFFFu) == (mask & 0x00FFFFFFu)) ? 0u : (src[x] | ((src[x] >> 24) == 0 ? 0xFF000000u : 0u));
    }
    image->sourceFormat = kPxFmt_A8R8G8B8;
    image->sourceBits = 32;
    return image.release();
}

} // extern "C"
