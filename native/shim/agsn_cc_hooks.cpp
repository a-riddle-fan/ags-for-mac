// ags4mac: host-provided functions that ags/Common's script code expects (see script/cc_common.h).
// Same behaviour as the upstream editor's AGS.Native (agsnative.cpp).
#include "script/cc_common.h"
#include "util/string.h"

using AGS::Common::String;

String cc_format_error(const String &message)
{
    if (currentline > 0)
        return String::FromFormat("Error (line %d): %s", currentline, message.GetCStr());
    else
        return String::FromFormat("Error (line unknown): %s", message.GetCStr());
}

String cc_get_callstack(int /*max_lines*/)
{
    return "";
}
