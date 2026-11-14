// Markdown <-> document. markdown-it in CommonMark mode with raw HTML off, plus three inline rules that mirror the C#
// TagParser, NoteLinkParser and PersonParser. The serializer writes the house style (- bullets, * emphasis, ** strong, ``` fences)
// and escapes only what would otherwise parse differently, so a note in that style comes back byte for byte.
import MarkdownIt from "markdown-it";
import { MarkdownParser, MarkdownSerializer, defaultMarkdownSerializer } from "prosemirror-markdown";
import { schema } from "./schema.js";

const MaxTagLength = 50;
const MaxTargetLength = 103;
const MaxTitleLength = 100;

const LetterRe = /^\p{L}$/u;
const LetterOrDigitRe = /^[\p{L}\p{Nd}]$/u;
const WhiteRe = /^\s$/;

function isLetter(c) { return c !== undefined && c.length === 1 && LetterRe.test(c); }
function isTagChar(c) { return c !== undefined && c.length === 1 && (LetterOrDigitRe.test(c) || c === "-" || c === "_"); }
function isWhite(c) { return c !== undefined && WhiteRe.test(c); }

// .NET's char.IsWhiteSpace, which PersonParser uses: JavaScript's \s without U+FEFF, plus U+0085.
function isNetWhite(c) { return c !== undefined && (c === "\u0085" || (c !== "\ufeff" && WhiteRe.test(c))); }

// .NET's char.IsControl: U+0000-U+001F and U+007F-U+009F.
function isControl(c) { const code = c.charCodeAt(0); return code <= 0x1f || (code >= 0x7f && code <= 0x9f); }

// PersonParser.RefusedChars plus the characters NoteStore.ValidateTitle refuses on every OS.
const PersonRefused = "[<>\"|?*/\\:";

const ReservedNames = new Set([
  "CON", "PRN", "AUX", "NUL",
  "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM\u00b9", "COM\u00b2", "COM\u00b3",
  "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT\u00b9", "LPT\u00b2", "LPT\u00b3",
]);

// PersonParser.Normalize: trimmed, whitespace runs become one space; null when it is empty, holds a refused or
// control character, is over 100 chars, starts with a dot, ends with a dot or is a name Windows reserves.
export function normalizePerson(text) {
  let out = "";
  let pendingSpace = false;
  for (const c of text.split("")) {
    if (isNetWhite(c)) {
      pendingSpace = out.length > 0;
      continue;
    }
    if (PersonRefused.includes(c) || isControl(c)) {
      return null;
    }
    if (pendingSpace) {
      out += " ";
      pendingSpace = false;
    }
    out += c;
    if (out.length > MaxTitleLength) {
      return null;
    }
  }
  if (out.length === 0 || out.startsWith(".") || out.endsWith(".")) {
    return null;
  }
  const dot = out.indexOf(".");
  const stem = (dot < 0 ? out : out.slice(0, dot)).replace(/ +$/, "");
  return ReservedNames.has(stem.toUpperCase()) ? null : out;
}

// TagParser.NormalizeBracket: trimmed, whitespace runs become one space, a letter first, then tag chars or spaces,
// at most 50 chars, lower-cased; null when it is not a tag.
export function normalizeBracket(text) {
  let out = "";
  let pendingSpace = false;
  for (const c of text.split("")) {
    if (isWhite(c)) {
      pendingSpace = out.length > 0;
      continue;
    }
    if (!(out.length === 0 ? isLetter(c) : isTagChar(c))) {
      return null;
    }
    if (pendingSpace) {
      out += " ";
      pendingSpace = false;
    }
    out += c;
    if (out.length > MaxTagLength) {
      return null;
    }
  }
  return out.length === 0 ? null : out.toLowerCase();
}

