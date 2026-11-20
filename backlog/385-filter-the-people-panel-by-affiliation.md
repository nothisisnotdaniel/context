# 385 Filter the People panel by affiliation and role

As the user, I want to filter the People panel by affiliation and role, so that I can find everyone from one organization or with one role at a glance.

## What

The People panel (PeoplePanel.razor) today lists person pages by name only, from the snapshot's note list. Add two filters above the list, Affiliation and Role, each listing the distinct values from the person pages' affiliation:: and role:: lines (an affiliation [[ACME]] shows as ACME and matches by AttributeParser.ValueKey, as FindView does). Choosing a value shows only the people that have it; both filters can be combined, and clearing them shows everyone. Each row may show the person's role and affiliation in grey under the name. Values come from the index (TagIndex attributes), never from reading files in the panel; sensitive pages are handled as in the rest of the people views. Keyboard accessible, both themes.

## Why

The user asked for it. Today affiliation and role are only pre-filled keys on a new person page (PageTypes) and are searchable through /find and context find, but nothing in the People panel uses them.

## Notes

Requested by the user on 2026-11-19. Builds on #51 people pages, #305 People panel and the attribute index.
