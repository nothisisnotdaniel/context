# 311 Skip one Outlook item when its Sensitivity can't be read

As a Windows user with Outlook, I want one unreadable appointment to be skipped instead of failing the whole calendar read, so that the rest of my day still shows.

Category: bug

## What

When reading `item.Sensitivity` on an Outlook appointment throws (for example a COMException), the whole Outlook read now fails as `Failed`. Skip that one item instead (treat it as hidden, so nothing private leaks) and keep reading the rest. Only checkable on Windows.

## Why

Job 227 code review: an exception from `item.Sensitivity` fails the whole Outlook read rather than skipping one item; nothing leaks, but one bad item hides the whole day.

## Notes

Windows-only behaviour; tests need a fake item whose Sensitivity throws.

Proposed by job 227-hide-private-appointments (`factory/jobs/227-hide-private-appointments/`).
