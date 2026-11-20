# 156 Hide the empty editor box when the editor fails to start

As a Context user, I want the empty editor box to go away when the editor fails to start, so that only the error line shows and I am not tempted to type into a dead box.

## What

In src/Context.Desktop/NoteEditor.razor, when the page editor fails to start, hide (or do not render) the empty editor host under the new error line, keeping the error line and the rest of the page as they are.

## Why

Raised by the ui review of job 143: after a failed start the empty editor box stays under the error line, which looks like an editor that works but shows nothing.

## Notes

Proposed by job 143-editor-mount-error (`factory/jobs/143-editor-mount-error/`).
