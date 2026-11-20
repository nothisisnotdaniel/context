# 413 Tell person rows apart from same-named notes in search

As a Context user, I want a person row in search results to look different from a plain note with the same name, so that I open the one I mean.

Category: ux

## What

In search results (SearchPanel.razor) a person page now shows only the name, for example Anna, so a plain note named Anna looks the same in the same list. Mark person rows so they can be told apart, for example with the people icon used in the People panel.

## Why

Raised by the ux review of job 338: only the hover title tells the two rows apart, so the user can open the wrong page.

## Notes

Proposed by job 338-person-pages-flat (`factory/jobs/338-person-pages-flat/`).
