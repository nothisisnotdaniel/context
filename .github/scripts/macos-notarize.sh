#!/bin/sh
# Notarizes a signed Context.app with Apple, staples the ticket and writes the zip that ships.
# Usage: sh macos-notarize.sh <Context.app> <zip> <key.p8> <key-id> <issuer-id>
# Steps: ditto -c -k --keepParent into a temporary zip, xcrun notarytool submit ... --wait (the reported status
# must be Accepted; an exit code 0 with any other status fails), xcrun stapler staple, xcrun stapler validate,
# spctl --assess --type execute, then the final zip is written again from the stapled app.
# <zip> is only created at the very end (written next to it, then moved), so a failed step leaves no file there.
# A wrong or missing argument, a missing bundle or key file, or a <zip> that already exists exits 1 before
# anything is created. The arguments are only ever passed on as data, never evaluated; the key is never printed.
# Exit codes: 1 refused, 2 notarization failed, 3 stapling failed, 4 the Gatekeeper assessment failed,
# 5 the final zip could not be written.
set -eu

refuse() {
  echo "$1" >&2
  exit 1
}

if [ "$#" -ne 5 ]; then
  refuse "usage: macos-notarize.sh <Context.app> <zip> <key.p8> <key-id> <issuer-id>"
fi

app="$1"
zip="$2"
key="$3"
key_id="$4"
issuer="$5"

if [ -z "$app" ] || [ -z "$zip" ] || [ -z "$key" ] || [ -z "$key_id" ] || [ -z "$issuer" ]; then
  refuse "usage: macos-notarize.sh <Context.app> <zip> <key.p8> <key-id> <issuer-id>"
fi

if [ ! -d "$app" ]; then
  refuse "no Context.app to notarize"
fi

if [ ! -f "$key" ]; then
  refuse "no notary key file"
fi

if [ -e "$zip" ]; then
  refuse "the zip is already there"
fi

# Next to the final zip, so the last step is a rename on one file system.
work="$(mktemp -d "$(dirname "$zip")/notarize.XXXXXX")"
trap 'rm -rf "$work"' EXIT

if ! ditto -c -k --keepParent "$app" "$work/submit.zip"; then
  echo "macos-notarize.sh: zipping the app for the submission failed" >&2
  exit 2
fi

if ! result="$(xcrun notarytool submit "$work/submit.zip" --key "$key" --key-id "$key_id" --issuer "$issuer" --wait)"; then
  printf '%s\n' "$result"
  echo "macos-notarize.sh: the submission failed" >&2
  exit 2
fi
printf '%s\n' "$result"

# notarytool ends with a line "  status: <status>"; the last one counts ("Current status: ..." lines do not match).
status="$(printf '%s\n' "$result" | sed -n 's/^[[:space:]]*status:[[:space:]]*//p' | sed -n '$p')"
if [ "$status" != "Accepted" ]; then
  echo "macos-notarize.sh: the notarization status is not Accepted" >&2
  exit 2
fi

if ! xcrun stapler staple "$app"; then
  echo "macos-notarize.sh: stapling the ticket failed" >&2
  exit 3
fi

if ! xcrun stapler validate "$app"; then
  echo "macos-notarize.sh: the stapled ticket is not valid" >&2
  exit 3
fi

if ! spctl --assess --type execute "$app"; then
  echo "macos-notarize.sh: Gatekeeper does not accept the app" >&2
  exit 4
fi

# ditto keeps the modes and the signatures' extended attributes; the zip holds Context.app itself, now stapled.
if ! ditto -c -k --keepParent "$app" "$work/final.zip" || ! mv "$work/final.zip" "$zip"; then
  rm -f "$zip"
  echo "macos-notarize.sh: writing the final zip failed" >&2
  exit 5
fi
