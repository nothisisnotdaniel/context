#!/bin/sh
# Signs Context.app with a Developer ID identity for the hardened runtime. It does not notarize and does not zip.
# Usage: sh macos-sign.sh <Context.app> <identity> <entitlements>
# Signs every regular file under Contents/MacOS except the main executable, then the main executable, then the
# bundle, each with: codesign --force --timestamp --options runtime --entitlements <entitlements> -s <identity>
# (inside out, never --deep for signing), then checks the result with codesign --verify --deep --strict.
# The identity "-" (ad-hoc) and anything else that starts with "-" is refused. A wrong or missing argument, a
# missing bundle or a missing entitlements file exits 1 before codesign is called. The arguments are only ever
# passed on as data, never evaluated.
# Exit codes: 1 refused, 2 signing failed, 3 the verification failed.
set -eu

refuse() {
  echo "$1" >&2
  exit 1
}

if [ "$#" -ne 3 ]; then
  refuse "usage: macos-sign.sh <Context.app> <identity> <entitlements>"
fi

app="$1"
identity="$2"
entitlements="$3"

if [ -z "$app" ] || [ -z "$identity" ] || [ -z "$entitlements" ]; then
  refuse "usage: macos-sign.sh <Context.app> <identity> <entitlements>"
fi

case "$identity" in
  -*) refuse "the identity must be a Developer ID identity, not '-' (ad-hoc) or an option" ;;
esac

macos="$app/Contents/MacOS"
main="$macos/Context.Desktop"
if [ ! -f "$main" ]; then
  refuse "no Contents/MacOS/Context.Desktop in the bundle"
fi

if [ ! -f "$entitlements" ]; then
  refuse "no entitlements file"
fi

# Inside out: the libraries and other files first. find exits non-zero when a codesign call failed.
if ! find "$macos" -type f ! -samefile "$main" -exec \
  codesign --force --timestamp --options runtime --entitlements "$entitlements" -s "$identity" {} +; then
  echo "macos-sign.sh: signing a file in Contents/MacOS failed" >&2
  exit 2
fi

if ! codesign --force --timestamp --options runtime --entitlements "$entitlements" -s "$identity" "$main"; then
  echo "macos-sign.sh: signing the main executable failed" >&2
  exit 2
fi

if ! codesign --force --timestamp --options runtime --entitlements "$entitlements" -s "$identity" "$app"; then
  echo "macos-sign.sh: signing the bundle failed" >&2
  exit 2
fi

if ! codesign --verify --deep --strict "$app"; then
  echo "macos-sign.sh: the signature does not verify" >&2
  exit 3
fi
