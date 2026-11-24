# 512 Focus the editor when the quick switcher reselects the current note

As a note taker, I want the first characters I type after picking a note in the quick switcher to reach that note, including when it is already open.

Category: bug

## What

- After a successful quick-switcher pick, direct focus to the selected editable note. Avoid the unconditional request to focus .layout when a note opens. Handle the already-open path, where the editor instance is retained and its mount-time focus does not run again.
- Preserve the existing contents, selection and normal save behavior when revisiting the current note. Keep the existing successful focus behavior for another note, the dirty-note guard, and the expected layout focus when the switcher is cancelled.
- Update QuickSwitcherTests.Enter_OpensTheNote_RecordsItRecent so its focus expectation matches the successful pick. Cover keyboard and pointer reselection of the current note. Verify in a real desktop window that immediately typed text is saved completely in that note without an extra editor click.

## Why

On Windows WebView2 at develop 29b1b32, all eight switches to a different note accepted and saved all nine typed characters. All four reselections of the current note, using Enter or a pointer pick, sent every printable key to .layout and left all four fixture files unchanged. The same result persisted after waiting more than 300 ms. A click in the editor restored typing.

## Notes

Manual regression #433 remains open until this fix passes. #431 is about scrolling the selected row; #432 is about styling. Main.SwitchTo currently calls CloseSwitcher, which requests layout focus, and RequestOpen returns early when the current note was picked.
