# 331 Check Cmd+Shift+V plain paste in the macOS app

As the developer, I want to confirm by hand that Cmd+Shift+V pastes plain text in the macOS desktop app, so that the README states what really happens.

## What

On macOS, open a note in the desktop app, copy a link and some Excel cells, and press Cmd+Shift+V. Check whether the text is pasted as is (no table, no title) or whether WKWebView sends no paste event at all. Update the smart paste note in the README with the result.

## Why

Job 244 could not test this in WKWebView; the README says it is unchecked.

## Notes

From job 244-smart-paste (review-ux 1).
