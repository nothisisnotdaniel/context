# 434 Share one class for the Suite Pro entry-row buttons

As a maintainer, I want the Suite Pro entry-row buttons to share one CSS class with the other entry rows, so that their look stays in step.

Category: ui

## What

The Suite Pro section in SettingsPanel styles button.pro-activate and button.pro-remove on their own. Move them onto the shared entry-row button class the other Settings rows use and drop the duplicate rules from app.css.

## Why

Raised by the ui review of job 367: duplicated button rules drift apart when the shared style changes, so the Pro row would start to look different from the rest of Settings.

## Notes

Proposed by job 367-pro-licence-context.

Proposed by job 367-pro-licence-context (`factory/jobs/367-pro-licence-context/`).
