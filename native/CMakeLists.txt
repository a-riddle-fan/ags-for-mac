# ags4mac: libagsnative – C ABI over upstream AGS native code (ags/Common, ags/Compiler),
# consumed from .NET via P/Invoke (src/AGS.Native.Interop). Replaces the Windows-only
# C++/CLI AGS.Native of the upstream editor.
cmake_minimum_required(VERSION 3.19)

project(agsnative LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 14)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

# Source paths compiled into the binaries (__FILE__ in asserts and logs) are made relative to the ags4mac
# folder, so the built app doesn't contain the developer's home folder.
get_filename_component(AGSN_PROJECT_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/.." REALPATH)
add_compile_options("-ffile-prefix-map=${AGSN_PROJECT_ROOT}/=")

set(AGS_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../ags" CACHE PATH "Upstream AGS checkout")
file(REAL_PATH "${AGS_ROOT}" AGS_ROOT)

# Pull in the upstream build. Everything is EXCLUDE_FROM_ALL: building `agsnative` only builds the
# libraries it needs; scripts/build-upstream-tools.sh builds the (equally patched) tools and engine.
set(AGS_BUILD_ENGINE ON CACHE BOOL "" FORCE)
set(AGS_BUILD_TOOLS ON CACHE BOOL "" FORCE)
set(AGS_BUILD_COMPILER ON CACHE BOOL "" FORCE)
set(AGS_TESTS OFF CACHE BOOL "" FORCE)
add_subdirectory("${AGS_ROOT}" upstream EXCLUDE_FROM_ALL)

# Fixes for upstream bugs found while porting, kept as plain unified diffs in patches/ (ready to
# submit upstream). Each patched file is copied to the build tree, patched there and swapped into
# the upstream target; the ags/ checkout itself is never modified. Configuration fails if a patch
# no longer applies, e.g. after an upstream update (then check whether upstream fixed it).
find_program(PATCH_EXECUTABLE patch REQUIRED)

function(agsn_apply_upstream_patch target patch_file)
    set(patched_root "${CMAKE_BINARY_DIR}/patched-upstream")
    foreach(rel_path IN LISTS ARGN)
        set(original "${AGS_ROOT}/${rel_path}")
        set(copy "${patched_root}/${rel_path}")
        get_filename_component(copy_dir "${copy}" DIRECTORY)
        file(MAKE_DIRECTORY "${copy_dir}")
        file(COPY_FILE "${original}" "${copy}")
        set_property(DIRECTORY APPEND PROPERTY CMAKE_CONFIGURE_DEPENDS "${original}")
    endforeach()
    execute_process(
        COMMAND "${PATCH_EXECUTABLE}" -p1 --forward --batch --silent -d "${patched_root}" -i "${patch_file}"
        RESULT_VARIABLE result OUTPUT_VARIABLE out ERROR_VARIABLE err)
    if(NOT result EQUAL 0)
        message(FATAL_ERROR "Upstream patch ${patch_file} does not apply:\n${out}${err}")
    endif()
    set_property(DIRECTORY APPEND PROPERTY CMAKE_CONFIGURE_DEPENDS "${patch_file}")

    get_target_property(sources ${target} SOURCES)
    get_target_property(source_dir ${target} SOURCE_DIR)
    foreach(rel_path IN LISTS ARGN)
        set(original "${AGS_ROOT}/${rel_path}")
        set(replaced FALSE)
        set(new_sources "")
        foreach(src IN LISTS sources)
            get_filename_component(abs "${src}" ABSOLUTE BASE_DIR "${source_dir}")
            if(abs STREQUAL original)
                list(APPEND new_sources "${patched_root}/${rel_path}")
                set(replaced TRUE)
            else()
                list(APPEND new_sources "${src}")
            endif()
        endforeach()
        if(NOT replaced)
            message(FATAL_ERROR "${rel_path} is not a source of target ${target}")
        endif()
        set(sources "${new_sources}")
    endforeach()
    set_property(TARGET ${target} PROPERTY SOURCES "${sources}")
    # Patched copies live elsewhere; keep "sibling.h" includes resolving to the original folder.
    foreach(rel_path IN LISTS ARGN)
        get_filename_component(original_dir "${AGS_ROOT}/${rel_path}" DIRECTORY)
        target_include_directories(${target} PRIVATE "${original_dir}")
        # A patched header must win over the original: put its component root (e.g. Engine/) of the
        # patched tree first, so "debug/x.h" resolves to the patched copy.
        if(rel_path MATCHES "\\.h$")
            string(REGEX MATCH "^[^/]+" top_dir "${rel_path}")
            target_include_directories(${target} BEFORE PRIVATE "${patched_root}/${top_dir}")
        endif()
    endforeach()
    message(STATUS "ags4mac: applied ${patch_file}")
endfunction()

agsn_apply_upstream_patch(common "${CMAKE_CURRENT_SOURCE_DIR}/patches/0009-Common-fix-CopyPixelsRegion-with-a-vertical-offset.patch"
    Common/gfx/bitmapdata.cpp)
# libtools compiles its own copy of this Common source: the Common fix must reach the tools too.
agsn_apply_upstream_patch(libtools "${CMAKE_CURRENT_SOURCE_DIR}/patches/0009-Common-fix-CopyPixelsRegion-with-a-vertical-offset.patch"
    Common/gfx/bitmapdata.cpp)
agsn_apply_upstream_patch(libtools "${CMAKE_CURRENT_SOURCE_DIR}/patches/0005-Tools-keep-mask-color-converting-32-to-16-bit.patch"
    Tools/data/sprite_utils.cpp)
agsn_apply_upstream_patch(engine "${CMAKE_CURRENT_SOURCE_DIR}/patches/0007-Engine-editor-debugging-on-non-Windows-platforms.patch"
    Engine/debug/debug.cpp Engine/debug/debugger.h Engine/debug/filebasedagsdebugger.cpp Engine/debug/filebasedagsdebugger.h
    Engine/main/main.cpp)
# all agsbuild sources: main.cpp includes taskmanager.h next to itself
agsn_apply_upstream_patch(agsbuild "${CMAKE_CURRENT_SOURCE_DIR}/patches/0010-agsbuild-deep-copy-task-strings-thread-race.patch"
    Tools/agsbuild/taskmanager.cpp Tools/agsbuild/taskmanager.h Tools/agsbuild/main.cpp
    Tools/agsbuild/pipedprocess.cpp Tools/agsbuild/pipedprocess.h)

set(AGSN_CONSTANTS_OUT "${CMAKE_BINARY_DIR}/generated/agsn_constants.inc")
include(cmake_generate_constants.cmake)
set_property(DIRECTORY APPEND PROPERTY CMAKE_CONFIGURE_DEPENDS "${AGS_ROOT}/Editor/AGS.Native/NativeMethods.cpp")

add_library(agsnative SHARED
    shim/agsnative.h
    shim/agsn_common.h
    shim/agsn_common.cpp
    shim/agsn_image.cpp
    shim/agsn_spritefile.cpp
    shim/agsn_room.cpp
    shim/agsn_room_background.cpp
    shim/agsn_gui.cpp
    shim/agsn_template.cpp
    "${AGS_ROOT}/Editor/Native/acgui_agsnative.cpp"
    shim/agsn_cc_hooks.cpp
    shim/agsn_constants.cpp
)

target_link_libraries(agsnative PRIVATE AGS::Common)
target_include_directories(agsnative PRIVATE "${CMAKE_BINARY_DIR}/generated")
target_compile_definitions(agsnative PRIVATE AGSN_BUILDING_LIBRARY)

# Export only the C API.
set_target_properties(agsnative PROPERTIES
    C_VISIBILITY_PRESET hidden
    CXX_VISIBILITY_PRESET hidden
    VISIBILITY_INLINES_HIDDEN ON
    OUTPUT_NAME agsnative
)

# The upstream static libraries are built with default visibility; export only the agsn_* C API
# and drop unused code (e.g. most of SDL, which Common links publicly).
target_link_options(agsnative PRIVATE
    "LINKER:-exported_symbol,_agsn_*"
    "LINKER:-dead_strip"
)
