# 282 Bigger level 1 and 2 headings in the note editor

As a desktop user writing notes, I want level 1 and level 2 headings to be bigger than level 3, so that the heading levels read in the right order.

## What

In the note editor, # (h1) and ## (h2) headings show smaller than ### (h3): the global h1 (20px) and h2 (13px, grey, uppercase) rules in src/Context.Desktop/wwwroot/app.css reach into .note-editor-body, while h3 and below keep browser defaults. Make h1 and h2 inside .note-editor-body clearly bigger than h3, largest first (h1 > h2 > h3), in normal case. Leave h3, h4, h5 and h6 exactly as they are now, and leave the app's own panel headings (outside the editor) unchanged. Light and dark mode.

## Why

The user reported on 2026-11-18 that level 1 and 2 headings are smaller than level 3.

## Notes

Requested 2026-11-18. Styles live in src/Context.Desktop/editor/src/editor.css (built to wwwroot/editor/editor.css) and wwwroot/app.css.
