# 037 Note store cleanup

As a Context user, I want very long note titles and case-sensitive file systems handled correctly, so that saving and finding notes never fails on edge cases.

## What

Cap temp file names for very long note titles in `NoteStore.Save`, and compare paths case-sensitively in `NoteStore.Contains` on case-sensitive file systems.

## Why

Temp file names for long titles can exceed limits, and `NoteStore.Contains` compares paths without regard to case even where case matters.
