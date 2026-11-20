# 246 Capture bar at the very top, Enter instead of buttons next to inputs

As a Context user, I want the capture bar flush with the top of the window and to press Return instead of clicking a button next to a text box, so that the window is cleaner and quicker to use from the keyboard.

## What

In the desktop app (src/Context.Desktop): (1) The quick capture bar (CaptureBox.razor, .capture-box) has no space above it: it starts directly at the top edge of the window, with no padding or margin from the main region around it. (2) Every button that only submits the text box next to it goes away, and pressing Return while focus is in that box does what the button did. This covers at least Capture (capture bar), New note (new note title), Add (new todo; Return in the todo text, date or time field adds it) and Save link (Settings, calendar link); the spec lists every other such button it finds. Each box gets a placeholder or label that says what Return does (for example "New note title – Return to create"), errors and busy states show as today, and an empty box does nothing on Return. Buttons that are not a submit for one box (for example Today and Tomorrow next to the todo date, Import, Set up sensitive notes) stay. Light and dark mode.

## Why

The gap above the capture bar and the many small buttons next to text boxes make the window look busy, and Return is the natural way to submit a single box.

## Notes

Requested by the user on 2026-11-16 after job 193. Some boxes may already submit on Return; keep that working.
