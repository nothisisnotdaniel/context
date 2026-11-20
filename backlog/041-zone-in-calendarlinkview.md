# 041 Zone in CalendarLinkView

As a maintainer, I want the time zone on `CalendarLinkView`, so that its renderer draws only from its view like the other renderers.

## What

`CalendarLinkCommands.Render(CalendarLinkView, TimeZoneInfo zone)` takes the time zone as a separate parameter; put it on `CalendarLinkView` (documented like `CurrentView.Zone`, job 046) so the renderer draws only from its view.

## Why

Passing the zone separately breaks the view pattern the other renderers follow since job 046.
