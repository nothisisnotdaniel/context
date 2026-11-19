#!/bin/sh
# Makes Context.icns, Context.ico and Context.png from context-icon.svg (macOS only: sips, iconutil, python3).
# Usage: sh make-icons.sh   (run from any folder; writes next to this script). Commit the three files it writes.
# dotnet build and dotnet test never run it; they use the committed files.
set -eu
here="$(cd "$(dirname "$0")" && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

sips -s format png "$here/context-icon.svg" --out "$work/1024.png" >/dev/null
for size in 16 24 32 48 64 128 256 512; do
  sips -z "$size" "$size" "$work/1024.png" --out "$work/$size.png" >/dev/null
done

# macOS bundle icon: the standard iconset sizes, packed by iconutil.
set_dir="$work/Context.iconset"
mkdir "$set_dir"
for size in 16 32 128 256 512; do
  cp "$work/$size.png" "$set_dir/icon_${size}x${size}.png"
  cp "$work/$((size * 2)).png" "$set_dir/icon_${size}x${size}@2x.png"
done
iconutil -c icns "$set_dir" -o "$here/Context.icns"

# Window icon off Windows.
cp "$work/512.png" "$here/Context.png"

# Windows icon: PNG-compressed entries (Windows Vista and later), 256 stored as width byte 0.
python3 -I - "$here/Context.ico" "$work" 16 24 32 48 64 256 <<'PY'
import struct, sys
out, work, sizes = sys.argv[1], sys.argv[2], [int(s) for s in sys.argv[3:]]
images = [open(f"{work}/{s}.png", "rb").read() for s in sizes]
header = struct.pack("<HHH", 0, 1, len(sizes))
offset = 6 + 16 * len(sizes)
entries = b""
for size, data in zip(sizes, images):
    byte = 0 if size >= 256 else size
    entries += struct.pack("<BBBBHHII", byte, byte, 0, 0, 1, 32, len(data), offset)
    offset += len(data)
with open(out, "wb") as f:
    f.write(header + entries + b"".join(images))
PY
