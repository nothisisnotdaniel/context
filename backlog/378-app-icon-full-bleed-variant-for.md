# 378 App icon: full-bleed variant for Windows and Linux

As a Windows or Linux user, I want the Context icon to fill its tile like the icons next to it, so that it doesn't look smaller in the taskbar and app switcher.

Category: ui

## What

src/Context.Desktop/Assets/context-icon.svg insets the rounded square by 64 px of 1024 (the macOS margin). The .ico and Linux .png are made from the same artwork, so they get a transparent border. Add a full-bleed variant (or a smaller inset) for the .ico and .png, keep the macOS .icns as it is, and update the generation script and the icon tests.

## Why

UI review of job 284 (nit 1): the icon looks slightly smaller than its neighbours on Windows and Linux.

## Notes

Proposed by job 284-app-icon (review-ui 1).

Proposed by job 284-app-icon (`factory/jobs/284-app-icon/`).
