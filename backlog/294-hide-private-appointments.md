# 294 Hide private appointments

As a desktop user, I want appointments marked private to stay out of Context, so that private meetings don't show in the sidebar, the snapshot or meeting notes.

## What

Skip appointments marked private: from Outlook, items whose Sensitivity is Private (2) or Confidential (3) (read Sensitivity in OutlookComSource.cs); from .ics files and calendar feeds, events with CLASS:PRIVATE or CLASS:CONFIDENTIAL. They don't appear in the sidebar appointments list, Snapshot.Current, /meeting numbering or the appointment page. Say in the spec whether this is always on or a setting (default: always on, no setting).

## Why

User review on 2026-11-18: can we exclude private appointments?

## Notes

Requested by the user on 2026-11-18.
