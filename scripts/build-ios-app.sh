#!/usr/bin/env bash
# Generates the iOS Xcode project from ios/project.yml with XcodeGen and (optionally) opens it.
#
#   ./scripts/build-ios-app.sh          # generate ios/SimplCourses.xcodeproj
#   ./scripts/build-ios-app.sh --open   # …and open it in Xcode
#
# Needs Xcode and Homebrew (XcodeGen is installed with brew if missing). Signing: in Xcode select the
# SimplCourses target → Signing & Capabilities → Team (a personal Apple ID team runs on your own iPhone).
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "This script must run on macOS (it needs Xcode)." >&2
  exit 1
fi
if ! command -v xcodegen >/dev/null 2>&1; then
  if ! command -v brew >/dev/null 2>&1; then
    echo "XcodeGen is not installed and Homebrew is not available. Install Homebrew (https://brew.sh) or XcodeGen (https://github.com/yonaskolb/XcodeGen), then run this again." >&2
    exit 1
  fi
  echo "▶ Installing XcodeGen with Homebrew …"
  brew install xcodegen
fi
echo "▶ Generating ios/SimplCourses.xcodeproj …"
(cd "$ROOT/ios" && xcodegen generate --quiet)
echo "✅ $ROOT/ios/SimplCourses.xcodeproj"
if [[ "${1:-}" == "--open" ]]; then
  open "$ROOT/ios/SimplCourses.xcodeproj"
fi
