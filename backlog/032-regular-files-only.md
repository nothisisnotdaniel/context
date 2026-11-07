# 032 Regular files only

As a Context user, I want `/current` to read only regular files in `calendar/`, so that a stray pipe or special file can't hang it.

## What

A pipe (FIFO) or other special file still hangs `/current` (a `*.ics` in `calendar/`), because it opens files with `FileMode.Open`. Read them through `BoundedFile` (as note editing and `/import` already do) or skip anything that isn't a regular file.

## Why

A FIFO named `*.ics` blocks `/current` forever, because it opens files with `FileMode.Open`. Note editing and import already avoid this through `BoundedFile`.
