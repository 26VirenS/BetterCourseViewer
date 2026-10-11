#!/bin/bash
# The Microsoft C++ runtime DLLs an exe needs, put beside it (app-local), so it runs on a PC without the Visual C++
# Redistributable: usage runtime-dlls.sh <folder> <exe>
set -euo pipefail
out="$1"; exe="$2"
VSWHERE="/c/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe"
VS="$("$VSWHERE" -latest -property installationPath | tr -d '\r')"
DUMPBIN="$(find "$(cygpath -u "$VS")/VC/Tools/MSVC" -path '*Hostx64/x64/dumpbin.exe' | head -1)"
deps="$("$DUMPBIN" //nologo //dependents "$(cygpath -w "$exe")" | tr -d '\r' | grep -iE '^\s+(msvcp|vcruntime|concrt|vccorlib)[0-9_a-z]*\.dll' | tr -d ' ' || true)"
echo "C++ runtime DLLs needed: ${deps:-none}"
for d in $deps; do
  src="$(find "$(cygpath -u "$VS")/VC/Redist/MSVC" -ipath '*x64*' -iname "$d" 2>/dev/null | grep -iv onecore | head -1)"
  [ -z "$src" ] && src="/c/Windows/System32/$d"
  cp "$src" "$out/" && echo "  $d <- $src"
done
