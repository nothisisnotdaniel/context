# 153 Check the unsaved-changes dot in both themes

As a Context user, I want the unsaved-changes dot to be easy to see in light and dark themes, so that I notice unsaved edits.

Category: ui

## What

Open a note in the desktop app, type, and check that the dot appears next to the note name and clears on save, in both light and dark themes; report anything wrong as a backlog item.

## Why

Job 141: the page-side callback has no DOM test and reviewers noted the dot is small and title-coloured.

## Notes

Raised by job 141-editor-dirty-marker.

## Manual verification

On 2026-11-24 the Windows desktop at `29b1b32` showed an 8 × 8 pixel dot immediately after the title of an edited note. Its measured contrast was 4.59:1 in Light and 7.97:1 in Dark. In both themes Ctrl+S and the idle save cleared it, and reopening each note showed the saved edits. The delegated check used native WebView2 with private test notes.
