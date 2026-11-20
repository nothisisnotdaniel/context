# 310 Choose light, dark or system theme in Settings

As a desktop user, I want to choose light mode, dark mode or the system default in Settings, so that the app looks the way I like regardless of my OS setting.

Category: feature

## What

The app now follows the OS only: wwwroot/app.css switches colours with @media (prefers-color-scheme). Add a theme setting with three choices, System (default), Light and Dark, to SettingsPanel.razor, saved with the other desktop settings and applied at startup and immediately when changed (for example a data-theme attribute on the root element that the CSS honours, with System keeping the media query). The editor and every popover must follow the chosen theme. Keyboard and screen reader friendly.

## Why

User request on 2026-11-18: add a way to switch between light and dark mode, with the option to just use the system default.

## Notes

Requested by the user on 2026-11-18.
