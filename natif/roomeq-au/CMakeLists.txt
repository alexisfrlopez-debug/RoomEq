# RoomEq AU — greffon AudioUnit « RoomEq Correction » (spec I-AUD-07 / N-AU-01).
#
# Deux modes d'après la plateforme :
#   • TOUTES plateformes (dont Linux) : cœur C++ portable + tests natifs
#     (parité exacte avec RoomEq.Core sur les références de tests/data) ;
#   • macOS uniquement : le greffon RoomEq.component (AUv2 'aufx'/'Rmeq'/'Rmeq',
#     AudioUnitSDK d'Apple vendangé dans thirdparty/, Apache-2.0).
#
# Usage :
#   cmake -S natif/roomeq-au -B build && cmake --build build && ./build/roomeq_au_tests
#   cmake -S natif/roomeq-au -B build-au && cmake --build build-au --target RoomEqAU
#   (macOS) → build-au/RoomEqAU.component ; voir build-macos.sh pour la suite
#   (signature ad hoc, installation, auval).

cmake_minimum_required(VERSION 3.22)

project(RoomEqAU VERSION 3.2.1 LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# AudioUnitSDK 1.4.0 exige C++23 (std::expected, concepts) — les cibles
# ausdk/RoomEqAU reçoivent CXX_STANDARD 23 ci-dessous ; le cœur et les tests
# restent C++17 portables (Linux/CI inclus).

if(NOT CMAKE_BUILD_TYPE AND NOT CMAKE_CONFIGURATION_TYPES)
    set(CMAKE_BUILD_TYPE Release)
endif()

# ------------------------------------------------------------------- cœur C++

add_library(roomeq_au_core STATIC
    core/roomeq/dsp/biquad.cpp
    core/roomeq/dsp/eq_chain.cpp
    core/roomeq/json/mini_json.cpp
    core/roomeq/preset/preset_json.cpp
    core/roomeq/preset/preset_ffp.cpp
)
target_include_directories(roomeq_au_core PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/core)
target_compile_options(roomeq_au_core PRIVATE
    $<$<CXX_COMPILER_ID:GNU,Clang,AppleClang>:-Wall -Wextra -Werror>)

# ---------------------------------------------------------------- tests natifs

enable_testing()

add_executable(roomeq_au_tests tests/test_main.cpp)
target_link_libraries(roomeq_au_tests PRIVATE roomeq_au_core)
target_compile_definitions(roomeq_au_tests PRIVATE
    ROOMEQ_AU_SOURCE_DIR="${CMAKE_CURRENT_SOURCE_DIR}")
target_compile_options(roomeq_au_tests PRIVATE
    $<$<CXX_COMPILER_ID:GNU,Clang,AppleClang>:-Wall -Wextra>)

add_test(NAME roomeq_au_core_tests COMMAND roomeq_au_tests)

# ------------------------------------------------------------- greffon (macOS)

if(APPLE)
    enable_language(OBJCXX)

    # greffon universel (Apple Silicon + Intel) : distribuable sur tous les
    # Mac ; sans effet sur les tests natifs (exécutés sur l'arch du runner)
    if(NOT DEFINED CMAKE_OSX_ARCHITECTURES)
        set(CMAKE_OSX_ARCHITECTURES "arm64;x86_64")
    endif()
    # cible de déploiement : macOS 11 (UTType/UniformTypeIdentifiers, 2020) —
    # sans cela, CMake reprend celle du SDK du runner et le bundle serait
    # inutilisable sur les Mac plus anciens
    if(NOT DEFINED CMAKE_OSX_DEPLOYMENT_TARGET)
        set(CMAKE_OSX_DEPLOYMENT_TARGET "11.0")
    endif()

    # SDK AudioUnit d'Apple (Apache-2.0), vendangé — version 1.4.0.
    add_library(ausdk STATIC
        thirdparty/AudioUnitSDK/src/AudioUnitSDK/AUBase.cpp
        thirdparty/AudioUnitSDK/src/AudioUnitSDK/AUBuffer.cpp
        thirdparty/AudioUnitSDK/src/AudioUnitSDK/AUBufferAllocator.cpp
        thirdparty/AudioUnitSDK/src/AudioUnitSDK/AUEffectBase.cpp
        thirdparty/AudioUnitSDK/src/AudioUnitSDK/AUInputElement.cpp
        thirdparty/AudioUnitSDK/src/AudioUnitSDK/AUOutputElement.cpp
        thirdparty/AudioUnitSDK/src/AudioUnitSDK/AUPlugInDispatch.cpp
        thirdparty/AudioUnitSDK/src/AudioUnitSDK/AUScopeElement.cpp
        thirdparty/AudioUnitSDK/src/AudioUnitSDK/ComponentBase.cpp
    )
    target_include_directories(ausdk PUBLIC thirdparty/AudioUnitSDK/include)
    set_target_properties(ausdk PROPERTIES CXX_STANDARD 23 CXX_STANDARD_REQUIRED ON)
    target_compile_options(ausdk PRIVATE
        $<$<CXX_COMPILER_ID:GNU,Clang,AppleClang>:-Wall -Wextra -Wno-unused-parameter>)

    add_library(RoomEqAU MODULE
        au/RoomEqAu.cpp
        au/RoomEqAuView.mm
    )
    set_target_properties(RoomEqAU PROPERTIES
        BUNDLE TRUE
        BUNDLE_EXTENSION "component"
        MACOSX_BUNDLE TRUE
        MACOSX_BUNDLE_INFO_PLIST "${CMAKE_CURRENT_SOURCE_DIR}/au/Info.plist"
        OUTPUT_NAME "RoomEq"
        PREFIX ""
        CXX_STANDARD 23
        XCODE_ATTRIBUTE_PRODUCT_BUNDLE_EXTENSION "component"
        XCODE_ATTRIBUTE_CLANG_ENABLE_OBJC_ARC "YES")
    # la vue Cocoa exige ARC (générateurs Makefile/Ninja : drapeau de compilation)
    set_source_files_properties(au/RoomEqAuView.mm PROPERTIES
        COMPILE_OPTIONS "-fobjc-arc")
    target_link_libraries(RoomEqAU PRIVATE
        roomeq_au_core
        ausdk
        "-framework AppKit"
        "-framework AudioUnit"
        "-framework AudioToolbox"
        "-framework CoreFoundation"
        "-framework CoreAudio"
        "-framework UniformTypeIdentifiers")
    target_compile_options(RoomEqAU PRIVATE
        $<$<CXX_COMPILER_ID:GNU,Clang,AppleClang>:-Wall -Wextra -Wno-unused-parameter>)
    target_compile_definitions(RoomEqAU PRIVATE
        ROOMEQ_AU_VERSION="${PROJECT_VERSION}"
        ROOMEQ_AU_SOURCE_DIR="${CMAKE_CURRENT_SOURCE_DIR}")
else()
    message(STATUS "RoomEq AU : hors macOS — cœur + tests natifs uniquement "
                   "(le greffon .component se compile sur macOS, voir build-macos.sh)")
endif()
