# 291 Remove the Mail panel from the right sidebar

As a desktop user, I want no mail list in the right sidebar, so that the sidebar stays about my day and my notes.

## What

Remove RecentMailPanel (src/Context.Desktop/RecentMailPanel.razor) from aside.todos-region, and remove the mail reading that only it uses (DesktopState.RecentMail, IOutlookSource.RecentMail and the Inbox read in OutlookComSource.cs, so SenderName is no longer read), with its tests and CSS. Outlook appointments stay. Update the settings text, README and CLAUDE.md where they mention mail.

## Why

User review on 2026-11-18: remove Mails from the right sidebar.

## Notes

Requested by the user on 2026-11-18. Not reading SenderName also avoids most of Outlook's object-model security prompts. Makes #284 (field caps, mail part) and #287 (Loading in the mail panel) partly or fully moot.
