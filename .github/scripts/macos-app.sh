#!/bin/sh
# Builds Context.app from a published Context.Desktop folder. It does not sign and does not zip.
# Usage: sh macos-app.sh <publish-dir> <version> <out-dir>
# Writes <out-dir>/Context.app/Contents/Info.plist and copies every published file to
# <out-dir>/Context.app/Contents/MacOS/ (the .NET app expects its files next to the executable).
# The version must be SemVer (X.Y.Z or X.Y.Z-label); the bundle versions are its X.Y.Z core.
# A wrong or missing argument exits non-zero before anything is created. The version is only ever
# compared and printed as data, never evaluated.
set -eu
# ASCII-only bracket ranges in the SemVer grep, whatever the runner locale.
LC_ALL=C
export LC_ALL

refuse() {
  echo "$1" >&2
  exit 1
}

if [ "$#" -ne 3 ]; then
  refuse "usage: macos-app.sh <publish-dir> <version> <out-dir>"
fi

publish="$1"
version="$2"
out="$3"
nl='
'
cr="$(printf '\r')"

if [ -z "$publish" ] || [ -z "$version" ] || [ -z "$out" ]; then
  refuse "usage: macos-app.sh <publish-dir> <version> <out-dir>"
fi

case "$version" in
  *"$nl"* | *"$cr"*) refuse "version holds a line break" ;;
esac

semver='^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$'
# The version has no line break (checked above), so grep sees exactly one line: a whole-string match.
if ! printf '%s\n' "$version" | grep -Eq "$semver"; then
  refuse "version is not SemVer (X.Y.Z or X.Y.Z-label)"
fi

if [ ! -f "$publish/Context.Desktop" ]; then
  refuse "no Context.Desktop executable in the publish folder"
fi

app="$out/Context.app"
if [ -e "$app" ]; then
  refuse "Context.app is already in the output folder"
fi

# MAJOR.MINOR.PATCH: macOS bundle versions take no prerelease label.
core="${version%%-*}"

mkdir -p "$app/Contents/MacOS"
cp -R "$publish/." "$app/Contents/MacOS/"
chmod +x "$app/Contents/MacOS/Context.Desktop"

cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>
  <string>Context</string>
  <key>CFBundleDisplayName</key>
  <string>Context</string>
  <key>CFBundleExecutable</key>
  <string>Context.Desktop</string>
  <key>CFBundleIdentifier</key>
  <string>com.nothisisnotdaniel.context</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleInfoDictionaryVersion</key>
  <string>6.0</string>
  <key>CFBundleShortVersionString</key>
  <string>$core</string>
  <key>CFBundleVersion</key>
  <string>$core</string>
  <key>LSMinimumSystemVersion</key>
  <string>12.0</string>
  <key>NSHighResolutionCapable</key>
  <true/>
</dict>
</plist>
PLIST
