# 469 Check chip keyboard focus in the desktop web view on Windows

As a keyboard user on Windows, I want to reach and open tag, link and mention chips with the keyboard, so that I don't need the mouse.

Category: ux

## What

Run the checklist below on Windows in the desktop app and tick each step.

## Checklist

- [x] Open a note with a tag chip, a link chip and a mention chip.
- [x] Press Tab: each chip gets focus, in order.
- [x] The focus ring is visible in light and in dark mode.
- [x] Enter on a focused chip opens it; a click still opens it.
- [x] With a chip focused, Ctrl+S saves and Ctrl+K opens the palette.
- [x] If any step fails, add a fix item that names the step and what you saw.

## Why

Focus behaviour depends on the OS web view, which bUnit and Jint can't show.

## Notes

- From job 142.
- Split from #155 (one item per OS).

## Windows verification

Verified on 2026-11-24 in the Windows desktop app at `29b1b32c` with WebView2 158.0.4370.33. In both themes, Tab moved from the editor through the tag, note link and mention in order. Their 2 px focus rings measured 4.59:1 in light and 7.97:1 in dark, including a wrapped link. Enter and a pointer click opened the tag results, target note and person page. With each chip focused, Ctrl+S persisted a new marker in the private note file and Ctrl+K focused the command palette.

All five checks passed. The conditional fix item was unnecessary. The macOS check is tracked separately in #468.
