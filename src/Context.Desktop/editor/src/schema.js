// The document schema: the CommonMark nodes, task list items, and the tag, noteLink and mention atoms.
// toDOM never builds <img>, <a href> or inline styles: images are a grey text placeholder and links are plain spans.
import { Schema } from "prosemirror-model";

const chips = {
  tag: { name: "tag", role: "button" },
  noteLink: { name: "note-link", role: "link" },
  mention: { name: "mention", role: "link" },
};

/** The toDOM spec of a chip (kind "tag", "noteLink" or "mention"): a span the keyboard can reach, with raw as its text. */
export function chipDom(kind, raw) {
  const chip = chips[kind];
  return ["span", { class: "chip " + chip.name + " accent", title: raw, tabindex: "0", role: chip.role }, raw];
}

export const schema = new Schema({
  nodes: {
    doc: {
      content: "block+",
      // False when the file did not end with a newline, so such a file still round-trips.
      attrs: { trailingNewline: { default: true } },
    },
    paragraph: {
      content: "inline*",
      group: "block",
      parseDOM: [{ tag: "p" }],
      toDOM() { return ["p", 0]; },
    },
    blockquote: {
      content: "block+",
      group: "block",
      parseDOM: [{ tag: "blockquote" }],
      toDOM() { return ["blockquote", 0]; },
    },
    horizontal_rule: {
      group: "block",
      parseDOM: [{ tag: "hr" }],
      toDOM() { return ["div", ["hr"]]; },
    },
    heading: {
      attrs: { level: { default: 1 } },
      content: "inline*",
      group: "block",
      defining: true,
      parseDOM: [1, 2, 3, 4, 5, 6].map(level => ({ tag: "h" + level, attrs: { level } })),
      toDOM(node) { return ["h" + node.attrs.level, 0]; },
    },
    code_block: {
      content: "text*",
      group: "block",
      code: true,
      defining: true,
      marks: "",
      attrs: { params: { default: "" } },
      parseDOM: [{ tag: "pre", preserveWhitespace: "full" }],
      toDOM() { return ["pre", ["code", 0]]; },
    },
    ordered_list: {
      content: "list_item+",
      group: "block",
      attrs: { order: { default: 1 }, tight: { default: true } },
      parseDOM: [{ tag: "ol", getAttrs: dom => ({ order: dom.hasAttribute("start") ? +dom.getAttribute("start") || 1 : 1 }) }],
      toDOM(node) { return ["ol", { start: node.attrs.order === 1 ? null : String(node.attrs.order) }, 0]; },
    },
    bullet_list: {
      content: "list_item+",
      group: "block",
      attrs: { tight: { default: true } },
      parseDOM: [{ tag: "ul" }],
      toDOM() { return ["ul", 0]; },
    },
    list_item: {
      content: "paragraph block*",
      defining: true,
      // null: a plain item; true / false: a task item (- [x] / - [ ]).
      attrs: { checked: { default: null } },
      parseDOM: [{ tag: "li" }],
      toDOM(node) {
        if (node.attrs.checked === null) {
          return ["li", 0];
        }
        return ["li", { class: node.attrs.checked ? "task done" : "task" }, ["div", { class: "task-body" }, 0]];
      },
    },
    text: { group: "inline" },
    // #tag or #[tag with spaces]; raw keeps the text exactly as written.
    tag: {
      inline: true,
      group: "inline",
      atom: true,
      selectable: true,
      attrs: { raw: {}, tag: {} },
      toDOM(node) { return chipDom("tag", node.attrs.raw); },
    },
    // [[Note]] or [[Note|alias]]; raw keeps the text exactly as written, target is the trimmed inner text.
    noteLink: {
      inline: true,
      group: "inline",
      atom: true,
      selectable: true,
      attrs: { raw: {}, target: {} },
      toDOM(node) { return chipDom("noteLink", node.attrs.raw); },
    },
    // @[Person name]; raw keeps the text exactly as written, name is the normalized person name.
    mention: {
      inline: true,
      group: "inline",
      atom: true,
      selectable: true,
      attrs: { raw: {}, name: {} },
      toDOM(node) { return chipDom("mention", node.attrs.raw); },
    },
    image: {
      inline: true,
      group: "inline",
      atom: true,
      attrs: { src: {}, alt: { default: null }, title: { default: null } },
      toDOM(node) {
        const alt = node.attrs.alt || "";
        return ["span", { class: "image-placeholder grey", title: node.attrs.src }, alt ? "[image: " + alt + "]" : "[image]"];
      },
    },
    hard_break: {
      inline: true,
      group: "inline",
      selectable: false,
      parseDOM: [{ tag: "br" }],
      toDOM() { return ["br"]; },
    },
  },
  marks: {
    em: {
      parseDOM: [{ tag: "i" }, { tag: "em" }],
      toDOM() { return ["em"]; },
    },
    strong: {
      parseDOM: [{ tag: "strong" }, { tag: "b" }],
      toDOM() { return ["strong"]; },
    },
    // Rendered without a live href: a span with the address in its title.
    link: {
      attrs: { href: {}, title: { default: null } },
      inclusive: false,
      toDOM(mark) { return ["span", { class: "link", title: mark.attrs.href }, 0]; },
    },
    code: {
      code: true,
      parseDOM: [{ tag: "code" }],
      toDOM() { return ["code"]; },
    },
  },
});
