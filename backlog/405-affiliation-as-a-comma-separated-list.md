# 405 Affiliation as a comma-separated list

As someone who keeps person pages, I want affiliation:: to take a comma-separated list, so that a person who belongs to several organisations can list them all on one line.

## What

Split every affiliation:: value at commas, as PersonAliases does for aliases::: each trimmed, non-empty part is its own affiliation (a part that is one [[link]] is also a note link). The People panel affiliation filter, /find affiliation and context find affiliation match any one part, and the panel shows the parts as a list. A single value and repeated affiliation:: lines keep working as today. Say it in the README attributes section.

## Why

User request on 2026-11-20: people often belong to more than one organisation, and today affiliation:: holds one value per line, so the filter and find miss the others.
