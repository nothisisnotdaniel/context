#!/bin/sh
# Prints the header of the release notes (Markdown, for gh release create --notes-file) on stdout.
# Usage: sh release-notes.sh <macos>
# <macos> is the macOS signing switch: "on" says the macOS build is signed and notarized; empty or "off" says
# how to open the ad-hoc signed build. Any other value, or a wrong argument count, exits non-zero and prints
# nothing on stdout. The Windows, update and console-tool text is the same in both cases. The argument is only
# ever compared, never evaluated or printed.
set -eu

refuse() {
  echo "$1" >&2
  exit 1
}

if [ "$#" -ne 1 ]; then
  refuse "usage: release-notes.sh <macos: on, off or empty>"
fi

case "$1" in
  on)
    cat <<'NOTES'
## Desktop app

The macOS build is signed and notarized by Apple. The Windows build is not signed yet, so Windows warns before
the first start.

- **macOS:** download `Context-<version>-osx-arm64.zip` (Apple silicon) or `Context-<version>-osx-x64.zip` (Intel),
  unzip it and open `Context.app`. The app is signed and notarized by Apple, so macOS opens it after its usual
  question about an app downloaded from the internet. The macOS copy does not update itself; download the next
  build when you want it.
NOTES
    ;;
  '' | off)
    cat <<'NOTES'
## Desktop app (unsigned test builds)

The desktop builds are not signed yet, so the operating system warns before the first start.

- **macOS:** download `Context-<version>-osx-arm64.zip` (Apple silicon) or `Context-<version>-osx-x64.zip` (Intel),
  unzip it and open `Context.app`. macOS blocks it the first time: open System Settings > Privacy & Security and
  click `Open Anyway`, or run `xattr -dr com.apple.quarantine Context.app` in the Terminal once. The macOS copy does
  not update itself; download the next build when you want it.
NOTES
    ;;
  *)
    refuse "usage: release-notes.sh <macos: on, off or empty>"
    ;;
esac

cat <<'NOTES'
- **Windows:** download and run `ContextDesktop-<channel>-Setup.exe`, where `<channel>` is `develop` for a develop
  prerelease and `win` for a release (`ContextDesktop-develop-Setup.exe` or `ContextDesktop-win-Setup.exe`). On the
  SmartScreen warning click `More info`, then `Run anyway`. The installed app keeps itself up to date: at each start
  it looks for a newer build on its own channel (`develop` builds follow the develop prereleases, a release follows
  the releases), downloads it and puts it in place after you close the app, or at the next start (the app then
  restarts once) when it stayed open for a while. `ContextDesktop-<channel>-Portable.zip` runs without installing
  and does not update itself. The `.nupkg`, `releases.*.json`, `assets.*.json` and `RELEASES*` files are for the
  updater.
- **Console tool:** `Context.<version>.nupkg` is the .NET tool package (command `context`).

NOTES