// #tag and #[tag with spaces], as in TagParser: '#' at the start of a line or after whitespace.
function tagRule(state, silent) {
  const src = state.src;
  const pos = state.pos;
  const max = state.posMax;
  if (src[pos] !== "#" || pos + 1 >= max) {
    return false;
  }
  if (pos > 0 && !isWhite(src[pos - 1])) {
    return false;
  }
  if (src[pos + 1] === "[") {
    let close = pos + 2;
    while (close < max && src[close] !== "]" && src[close] !== "\n") {
      close++;
    }
    if (close >= max || src[close] !== "]" || src[close + 1] === "(") {
      return false;
    }
    const name = normalizeBracket(src.slice(pos + 2, close));
    if (name === null) {
      return false;
    }
    if (!silent) {
      const token = state.push("tag", "", 0);
      token.content = src.slice(pos, close + 1);
      token.meta = { tag: name };
    }
    state.pos = close + 1;
    return true;
  }
  if (!isLetter(src[pos + 1])) {
    return false;
  }
  let end = pos + 2;
  while (end < max && isTagChar(src[end])) {
    end++;
  }
  if (end - pos - 1 > MaxTagLength) {
    return false;
  }
  if (!silent) {
    const token = state.push("tag", "", 0);
    token.content = src.slice(pos, end);
    token.meta = { tag: src.slice(pos + 1, end).toLowerCase() };
  }
  state.pos = end;
  return true;
}

// @[Person name], as in PersonParser: '@' at the start of a line or after whitespace, then '[', text up to the first
// ']' on the line, not followed by '('.
function mentionRule(state, silent) {
  const src = state.src;
  const pos = state.pos;
  const max = state.posMax;
  if (src[pos] !== "@" || src[pos + 1] !== "[" || pos + 1 >= max) {
    return false;
  }
  if (pos > 0 && !isNetWhite(src[pos - 1])) {
    return false;
  }
  let close = pos + 2;
  while (close < max && src[close] !== "]" && src[close] !== "\n") {
    close++;
  }
  if (close >= max || src[close] !== "]" || src[close + 1] === "(") {
    return false;
  }
  const name = normalizePerson(src.slice(pos + 2, close));
  if (name === null) {
    return false;
  }
  if (!silent) {
    const token = state.push("mention", "", 0);
    token.content = src.slice(pos, close + 1);
    token.meta = { name };
  }
  state.pos = close + 1;
  return true;
}

// [[Note]], as in NoteLinkParser: text with no '[' up to the first ']' on the line, then "]]"; never after '#' or '@'.
function noteLinkRule(state, silent) {
  const src = state.src;
  const pos = state.pos;
  const max = state.posMax;
  if (src[pos] !== "[" || src[pos + 1] !== "[" || (pos > 0 && (src[pos - 1] === "#" || src[pos - 1] === "@"))) {
    return false;
  }
  let close = pos + 2;
  while (close < max && src[close] !== "]" && src[close] !== "[" && src[close] !== "\n") {
    close++;
  }
  if (close + 1 >= max || src[close] !== "]" || src[close + 1] !== "]") {
    return false;
  }
  const target = src.slice(pos + 2, close).trim();
  if (target.length === 0 || target.length > MaxTargetLength) {
    return false;
  }
  if (!silent) {
    const token = state.push("note_link", "", 0);
    token.content = src.slice(pos, close + 2);
    token.meta = { target };
  }
  state.pos = close + 2;
  return true;
}

const md = MarkdownIt("commonmark", { html: false, linkify: false, typographer: false });
md.inline.ruler.before("link", "note_link", noteLinkRule);
md.inline.ruler.before("link", "tag", tagRule);
md.inline.ruler.before("link", "mention", mentionRule);

const TaskRe = /^\[([ xX])\](?: |$)/;

// Soft line breaks stay line breaks (the text keeps its wrapping), and "[ ] " / "[x] " at the start of a list
// item's first paragraph becomes the item's checked attribute.
function tidy(tokens) {
  for (let i = 0; i < tokens.length; i++) {
    const token = tokens[i];
    if (token.type === "inline" && token.children) {
      for (const child of token.children) {
        if (child.type === "softbreak") {
          child.type = "text";
          child.content = "\n";
        }
      }
    }
    if (token.type === "list_item_open"
      && tokens[i + 1] && tokens[i + 1].type === "paragraph_open"
      && tokens[i + 2] && tokens[i + 2].type === "inline") {
      const children = tokens[i + 2].children || [];
      const first = children[0];
      const match = first && first.type === "text" ? TaskRe.exec(first.content) : null;
      if (match) {
        token.meta = { checked: match[1] !== " " };
        first.content = first.content.slice(match[0].length);
      }
    }
  }
  return tokens;
}

const tokenizer = { parse: (text, env) => tidy(md.parse(text, env)) };

function listIsTight(tokens, i) {
  while (++i < tokens.length) {
    if (tokens[i].type !== "list_item_open") {
      return tokens[i].hidden;
    }
  }
  return false;
}

