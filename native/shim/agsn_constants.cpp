// ags4mac: native constants for the editor, as upstream NativeMethods::GetNativeConstant().
// The integer table is generated from upstream's source at configure time (agsn_constants.inc).
#include <cstring>
#include "agsn_common.h"
#include "ac/characterinfo.h"
#include "ac/dialogtopic.h"
#include "ac/game_version.h"
#include "ac/gamesetupstruct.h"
#include "ac/gamestructdefines.h"
#include "ac/inventoryiteminfo.h"
#include "ac/mousecursor.h"
#include "ac/spritecache.h"
#include "ac/view.h"
#include "ac/wordsdictionary.h"
#include "data/data_helpers.h"
#include "font/fonts.h"
#include "game/customproperties.h"
#include "game/main_game_file.h"
#include "game/plugininfo.h"
#include "gui/guidefines.h"
#include "gui/guimain.h"
#include "gui/guibutton.h"
#include "gui/guilistbox.h"
#include "gui/guitextbox.h"
#include "util/string_utils.h"

using namespace AGS;
using namespace AGS::Common;

// Defined by the upstream editor's own native code (Editor/AGS.Native/agsnative.h), not by Common.
#ifndef MAX_STATIC_SPRITES
#define MAX_STATIC_SPRITES 90000
#endif

namespace
{
    struct IntConstant
    {
        const char *Name;
        int32_t Value;
    };

    const IntConstant IntConstants[] =
    {
#include "agsn_constants.inc"
    };
}

extern "C" {

int32_t agsn_get_constant(const char *name, int32_t *int_value, const char **data, int32_t *data_length)
{
    agsn::ApiLock lock;
    if (!name)
        return AGSN_CONST_NONE;

    if (std::strcmp(name, "GAME_FILE_SIG") == 0)
    {
        if (data) *data = MainGameSource::Signature.GetCStr();
        return AGSN_CONST_STRING;
    }
    if (std::strcmp(name, "SPRSET_NAME") == 0)
    {
        if (data) *data = "acsprset.spr"; // sprsetname in the upstream editor
        return AGSN_CONST_STRING;
    }
    if (std::strcmp(name, "PASSWORD_ENC_STRING") == 0)
    {
        if (data) *data = EncryptPassword;
        if (data_length) *data_length = static_cast<int32_t>(std::strlen(EncryptPassword));
        return AGSN_CONST_BYTES;
    }

    for (const IntConstant &c : IntConstants)
    {
        if (std::strcmp(name, c.Name) == 0)
        {
            if (int_value) *int_value = c.Value;
            return AGSN_CONST_INT;
        }
    }
    return AGSN_CONST_NONE;
}

} // extern "C"
