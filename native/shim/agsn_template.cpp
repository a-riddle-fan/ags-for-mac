// ags4mac: game and room templates for libagsnative.
//
// Port of the upstream editor's extract_template_files_impl / extract_template_files /
// extract_room_template_files (AGS.Native/agsnative.cpp): templates are asset packs (.agt for games,
// .art for rooms) read through the AssetManager.
#include <algorithm>
#include <memory>
#include <string>
#include <vector>
#include "agsn_common.h"
#include "data/assetmanager.h"
#include "util/directory.h"
#include "util/file.h"
#include "util/path.h"
#include "util/stream.h"
#include "util/string_utils.h"

using namespace AGS::Common;

namespace
{
    const char *TEMPLATE_LOCK_FILE = "template.dta";
    const char *ROOM_TEMPLATE_ID_FILE = "rtemplate.dat";

    int ExtractImpl(const char *template_path, const char *dest_dir, const std::vector<String> &check_list,
                    const char *check_list_error, const std::vector<String> &exclude_list, const String &asset_output_format)
    {
        const AssetLibInfo *lib = nullptr;
        std::unique_ptr<AssetManager> templateMgr(new AssetManager());
        AssetError err = templateMgr->AddLibrary(template_path, &lib);
        if (err != kAssetNoError)
            return agsn::Fail(AGSN_E_FORMAT, std::string("Failed to read the template file: ") + GetAssetErrorText(err).GetCStr());

        // If check_list is provided, then the package must include at least one of the files
        if (!check_list.empty())
        {
            bool at_least_one_check = false;
            for (const auto &check : check_list)
                at_least_one_check |= templateMgr->DoesAssetExist(check);
            if (!at_least_one_check)
                return agsn::Fail(AGSN_E_FORMAT, check_list_error);
        }

        for (const auto &asset : lib->AssetInfos)
        {
            const String thisFile = asset.FileName;
            if (thisFile.IsEmpty())
                continue;
            // Don't extract excluded files
            if (std::find_if(exclude_list.begin(), exclude_list.end(), StrEqNoCasePred(thisFile)) != exclude_list.end())
                continue;

            std::unique_ptr<Stream> readin(templateMgr->OpenAsset(thisFile));
            if (!readin)
                return agsn::Fail(AGSN_E_IO, std::string("Failed to open template asset '") + thisFile.GetCStr() + "' for reading.");

            String outputName = thisFile;
            // If format pattern is provided, then rename the output file
            if (!asset_output_format.IsEmpty())
            {
                String ext = Path::GetFileExtension(thisFile);
                outputName = String::FromFormat(asset_output_format.GetCStr(), ext.GetCStr());
            }
            outputName.Replace('\\', '/');
            // Make sure to create necessary subfolders
            Directory::CreateAllDirectories(dest_dir, Path::GetDirectoryPath(outputName));
            String fullOutput = Path::ConcatPaths(dest_dir, outputName);
            std::unique_ptr<Stream> wrout(File::CreateFile(fullOutput));
            if (!wrout)
                return agsn::Fail(AGSN_E_IO, std::string("Failed to open file '") + fullOutput.GetCStr() + "' for writing.");
            const soff_t src_len = readin->GetLength();
            soff_t result = CopyStream(readin.get(), wrout.get(), src_len);
            if (result < src_len)
                return agsn::Fail(AGSN_E_IO, std::string("Failed to extract file '") + thisFile.GetCStr() + "'.");
        }
        return AGSN_OK;
    }
}

extern "C" {

int agsn_template_extract_game(const char *template_path, const char *dest_dir)
{
    agsn::ApiLock lock;
    agsn::EnsureInitialized();
    agsn::ClearError();
    if (!template_path || !dest_dir)
        return agsn::Fail(AGSN_E_INVALID_ARG, "template or destination is null");
    // extract_template_files
    std::vector<String> check_list = { "editor.dat", "game.agf" };
    std::vector<String> exclude_list = { TEMPLATE_LOCK_FILE };
    return ExtractImpl(template_path, dest_dir, check_list, "Template file does not contain main project data.",
                       exclude_list, {});
}

int agsn_template_extract_room(const char *template_path, const char *dest_dir, int32_t new_room_number)
{
    agsn::ApiLock lock;
    agsn::EnsureInitialized();
    agsn::ClearError();
    if (!template_path || !dest_dir || new_room_number < 0)
        return agsn::Fail(AGSN_E_INVALID_ARG, "invalid arguments");
    // extract_room_template_files
    std::vector<String> check_list = { ROOM_TEMPLATE_ID_FILE };
    return ExtractImpl(template_path, dest_dir, check_list, "Template file does not contain necessary metadata.",
                       check_list, String::FromFormat("room%d.%%s", new_room_number));
}

} // extern "C"
