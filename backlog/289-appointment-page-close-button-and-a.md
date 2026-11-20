# 289 Appointment page: × close button and a Notes heading

As a desktop user, I want the appointment page to close with an × like other pages and to label its note list, so that it is clear what the list is and how to leave.

## What

In src/Context.Desktop/AppointmentPanel.razor, replace the text button 'Close' in the panel bar with an icon-only × close button (aria-label and title 'Close'), styled like the app's other icon buttons. Above the list of related notes (ul.appointment-notes, and the empty states), add a heading 'Notes' in the style of the app's other section headings. Light and dark mode.

## Why

User review on 2026-11-18: the appointment page should have the × as a close button, and the notes should have a header that says Notes.

## Notes

Requested by the user on 2026-11-18.
