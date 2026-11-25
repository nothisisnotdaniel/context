# 519 Preserve consecutive spaces in recovery warnings

As someone locating a readable version after marking a note sensitive, I want the warning to show its exact location so I can secure the correct file.

Category: bug

## What

- In a disposable data folder with two consecutive ASCII spaces in its name, use the existing Journal editor confirmation for **Mark sensitive** with an earlier plaintext version whose cleanup is refused. The warning shows the folder and filename with the two spaces collapsed to one. For example, `Journal  store` appears as `Journal store`.
- Preserve consecutive spaces in literal recovery paths in the red warning while allowing long lines to wrap. A browser or desktop WebView regression should compare the displayed folder and filename with their actual on-disk spelling. The current assertion reads the text before layout.

## Why

Following the displayed path can lead to a different directory while an older plaintext copy still needs to be secured.

## Notes

Known issue accepted for [Context #476](476-journal-view-of-the-days-with.md) on 25 November 2026. The operator considers this combination of a cleanup refusal and a path containing adjacent spaces unlikely. For the exact location, expand **Recovery paths** and use its selectable read-only field. Round 20 verified the plaintext and encrypted copies on commit `dfc9b98a7b11`.
