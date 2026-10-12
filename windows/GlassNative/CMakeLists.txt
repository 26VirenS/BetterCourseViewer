# Liquid Glass for Windows — Simpl's native showcase: Win32, Direct3D 11 with Simpl's own glass shader (src/Glass.h,
# modelled on macOS 26's; compiled when the app starts by d3dcompiler_47, part of Windows), Direct2D and DirectWrite.
cmake_minimum_required(VERSION 3.20)
project(SimplLiquidGlass LANGUAGES CXX)
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
if(MSVC)
  add_compile_options(/W3 /utf-8 /EHsc /permissive-)
  set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>") # (no C++ runtime to install)
endif()

add_executable(LiquidGlass WIN32 src/main.cpp src/app.rc)
target_link_libraries(LiquidGlass PRIVATE d3d11 dxgi d3dcompiler d2d1 dwrite windowscodecs dwmapi ole32 shell32 user32)
target_compile_definitions(LiquidGlass PRIVATE UNICODE _UNICODE NOMINMAX WIN32_LEAN_AND_MEAN)
