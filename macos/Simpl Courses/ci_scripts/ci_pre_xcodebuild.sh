#!/bin/sh
# Xcode Cloud runs this before xcodebuild: stamp the build number so TestFlight accepts every upload.
set -e
cd "$CI_PRIMARY_REPOSITORY_PATH/macos/Simpl Courses"
if [ -n "${CI_BUILD_NUMBER:-}" ]; then xcrun agvtool new-version -all "$CI_BUILD_NUMBER"; fi
