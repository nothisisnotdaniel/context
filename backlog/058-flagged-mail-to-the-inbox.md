# 058 Flagged mail to the inbox

As a Context user, I want mail I flag in Outlook to land in my Context inbox, so that mail follow-ups go through the same triage as everything else.

## What

Each mail the user flags in Outlook (read through Outlook as a source) adds one line to `inbox.md` with the sender, subject and received date and a way to open that mail in Outlook again, so it goes through inbox triage like any capture. Each mail is added once (its Outlook id remembered in the settings file); unflagging it later doesn't remove the line. Checked when the desktop app starts and every few minutes while it runs; off by default, a setting. Mail text beyond the subject is not copied. When its line is handled in inbox triage (turned into a note, a todo or an addition, or dropped), Context marks the mail's flag as complete in Outlook, so the Outlook inbox empties as the Context inbox does; the todo or note keeps the way to open the mail. A setting can also move the handled mail to a chosen Outlook folder (for example Archive), off by default. These are the only writes to Outlook: they happen only after the target is saved, a mail that was changed or deleted meanwhile is left alone with a grey hint, and nothing is ever deleted. Builds on Outlook as a source and quick capture.

## Why

Flagged mail is a second to-do list that's easy to forget. Adding each flagged mail once, with a way back to it, puts it where I already look.
