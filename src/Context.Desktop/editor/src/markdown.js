// Markdown <-> document. markdown-it in CommonMark mode with raw HTML off, plus three inline rules that mirror the C#
// TagParser, NoteLinkParser and PersonParser. The serializer writes the house style (- bullets, * emphasis, ** strong, ``` fences)
// and never escapes text (job 290): what the user typed is what the file gets. The parser keeps a backslash escape
// (\*) and an entity (&amp;) as their source characters in the text, so a note that holds them comes back byte for byte.
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
export function isNetWhite(c) { return c !== undefined && (c === "\u0085" || (c !== "\ufeff" && WhiteRe.test(c))); }

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

// An alt text of file-name characters only, written without escapes (no markup can start in it).
const PlainAlt = /^[A-Za-z0-9_.-]+$/;

// [label](.attachments/name) with a file-name label and a stored name: the label is plain text, so a name such as
// "_a_.pdf" or "a__b.pdf" stays as written (no emphasis, no escapes) and round-trips byte for byte.
const AttachmentLink = /^\[([A-Za-z0-9_.-]+)\]\((\.attachments\/[A-Za-z0-9_][A-Za-z0-9_.-]*)\)/;

function attachmentLinkRule(state, silent) {
  if (state.src[state.pos] !== "[") {
    return false;
  }
  const match = AttachmentLink.exec(state.src.slice(state.pos, state.posMax));
  if (!match) {
    return false;
  }
  if (!silent) {
    const open = state.push("link_open", "a", 1);
    open.attrs = [["href", match[2]]];
    const text = state.push("text", "", 0);
    text.content = match[1];
    state.push("link_close", "a", -1);
  }
  state.pos += match[0].length;
  return true;
}

const md = MarkdownIt("commonmark", { html: false, linkify: false, typographer: false });
md.inline.ruler.before("link", "note_link", noteLinkRule);
md.inline.ruler.before("link", "attachment_link", attachmentLinkRule);
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

// Backslash escapes and entities stay as written: each text_special token (markdown-it 14) becomes its source text
// before text_join merges it into the text around it.
function keepEscapes(state) {
  const visit = tokens => {
    for (const token of tokens) {
      if (token.type === "text_special") {
        token.content = token.markup;
      }
      if (token.children) {
        visit(token.children);
      }
    }
  };
  visit(state.tokens);
}

