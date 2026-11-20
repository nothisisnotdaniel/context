# 283 Spacing for the no-note placeholder in the note area

As a desktop user, I want the placeholder shown when no note is open to sit with proper spacing below the top bar, so that the empty note area looks deliberate.

Category: ui

## What

The p.no-note placeholder in src/Context.Desktop/Main.razor has no CSS rule of its own in wwwroot/app.css; it relies on the default p and .grey styles and may sit tight to the top bar inside .note-area. Check it in the running app and give it its own spacing if needed, in light and dark mode.

## Why

The UI review of job 216 noted the spacing may be too tight; without a check the empty state can look broken.

## Notes

Proposed by job 216-today-home-open-note (review-ui nit 1).

Proposed by job 216-today-home-open-note (`factory/jobs/216-today-home-open-note/`).
