# 507 Give sidebar outline links sufficient contrast on the panel

As a Context user reading the outline of a note, I want the heading links in the right sidebar to remain legible in both themes.

Category: bug

## What

- Use a link color for .todos-region .outline-item that provides at least 4.5:1 against the actual --panel in both themes. Use a scoped outline-link color with #1a6db7 in Light and the current #63b8ff in Dark. These colors calculate to 4.9261:1 and 7.2533:1 on the measured panels respectively.
- Keep heading levels, their indentation, keyboard behavior, click-to-heading and the current hover underline. Add a targeted contrast check against the panel token and repeat manual #273 in the desktop app on a note with three or more headings.

## Why

On Windows WebView2 158.0.4370.33 at develop 29b1b32, three displayed 14 px outline links rendered as #1e78c8 on #f5f5f7 in Light, a 4.2149:1 contrast ratio. The same links rendered as #63b8ff on #242426 in Dark, reaching 7.2533:1. All six foreground/background pairs were confirmed in actual screenshots.

## Notes

From delegated Windows check #273. #274 concerns a visible outline heading. The independent placeholder, pin and glossary contrast items concern other controls.
