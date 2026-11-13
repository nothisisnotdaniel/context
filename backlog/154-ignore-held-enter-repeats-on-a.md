# 154 Ignore held Enter repeats on a focused chip

As a keyboard user of the desktop editor, I want holding Enter on a focused chip to open it once, so that I do not trigger the tag or link action many times.

## What

In the editor chip keydown handler (src/Context.Desktop/editor/src/view.js), ignore keydown events with event.repeat set, so OnTag / OnLink is called once per press. Add a test if the editor tests can cover the handler.

## Why

Raised by the ux review of job 142: a held Enter fires keydown repeatedly, so the chip callback runs several times and may open the same tag or link view again and again.

## Notes

Proposed by job 142-chip-keyboard-focus (`factory/jobs/142-chip-keyboard-focus/`).