const parser = new MarkdownParser(schema, tokenizer, {
  blockquote: { block: "blockquote" },
  paragraph: { block: "paragraph" },
  list_item: { block: "list_item", getAttrs: tok => ({ checked: tok.meta && typeof tok.meta.checked === "boolean" ? tok.meta.checked : null }) },
  bullet_list: { block: "bullet_list", getAttrs: (_, tokens, i) => ({ tight: listIsTight(tokens, i) }) },
  ordered_list: { block: "ordered_list", getAttrs: (tok, tokens, i) => ({ order: +tok.attrGet("start") || 1, tight: listIsTight(tokens, i) }) },
  heading: { block: "heading", getAttrs: tok => ({ level: +tok.tag.slice(1) }) },
  code_block: { block: "code_block", noCloseToken: true },
  fence: { block: "code_block", getAttrs: tok => ({ params: tok.info || "" }), noCloseToken: true },
  hr: { node: "horizontal_rule" },
  image: {
    node: "image",
    getAttrs: tok => ({ src: tok.attrGet("src"), title: tok.attrGet("title") || null, alt: (tok.children[0] && tok.children[0].content) || null }),
  },
  hardbreak: { node: "hard_break" },
  em: { mark: "em" },
  strong: { mark: "strong" },
  link: { mark: "link", getAttrs: tok => ({ href: tok.attrGet("href"), title: tok.attrGet("title") || null }) },
  code_inline: { mark: "code", noCloseToken: true },
  tag: { node: "tag", getAttrs: tok => ({ raw: tok.content, tag: tok.meta.tag }) },
  note_link: { node: "noteLink", getAttrs: tok => ({ raw: tok.content, target: tok.meta.target }) },
  mention: { node: "mention", getAttrs: tok => ({ raw: tok.content, name: tok.meta.name }) },
});

// ---- Escaping: only what would otherwise parse as something else ----

const AsciiPunct = /^[!-\/:-@\[-`{-~]$/;
const Autolink = /^<(?:[A-Za-z][A-Za-z0-9.+-]{1,31}:[^<>\x00-\x20]*|[A-Za-z0-9.!#$%&'*+\/=?^_`{|}~-]+@[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*)>/;
const Entity = /^&(?:#[0-9]{1,7}|#[xX][0-9a-fA-F]{1,6}|[A-Za-z][A-Za-z0-9]{1,31});/;
const Alnum = /^[\p{L}\p{N}]$/u;

function isAlnum(c) { return c !== undefined && c !== "" && Alnum.test(c); }

// Characters that only matter at the start of a line: headings, list markers, quotes, setext underlines,
// tilde fences and reference definitions.
function escapeLineStart(line) {
  if (/^#{1,6}(?=[ \t]|$)/.test(line)) {
    return "\\" + line;
  }
  if (/^[-+](?=[ \t]|$)/.test(line) || /^-+[ \t]*$/.test(line) || /^=+[ \t]*$/.test(line) || /^>/.test(line) || /^~~~/.test(line)) {
    return "\\" + line;
  }
  const ordered = /^(\d{1,9})([.)])(?=[ \t]|$)/.exec(line);
  if (ordered) {
    return ordered[1] + "\\" + line.slice(ordered[1].length);
  }
  if (/^\[[^\]]*\]:/.test(line)) {
    return "\\" + line;
  }
  return line;
}

// Escapes a text node. lineStart: whether the text starts a line; after: the first character written after it
// ("" when unknown).
export function escapeText(text, lineStart, after) {
  // A '[' is escaped only when a link could follow from it: "](" or "][" later in the text.
  const linkClose = /\]\s*[(\[]/.test(text) || (text.endsWith("]") && (after === "(" || after === "["));
  let out = "";
  let line = "";
  let atLineStart = lineStart;
  const flush = () => {
    out += atLineStart ? escapeLineStart(line) : line;
    line = "";
  };
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    const prev = i > 0 ? text[i - 1] : "";
    const next = i + 1 < text.length ? text[i + 1] : after;
    switch (c) {
      case "\n":
        flush();
        out += "\n";
        atLineStart = true;
        break;
      case "\\":
        line += next === "" || next === "\n" || AsciiPunct.test(next) ? "\\\\" : c;
        break;
      case "`":
      case "*":
      case "|":
        line += "\\" + c;
        break;
      case "_":
        line += isAlnum(prev) && isAlnum(next) ? c : "\\_";
        break;
      case "[":
        line += linkClose && text.indexOf("]", i) >= 0 ? "\\[" : c;
        break;
      case "<":
        line += Autolink.test(text.slice(i)) ? "\\<" : c;
        break;
      case "&":
        line += Entity.test(text.slice(i)) ? "\\&" : c;
        break;
      default:
        line += c;
    }
  }
  flush();
  return out;
}

