# 230 Clean invisible and control characters in text

As a Context user, I want invisible, bidi and control characters kept out of names, titles and generated notes, so that nothing can look different from what it is or create tags and links I didn't write.

Category: security

## What

- Refuse invisible characters in person names and note titles (was #182): Make PersonParser.Normalize and NoteStore.ValidateTitle refuse Unicode Format characters (UnicodeCategory.Format, for example U+200B and U+202E), not only control characters. Check that OneLine and SafeText.Clean strip them wherever titles are shown. Bump the index ParserVersion if mention parsing changes.
- Replace lone surrogates in meeting note names (was #203): In MeetingNote (src/Context.Core/MeetingNote.cs, the name cleaning around line 229), replace every unpaired UTF-16 surrogate in the title with a space before the other clean-up steps, not only one at the cut point. Update the spec text for the name rules and add tests with a lone high and a lone low surrogate.
- Strip bidi and line-separator characters in SafeText (was #210): In SafeText.Clean, remove or replace Unicode bidi controls (U+202A–U+202E, U+2066–U+2069) and the line and paragraph separators (U+2028, U+2029) as well as other format characters. Check every caller (day end lists, todos, search snippets) still behaves, and add tests. Do the same in DesktopText.OneLine, and include U+200E and U+200F.
- Keep Times names from becoming tags or links in generated notes (was #213): In Briefing and DayEnd, escape or neutralise tag and link syntax (#tag, [[link]], and any other syntax the index parses) in Times checkpoint names and todo texts before writing them into a stored note. Keep the text readable. Check that search and tag lists no longer pick them up, and add tests.
- Strip stray control characters from inbox lines before triage writes (was #224): In InboxTriage (Lines, or before each target write), remove or refuse a lone `\r` and other control characters in a line's text before it is written to a new note, a todo or an appended line. Decide which, keep the inbox file itself unchanged until the line is triaged, and add tests.

## Why

- #182: Today @[Anna​Schmidt] creates a second person page that looks the same as Anna Schmidt, and U+202E can reorder a title on screen. That is confusing and can be used to disguise a page.
- #203: An unpaired surrogate from .ics data passes ValidateTitle, and APFS may reject it, so /meeting shows "Can't write <name>." for an appointment whose title only has one broken character.
- #210: Day end copies Times checkpoint names and todo texts into notes through SafeText, which lets these characters through, so a crafted name could display reversed or split.
- #213: Names are cleaned to one line but otherwise written verbatim, so Times data can create real tags and links in Context's index.
- #224: Only `\r\n` is folded today, so a hand-edited inbox line with a lone `\r` is copied verbatim into the target.

## Notes

- Merges #182, #203, #210, #213, #224 on 2026-11-16, to save the fixed cost of one job per item.
- Use quality mode. Put the cleaning in one shared place (SafeText and the name validation) and bump ParserVersion once if mention parsing changes.
- #182: Proposed by job 156-people-pages (review-security round 1, nit 1).
- #203: Proposed by job 167-meeting-notes-appointments (review-security round 1, nit 2). The CLI/desktop catch difference (review-code nit 2) is covered by #197. Proposed by job 167-meeting-notes-appointments (`factory/jobs/167-meeting-notes-appointments/`).
- #210: Proposed by job 171-days-end-daily-note (review-security round 1, nit 2). Proposed by job 171-days-end-daily-note (`factory/jobs/171-days-end-daily-note/`). Duplicate #148 (Replace bidi and format characters in shown text, proposed by job 139-desktop-import-export) was merged into this item on 2026-11-15.
- #213: Proposed by job 172-morning-briefing (review-security round 1, nit 2). Proposed by job 172-morning-briefing (`factory/jobs/172-morning-briefing/`).
- #224: Proposed by job 179-inbox-triage (review-security round 1, nit 1). Proposed by job 179-inbox-triage (`factory/jobs/179-inbox-triage/`).
