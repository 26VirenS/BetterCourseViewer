# Liquid Glass for Windows — Simpl's native showcase: Win32, Direct3D 11 with AuroraGlass's glass (its core, compiled in
# from a pinned copy: -DAURORA_DIR=<its source>), Direct2D and DirectWrite.
cmake_minimum_required(VERSION 3.20)
project(SimplLiquidGlass LANGUAGES CXX)
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
if(NOT AURORA_DIR)
  message(FATAL_ERROR "Set AURORA_DIR to AuroraGlass's source (https://github.com/VeiQiuLab/AuroraGlass).")
endif()
if(MSVC)
  add_compile_options(/W3 /utf-8 /EHsc /permissive-)
  set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>") # (no C++ runtime to install)
endif()

add_library(AuroraGlassCore STATIC
  ${AURORA_DIR}/src/core/glass_material.cpp
  ${AURORA_DIR}/src/core/shader_library.cpp
  ${AURORA_DIR}/src/core/glass_surface.cpp)
target_include_directories(AuroraGlassCore PUBLIC ${AURORA_DIR}/src)

add_executable(LiquidGlass WIN32 src/main.cpp src/app.rc)
target_link_libraries(LiquidGlass PRIVATE AuroraGlassCore d3d11 dxgi d3dcompiler d2d1 dwrite windowscodecs dwmapi ole32 shell32 user32)
target_compile_definitions(LiquidGlass PRIVATE UNICODE _UNICODE NOMINMAX WIN32_LEAN_AND_MEAN)
target_compile_definitions(AuroraGlassCore PRIVATE UNICODE _UNICODE NOMINMAX)
# AuroraGlass reads its shaders at run time: a "shaders" folder beside the app
add_custom_command(TARGET LiquidGlass POST_BUILD
  COMMAND ${CMAKE_COMMAND} -E copy_directory ${AURORA_DIR}/shaders $<TARGET_FILE_DIR:LiquidGlass>/shaders)
