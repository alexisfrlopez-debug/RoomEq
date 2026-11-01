# RoomEq VST — greffon VST 2 32 bits « RoomEq Correction » (spec I-AUD-08 /
# N-VST-01, décision D21).
#
# Deux étages :
#   • TOUTES plateformes (dont Linux/CI) : cœur C++ portable + greffon VST
#     sans éditeur (RoomEqVst.so chargé par le mini-hôte host/mini_host.cpp)
#     + tests natifs (parité avec RoomEq.Core sur les références du module
#     AU, chunks, paramètres, bypass) ;
#   • Build de livraison Windows 32 bits : build-vst.sh cross-compile les
#     MÊMES sources + EditorWin32.cpp avec Zig (MinGW-w64 embarqué, SSE2
#     forcé) → RoomEq.dll PE32 i386.
#
# Le cœur est celui du greffon AU (natif/roomeq-au/core) : AUCUNE copie de
# fichier, les sources sont compilées ici directement (le module AU reste
# scellé et propriétaire de ses sources — voir REFERENCE-NATIF.md).
#
# Usage :
#   cmake -S natif/roomeq-vst -B build && cmake --build build
#   ./build/roomeq_vst_tests
#   ./build/roomeq_vst_host ./build/RoomEqVst.so natif/roomeq-au/tests/data
#   natif/roomeq-vst/build-vst.sh   # → dist/RoomEq.dll (Windows 32 bits)

cmake_minimum_required(VERSION 3.22)

project(RoomEqVST VERSION 3.4.2 LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

if(NOT CMAKE_BUILD_TYPE AND NOT CMAKE_CONFIGURATION_TYPES)
    set(CMAKE_BUILD_TYPE Release)
endif()

# ------------------------------------------------------------------- cœur C++
# (réutilisé du module AU — spec I-AUD-07, parité bit à bit vérifiée là-bas
#  et re-vérifiée ici à travers le greffon VST)

set(ROOMEQ_AU_CORE ${CMAKE_CURRENT_SOURCE_DIR}/../roomeq-au/core)

add_library(roomeq_vst_core STATIC
    ${ROOMEQ_AU_CORE}/roomeq/dsp/biquad.cpp
    ${ROOMEQ_AU_CORE}/roomeq/dsp/eq_chain.cpp
    ${ROOMEQ_AU_CORE}/roomeq/json/mini_json.cpp
    ${ROOMEQ_AU_CORE}/roomeq/preset/preset_json.cpp
    ${ROOMEQ_AU_CORE}/roomeq/preset/preset_ffp.cpp
)
target_include_directories(roomeq_vst_core PUBLIC ${ROOMEQ_AU_CORE})
# PIC : la bibliothèque statique alimente le MODULE chargeable (RoomEqVst.so)
set_property(TARGET roomeq_vst_core PROPERTY POSITION_INDEPENDENT_CODE ON)
# Pas de -Werror sur le cœur : sources du module AU (scellé, compilées telles
# quelles) — gcc 14 émet un faux positif maybe-uninitialized sur le switch de
# biquad.cpp (toutes les valeurs de l'énumération sont couvertes) ; la
# correction appartient au module propriétaire, la parité est garantie par
# les tests.
target_compile_options(roomeq_vst_core PRIVATE
    $<$<CXX_COMPILER_ID:GNU,Clang,AppleClang>:-Wall -Wextra>)

# ------------------------------------------------------------------ greffon

set(ROOMEQ_VST_SOURCES vst/RoomEqVst.cpp)
if(WIN32)
    list(APPEND ROOMEQ_VST_SOURCES vst/EditorWin32.cpp)
endif()

# Greffon chargeable (MODULE) : sous Linux, chargé par le mini-hôte de test ;
# sous Windows, produit par build-vst.sh (CMake ne construit pas la DLL).
add_library(RoomEqVst MODULE ${ROOMEQ_VST_SOURCES})
target_link_libraries(RoomEqVst PRIVATE roomeq_vst_core)
set_target_properties(RoomEqVst PROPERTIES
    PREFIX ""
    SUFFIX ".so"
    OUTPUT_NAME "RoomEqVst")
target_compile_options(RoomEqVst PRIVATE
    $<$<CXX_COMPILER_ID:GNU,Clang,AppleClang>:-Wall -Wextra -Werror>)

# ---------------------------------------------------------------- tests

enable_testing()

add_executable(roomeq_vst_tests tests/test_main.cpp ${ROOMEQ_VST_SOURCES})
target_link_libraries(roomeq_vst_tests PRIVATE roomeq_vst_core)
target_include_directories(roomeq_vst_tests PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})
target_compile_definitions(roomeq_vst_tests PRIVATE
    ROOMEQ_VST_SOURCE_DIR="${CMAKE_CURRENT_SOURCE_DIR}"
    ROOMEQ_VST_NO_LEGACY_MAIN)
target_compile_options(roomeq_vst_tests PRIVATE
    $<$<CXX_COMPILER_ID:GNU,Clang,AppleClang>:-Wall -Wextra>)

add_test(NAME roomeq_vst_core_tests
    COMMAND roomeq_vst_tests ${CMAKE_CURRENT_SOURCE_DIR}/../roomeq-au/tests/data)

# Mini-hôte : ABI VST 2 complète par dlopen (miroir d'un DAW).
add_executable(roomeq_vst_host host/mini_host.cpp)
target_include_directories(roomeq_vst_host PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/tests)
target_link_libraries(roomeq_vst_host PRIVATE ${CMAKE_DL_LIBS})
target_compile_options(roomeq_vst_host PRIVATE
    $<$<CXX_COMPILER_ID:GNU,Clang,AppleClang>:-Wall -Wextra>)

add_test(NAME roomeq_vst_host_abi
    COMMAND roomeq_vst_host $<TARGET_FILE:RoomEqVst>
            ${CMAKE_CURRENT_SOURCE_DIR}/../roomeq-au/tests/data)