md.core.ruler.before("text_join", "keep_escapes", keepEscapes);

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
  fence: { block: "code_block", getAttrs: tok => ({ params: tok.info || "", fence: tok.markup || "" }), noCloseToken: true },
  hr: { node: "horizontal_rule" },
  image: {
    node: "image",
    // A plain file-name alt (an attachment's stored name) is kept exactly as written, so it round-trips byte for byte.
    getAttrs: tok => ({
      src: tok.attrGet("src"),
      title: tok.attrGet("title") || null,
      alt: PlainAlt.test(tok.content || "") ? tok.content : (tok.children[0] && tok.children[0].content) || null,
    }),
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

function taskPrefix(item) {
  return item.attrs.checked === null ? "" : item.attrs.checked ? "[x] " : "[ ] ";
}

const base = defaultMarkdownSerializer.nodes;

const serializer = new MarkdownSerializer({
  blockquote: base.blockquote,
  // A fence keeps its character (``` or ~~~) and its length, made longer than any run of that character inside.
  code_block(state, node) {
    const char = node.attrs.fence[0] === "~" ? "~" : "`";
    const runs = node.textContent.match(char === "~" ? /~{3,}/g : /`{3,}/g) || [];
    let length = Math.max(3, node.attrs.fence.length);
    for (const run of runs) {
      length = Math.max(length, run.length + 1);
    }
    const fence = char.repeat(length);
    state.write(fence + (node.attrs.params || "") + "\n");
    state.text(node.textContent, false);
    state.write("\n");
    state.write(fence);
    state.closeBlock(node);
  },
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
  image(state, node) {
    const alt = node.attrs.alt || "";
    // The alt text is written as is, like any text (job 290): the parser keeps its escapes.
    state.write("![" + alt + "](" + node.attrs.src.replace(/[\(\)]/g, "\\$&")
      + (node.attrs.title ? " \"" + node.attrs.title.replace(/"/g, "\\\"") + "\"" : "") + ")");
  },
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
  // Text is written as is, never escaped (job 290).
  text(state, node) {
    state.text(node.text, false);
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

// The first line when it is the note's own title: "# " (an ATX level-1 heading), then text that, trimmed, equals the
// name or its last segment ignoring case.
const TitleLine = /^# ([^\n]*)(?:\n|$)/;
// Blank lines (spaces and tabs only) straight after the title line.
const BlankLines = /^(?:[ \t]*\n)*/;

function titleSplit(markdown, name) {
  if (typeof markdown !== "string" || typeof name !== "string" || name.length === 0) {
    return { head: "", body: markdown };
  }
  const line = TitleLine.exec(markdown);
  if (!line) {
    return { head: "", body: markdown };
  }
  const text = line[1].trim().toLowerCase();
  const full = name.trim().toLowerCase();
  const last = full.slice(full.lastIndexOf("/") + 1).trim();
  if (text.length === 0 || (text !== full && text !== last)) {
    return { head: "", body: markdown };
  }
  let end = line[0].length;
  if (markdown[end - 1] === "\n") {
    end += BlankLines.exec(markdown.slice(end))[0].length;
  }
  return { head: markdown.slice(0, end), body: markdown.slice(end) };
}

/**
 * Splits note text into the hidden title (head: the first line "# <name>" with its newline and the blank lines after
 * it, or "") and the body the editor shows. head + body is always the input. Returns { head, body } as JSON.
 */
export function splitTitle(markdown, name) {
  return JSON.stringify(titleSplit(markdown, name));
}

/** The editor's whole text: the hidden head, then the body; a head without a newline gets a blank line first. */
export function joinTitle(head, body) {
  if (head.length > 0 && !head.endsWith("\n") && body.length > 0) {
    return head + "\n\n" + body;
  }
  return head + body;
}

// AttributeParser.KeyEnd: the length of the key at the start of line (a letter, then letters, digits, "-" or "_",
// tested per UTF-16 unit); 0 when it doesn't start with a letter.
function keyEnd(line) {
  if (!isLetter(line[0])) {
    return 0;
  }
  let end = 1;
  while (end < line.length && isTagChar(line[end])) {
    end++;
  }
  return end;
}

// The key length of an attribute-shaped line (a column-0 key of at most 50 chars directly followed by "::", any
// value), or 0.
function attributeKeyEnd(line) {
  const end = keyEnd(line);
  return end > 0 && end <= MaxTagLength && line.startsWith("::", end) ? end : 0;
}

// .NET's string.Trim (char.IsWhiteSpace at both ends).
function netTrim(text) {
  let start = 0;
  let end = text.length;
  while (start < end && isNetWhite(text[start])) {
    start++;
  }
  while (end > start && isNetWhite(text[end - 1])) {
    end--;
  }
  return text.slice(start, end);
}

// The lines of text, each with its "\n" (the last one may have none).
function rawLines(text) {
  const lines = [];
  let pos = 0;
  while (pos < text.length) {
    const nl = text.indexOf("\n", pos);
    const end = nl < 0 ? text.length : nl + 1;
    lines.push(text.slice(pos, end));
    pos = end;
  }
  return lines;
}

// A line without its line ending ("\n" or "\r\n").
function lineText(raw) {
  return raw.replace(/\r?\n$/, "");
}

const BlankLine = /^[ \t]*\r?\n?$/;

/**
 * Splits the body (the text after the hidden title head) into the attribute block (attrs: the run of attribute-shaped
 * lines from its first line on, plus the blank lines straight after it; "" when the first line is not one) and the
 * rest. attrs + body is always the input.
 */
export function attributeSplit(body) {
  if (typeof body !== "string") {
    return { attrs: "", body };
  }
  const lines = rawLines(body);
  let count = 0;
  let length = 0;
  while (count < lines.length && attributeKeyEnd(lineText(lines[count])) > 0) {
    length += lines[count].length;
    count++;
  }
  if (count === 0) {
    return { attrs: "", body };
  }
  for (let i = count; i < lines.length && BlankLine.test(lines[i]); i++) {
    length += lines[i].length;
  }
  return { attrs: body.slice(0, length), body: body.slice(length) };
}

// The rows of an attribute block: each attribute line's raw text (line ending included), key as written and value
// trimmed; and the trailing blank lines.
function attributeBlockRows(attrs) {
  const rows = [];
  let length = 0;
  for (const raw of rawLines(attrs)) {
    const text = lineText(raw);
    const end = attributeKeyEnd(text);
    if (end === 0) {
      break;
    }
    rows.push({ raw, key: text.slice(0, end), value: netTrim(text.slice(end + 2)) });
    length += raw.length;
  }
  return { rows, trailing: attrs.slice(length) };
}

/** The rows of an attribute block as JSON [{ key, value }]: key as written, value trimmed. */
export function attributeBlockRowsJson(attrs) {
  return JSON.stringify(attributeBlockRows(attrs).rows.map(row => ({ key: row.key, value: row.value })));
}

/** The note's attribute block (see attributeSplit) as JSON [{ key, value }]: key as written, value trimmed; no DOM. */
export function attributeRows(markdown, name) {
  const { body } = titleSplit(markdown, name);
  return attributeBlockRowsJson(attributeSplit(typeof body === "string" ? body : "").attrs);
}

// A key or value from outside with every control character (and the Unicode line and paragraph separators) as a
// space, so an edit can never add a line.
function oneLine(text) {
  let out = "";
  for (const c of text) {
    out += c.length === 1 && (isControl(c) || c === "\u2028" || c === "\u2029") ? " " : c;
  }
  return out;
}

const MaxEdits = 1000;

/**
 * The attribute block attrs after editsJson ([{ line, key, value }] in the new order; line is the original row index
 * or -1 for a new row). A row whose key and value are unchanged keeps its raw bytes; a changed or new one is written
 * "key:: value" ("key::" when the value is empty) with the block's line ending. New rows go after the last row, before
 * the trailing blank lines; an empty list removes the block. Returns null, changing nothing, for malformed edits
 * (not an array, a line out of range, a key or value that isn't a string, a key that is no attribute key).
 */
export function editAttributes(attrs, editsJson) {
  if (typeof attrs !== "string" || typeof editsJson !== "string") {
    return null;
  }
  let edits;
  try {
    edits = JSON.parse(editsJson);
  } catch {
    return null;
  }
  if (!Array.isArray(edits) || edits.length > MaxEdits) {
    return null;
  }
  const { rows, trailing } = attributeBlockRows(attrs);
  if (edits.length === 0) {
    return "";
  }
  const first = rows.length > 0 ? rows[0].raw : "";
  const eol = first.endsWith("\r\n") ? "\r\n" : "\n";
  const lines = [];
  for (const edit of edits) {
    if (edit === null || typeof edit !== "object" || !Number.isInteger(edit.line) || edit.line < -1
      || edit.line >= rows.length || typeof edit.key !== "string" || typeof edit.value !== "string") {
      return null;
    }
    const row = edit.line >= 0 ? rows[edit.line] : null;
    if (row !== null && row.key === edit.key && row.value === edit.value) {
      lines.push(row.raw);
      continue;
    }
    const key = oneLine(edit.key);
    const value = netTrim(oneLine(edit.value));
    if (key.length === 0 || attributeKeyEnd(key + "::") !== key.length) {
      return null;
    }
    const text = value.length > 0 ? key + ":: " + value : key + "::";
    // A changed row keeps having no line ending when it had none (the block ended the file).
    lines.push(row !== null && !row.raw.endsWith("\n") ? text : text + eol);
  }
  let out = "";
  for (const line of lines) {
    if (out.length > 0 && !out.endsWith("\n")) {
      out += eol;
    }
    out += line;
  }
  if (trailing.length > 0 && !out.endsWith("\n")) {
    out += eol;
  }
  return out + trailing;
}

/** The text after opening and saving without an edit (with a name: the title line is kept as it is). */
export function roundTrip(markdown, name) {
  const { head, body } = titleSplit(markdown, name);
  const { attrs, body: rest } = attributeSplit(body);
  return joinTitle(head, attrs + serialize(parse(rest)));
}

/** True when opening and saving without an edit gives the same text back. */
export function isLossless(markdown, name) {
  return roundTrip(markdown, name) === markdown;
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

// The atoms that show their raw text in a heading: tags, note links and mentions.
const chipKinds = new Set(["tag", "noteLink", "mention"]);

/**
 * The headings of doc in document order, as [{ level, text }]: text is the heading's text nodes and each chip's raw
 * text (marks dropped, a line break as a space), trimmed. Empty headings are kept, so an index always names the same
 * heading the editor shows.
 */
export function headingList(doc) {
  const list = [];
  doc.descendants(node => {
    if (node.type.name !== "heading") {
      return true;
    }
    let text = "";
    node.descendants(child => {
      if (child.isText) {
        text += child.text;
      } else if (chipKinds.has(child.type.name)) {
        text += child.attrs.raw;
      } else if (child.type.name === "hard_break") {
        text += " ";
      }
      return true;
    });
    list.push({ level: node.attrs.level, text: text.trim() });
    return false;
  });
  return list;
}

/** The headings of the note body (the hidden title line left out), as JSON [{ level, text }]; needs no page. */
export function outline(markdown, name) {
  const { body } = titleSplit(markdown, name);
  return JSON.stringify(headingList(parse(attributeSplit(typeof body === "string" ? body : "").body)));
}
