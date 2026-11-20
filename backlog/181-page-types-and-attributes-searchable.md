# 181 Page types and attributes, searchable

As a Context user, I want pages to carry typed attributes such as a person's role and affiliation, so that I can find pages by them, for example every person whose role is Bereichsleiter.

## What

- Let any page have attributes (key and value), for example `role: Bereichsleiter` or `affiliation: [[ACME]]`.
- Let me define page types, each with the attributes its pages come with. A person page has `affiliation` and `role`; an organization page is a type of its own.
- An attribute value can link to another page, for example a person's `affiliation` links to the organization's page, which works like any other link and backlink.
- Search can find pages by attribute: searching for the role Bereichsleiter lists every person with that role.
- Open design question for the spec: real page types with their own attribute definitions, or attributes as special prefixed tags (for example `#role:Bereichsleiter`) that reuse the tag index. Pick one, say why, and keep the plain-text, local-files approach.

## Why

Notes about people and organizations are only useful once you can ask who holds a role or who belongs to an organization. Typed attributes with links make that a search instead of scrolling through pages.

## Notes

Added by the user on 2026-11-14 during the auto run. Builds on #51 People pages (job 156).
