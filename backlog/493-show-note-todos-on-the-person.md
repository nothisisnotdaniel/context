# 493 Show note todos on the person's page

As the user, I want todo lines in my notes that mention a person to show on that person's page, so that I see everything I have to do with them in one place, wherever I wrote it.

Category: feature

## What

On a person's page (`PersonView.Build`, desktop `PersonPanel`), also list the open note todos (`- { }` lines, the `note_todos` rows read by `TagIndex.NoteTodos()`) whose text mentions the person, matched at query time with `PersonParser.Parse` and the person's aliases, the same way as todo-list todos and appointments. Show them in their own section, for example `Todos in notes`, after `Open todos`: each with its due date if any, its text with mentions drawn as in the todo panel, and the note's name as a link that opens the note. Order by due date (none last), then note name, then line. A ticked note line drops out once the index syncs. A person's own page (`people/<Name>`) counts like any other note. The person's empty-page text and `Kind` count these todos too. Nothing new is stored: no `ParserVersion` change. The command-line person view, if it has one, shows them the same way.

## Why

Today a todo written as a `- { }` line in a note only makes the whole note appear under `Mentioned in` on the person's page; the todo itself is not listed under the person's todos. The user expected it there and thought the mention didn't work. Without this, todos written in meeting notes stay hidden from the person view, and the user has to keep them in the todo list twice.

## Notes

Raised on 23 Nov when a `- { }` todo with an `@[Name]` mention didn't show under Open todos on the person's page. Related: job 159 (todo-list todos on the person page), job 201 / 329 (note todos as `- { }` lines), job 361 (done todos).
