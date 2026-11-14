# 183 Turn a typed @[Name] into a mention chip right away

As a Context user, I want a mention to become a chip as soon as I finish typing it, so that I see straight away that it was recognised.

## What

Add an editor input rule: when the closing ] of @[Name] is typed and the name is valid, turn it into the mention chip at once, the same as when the note is opened. Keep the stored text unchanged.

## Why

Today a typed @[Name] only becomes a chip after the note is reopened, so it is unclear whether the mention worked.

## Notes

Proposed by job 156-people-pages (review-ux round 1, nit 2).
