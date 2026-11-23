# 495 Checkbox icon for Todos, and the ! button opens the decision overview

As a user, I want the Todos button to show a checkbox and the ! button to open the decision overview, so that each icon leads where it looks like it leads.

Category: ui

## What

In the notes bar (NotesPanel.razor) the Todos button (aria-label Todos) shows an exclamation mark today. Give it a checkbox icon (a square with a tick, same size, stroke and currentColor as the other bar icons). Add a ! button (the current exclamation-mark SVG, aria-label Decisions, with a title) that opens the decision overview: DecisionsPanel with every decision, the same as /decisions without a tag (Main sets decisionsShown to the empty tag). Keep the bar keyboard-usable and the tests for the bar buttons up to date.

## Why

An exclamation mark reads as decisions or warnings, not todos, so the user looked for todos in the wrong place and had no one-click way to the decision overview.
