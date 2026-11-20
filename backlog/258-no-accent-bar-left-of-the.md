# 258 No accent bar left of the editor while editing

As a Context user, I want no coloured bar next to the note while I edit it, so that an open note looks the same whether I am reading or editing.

## What

In the desktop app (src/Context.Desktop/wwwroot/app.css): remove the accent bar that appears to the left of the note text while the editor has focus (`.editor-host:focus-within { box-shadow: -3px 0 0 0 var(--accent-strong); }`). Nothing replaces it: the text does not shift, no border, outline, shadow or background appears on focus, and only the caret shows that the note can be edited. Keyboard focus rings on other controls (buttons, chips, inputs) stay as they are. Update any test that checks the bar. Light and dark mode.

## Why

The bar was added in job 196 as a quiet edit marker, but it still reads as a box edge and the user wants the note to look the same in edit and read view.

## Notes

Requested by the user on 2026-11-17 ("remove the bar in front of the editor when editing"; confirmed: the vertical line at the left). Rule at app.css:508.

The user asked again on 2026-11-18: "The vertical line in front of the editor is still present. Remove it." Check that no left border, bar or shadow is left next to the editor, focused or not.
