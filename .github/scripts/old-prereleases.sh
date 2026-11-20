#!/bin/sh
# Prints the develop prereleases that a release makes old, so the workflow can delete them.
# Usage: gh release list ... | sh old-prereleases.sh <X.Y.Z>
# Tag names come one per line on stdin. Printed, in input order, are only the lines that are exactly
# v<MAJOR.MINOR.PATCH>-develop.<n> (<n> a positive whole number without leading zero) whose core is
# lower than or equal to X.Y.Z (1.2.3-develop.5 sorts below 1.2.3). Every other line is ignored.
# The argument must be a plain X.Y.Z. Tag names are only compared, never run as code.
set -eu
# ASCII-only bracket ranges in the greps and byte order in sort, whatever the runner locale.
LC_ALL=C
export LC_ALL

release="${1-}"
nl='
'

refuse() {
  echo "$1" >&2
  exit 1
}

case "$release" in
  *"$nl"*) refuse "release version holds a line break; it must be a plain X.Y.Z" ;;
esac
if ! printf '%s\n' "$release" | grep -Eq '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$'; then
  refuse "release version '$release' is not a plain X.Y.Z"
fi

# Splits X.Y.Z (already checked) into its three numbers.
rel_major="${release%%.*}"
rest="${release#*.}"
rel_minor="${rest%%.*}"
rel_patch="${rest#*.}"

# True when whole number $1 is greater than $2. Both have no leading zeros, so a longer one is
# greater, and same-length ones compare as text; this never overflows the shell's integers.
greater() {
  if [ "${#1}" -ne "${#2}" ]; then
    [ "${#1}" -gt "${#2}" ]
    return
  fi
  [ "$1" != "$2" ] && [ "$(printf '%s\n%s\n' "$1" "$2" | sort | tail -n 1)" = "$1" ]
}

develop='^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)-develop\.[1-9][0-9]*$'

while IFS= read -r tag || [ -n "$tag" ]; do
  # read stops at the line break, so grep sees exactly one line: a whole-string match.
  if ! printf '%s\n' "$tag" | grep -Eq "$develop"; then
    continue
  fi

  core="${tag#v}"
  core="${core%%-*}"
  ma="${core%%.*}"
  rest="${core#*.}"
  mi="${rest%%.*}"
  pa="${rest#*.}"

  # Newer than the release: keep it.
  if greater "$ma" "$rel_major"; then
    continue
  fi
  if [ "$ma" = "$rel_major" ]; then
    if greater "$mi" "$rel_minor"; then
      continue
    fi
    if [ "$mi" = "$rel_minor" ] && greater "$pa" "$rel_patch"; then
      continue
    fi
  fi

  printf '%s\n' "$tag"
done
