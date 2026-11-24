# 505 Raise the contrast of desktop text placeholders in dark mode

As a Context user starting a checkpoint, I want to read what an empty name field expects in the dark theme before I type.

Category: bug

## What

- Give the Times checkpoint name placeholder and the standard desktop text placeholders that share its low-contrast rendering a readable themed foreground in dark mode. Use the common text-field styling where possible and preserve the existing light appearance.
- At normal text size, reach at least 4.5:1 between the visible placeholder and its actual input background in both explicit themes. Verify the real checkpoint name field and one ordinary text field, including keyboard focus. The chosen project, entered name and existing light/dark field colors should remain readable.
- Repeat manual check #456 in a native desktop window with synthetic Times data, capturing the empty and populated form in both themes. Cancel the test form without making a booking.

## Why

Windows WebView2 on develop 29b1b32 rendered the empty checkpoint name hint as #757575 on #1c1c1e, only 3.69:1. It is the only visible label on that field. A regular text field reproduced the same values. In Light the placeholder reached 4.61:1. The selected project and entered checkpoint name were already readable in both themes (11.20:1 and 15.63:1 in Dark).

## Notes

Found by the delegated #456 manual desktop check. The project selector matches the native People selector and the populated name matches ordinary text inputs. Scope the style change to the weak placeholder foreground. #412 concerns source buttons and the other open contrast checks concern their own controls.