function firstChar(node) {
  if (!node) {
    return "";
  }
  if (node.isText) {
    return node.text[0];
  }
  if (node.type.name === "tag" || node.type.name === "noteLink" || node.type.name === "mention") {
    return node.attrs.raw[0];
  }
  return "";
}

function taskPrefix(item) {
  return item.attrs.checked === null ? "" : item.attrs.checked ? "[x] " : "[ ] ";
}

const base = defaultMarkdownSerializer.nodes;

const serializer = new MarkdownSerializer({
  blockquote: base.blockquote,
  code_block: base.code_block,
  heading: base.heading,
  horizontal_rule(state, node) {
    state.write("---");
    state.closeBlock(node);
  },
  bullet_list(state, node) {
    state.renderList(node, "  ", i => "- " + taskPrefix(node.child(i)));
  },
  ordered_list(state, node) {
    const start = node.attrs.order;
    const width = String(start + node.childCount - 1).length;
    const space = state.repeat(" ", width + 2);
    state.renderList(node, space, i => {
      const n = String(start + i);
      return state.repeat(" ", width - n.length) + n + ". " + taskPrefix(node.child(i));
    });
  },
  list_item: base.list_item,
  paragraph: base.paragraph,
  image: base.image,
  hard_break: base.hard_break,
  tag(state, node) {
    state.write(node.attrs.raw);
  },
  noteLink(state, node) {
    state.write(node.attrs.raw);
  },
  mention(state, node) {
    state.write(node.attrs.raw);
  },
  text(state, node, parent, index) {
    if (state.inAutolink) {
      state.text(node.text, false);
      return;
    }
    const lineStart = state.atBlockStart || state.out === "" || state.out.endsWith("\n");
    const after = index + 1 < parent.childCount ? firstChar(parent.child(index + 1)) : "";
    state.text(escapeText(node.text, lineStart, after), false);
  },
}, {
  em: { open: "*", close: "*", mixable: true, expelEnclosingWhitespace: true },
  strong: { open: "**", close: "**", mixable: true, expelEnclosingWhitespace: true },
  link: defaultMarkdownSerializer.marks.link,
  code: defaultMarkdownSerializer.marks.code,
});

/** Parses note text (\n line endings) into a document. */
export function parse(markdown) {
  const doc = parser.parse(markdown);
  const trailingNewline = markdown.length === 0 || markdown.endsWith("\n");
  return trailingNewline ? doc : schema.topNodeType.create({ trailingNewline: false }, doc.content);
}

/** Writes a document back as Markdown. */
export function serialize(doc) {
  const out = serializer.serialize(doc);
  return out.length > 0 && doc.attrs.trailingNewline ? out + "\n" : out;
}

/** The text after opening and saving without an edit. */
export function roundTrip(markdown) {
  return serialize(parse(markdown));
}

/** True when opening and saving without an edit gives the same text back. */
export function isLossless(markdown) {
  return roundTrip(markdown) === markdown;
}

/**
 * The tags (normalized, distinct), link targets (distinct ignoring case) and mentioned people (distinct ignoring case,
 * first spelling kept) the editor finds, as JSON.
 */
export function inlineNodes(markdown) {
  const tags = [];
  const links = [];
  const mentions = [];
  const seenLinks = new Set();
  const seenMentions = new Set();
  parse(markdown).descendants(node => {
    if (node.type.name === "tag" && !tags.includes(node.attrs.tag)) {
      tags.push(node.attrs.tag);
    } else if (node.type.name === "noteLink" && !seenLinks.has(node.attrs.target.toLowerCase())) {
      seenLinks.add(node.attrs.target.toLowerCase());
      links.push(node.attrs.target);
    } else if (node.type.name === "mention" && !seenMentions.has(node.attrs.name.toLowerCase())) {
      seenMentions.add(node.attrs.name.toLowerCase());
      mentions.push(node.attrs.name);
    }
    return true;
  });
  return JSON.stringify({ tags, links, mentions });
}

/** The parsed document as JSON. */
export function docJson(markdown) {
  return JSON.stringify(parse(markdown).toJSON());
}
