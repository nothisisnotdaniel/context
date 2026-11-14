# 188 Cap the size of note reads

As a Context user, I want Context to refuse to load a huge note or type page, so that one oversized file can't freeze the app or the index.

## What

Add one shared note size limit and use it wherever notes are read in full: NoteStore.Read callers, TagIndex.Sync and PageTypes.Load (types/* pages). A file over the limit is skipped with a clear message instead of being read into memory. Add tests for a note just under and just over the limit.

## Why

Today every note and types/* page is read with File.ReadAllText and no cap, so a very large file in the data folder can make indexing or type loading slow or run out of memory.

## Notes

Proposed by job 157-page-types-attributes (review-security round 1, nit 1).
