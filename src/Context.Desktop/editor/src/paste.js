// Smart paste (job 244): tab-separated cells become a Markdown table in its own paragraph, and a lone web link goes in
// as plain text that .NET may later turn into [page title](url). Everything here is DOM-free, so the tests run it in
// Jint; view.js wires it to the page's paste event. Job 329: lines that are all task items (- { } / - [ ]) go in as
// task items of their kind.
import { Fragment, Slice } from "prosemirror-model";
import { EditorState, Selection, TextSelection } from "prosemirror-state";
import { closeHistory } from "prosemirror-history";
import { schema } from "./schema.js";
import { parse, serialize, sourceLink, webHref } from "./markdown.js";

const maxRows = 500;
const maxTabs = 49;
const maxUrl = 2048;

// The clipboard image types a paste stores as an attachment (job 435); .NET checks the bytes again.
const pastedImageTypes = ["image/png", "image/jpeg", "image/gif", "image/webp"];

/**
 * The pasted image files (job 435): those of files (an array or FileList) whose type is PNG, JPEG, GIF or WebP, but only
 * when the clipboard's text/plain is empty after trimming; null when there is text (an Excel or Word copy that holds a
 * picture too goes through smart paste), no file, or no file of an allowed type.
 */
export function pastedImages(files, text) {
  if (typeof text === "string" && text.trim().length > 0) {
    return null;
  }
  if (!files || typeof files.length !== "number" || files.length === 0) {
    return null;
  }
  const images = Array.from(files).filter(file => file && pastedImageTypes.includes(file.type));
  return images.length > 0 ? images : null;
}

/** Test hook, DOM-free (job 435): pastedImages over a JSON array of { name, type }, as JSON (the picked names or null). */
export function pastedImagesJson(filesJson, text) {
  const files = filesJson === null || filesJson === undefined ? null : JSON.parse(String(filesJson));
  const picked = pastedImages(files, text === null || text === undefined ? text : String(text));
  return JSON.stringify(picked === null ? null : picked.map(file => file.name));
}

// The meta a smartPaste transaction carries when it inserted a lone link: the URL.
export const linkMeta = "contextPastedLink";

/**
 * The rows of the Markdown table tab-separated text makes, joined by "\n", or null when it is no table: after CRLF/CR
 * become "\n" and one trailing "\n" is dropped, 2 to 500 lines, each with the same number of tabs (1 to 49), and no cell
 * holding "|". Cells are trimmed; the first line is the header.
 */
export function tableText(text) {
  if (typeof text !== "string") {
    return null;
  }
  let normal = text.replace(/\r\n?/g, "\n");
  if (normal.endsWith("\n")) {
    normal = normal.slice(0, -1);
  }
  const lines = normal.split("\n");
  if (lines.length < 2 || lines.length > maxRows) {
    return null;
  }
  const rows = lines.map(line => line.split("\t"));
  const tabs = rows[0].length - 1;
  if (tabs < 1 || tabs > maxTabs) {
    return null;
  }
  if (rows.some(cells => cells.length !== tabs + 1 || cells.some(cell => cell.includes("|")))) {
    return null;
  }
  const row = cells => "| " + cells.map(cell => cell.trim()).join(" | ") + " |";
  const out = [row(rows[0]), row(rows[0].map(() => "---"))];
  for (let i = 1; i < rows.length; i++) {
    out.push(row(rows[i]));
  }
  return out.join("\n");
}

/** The trimmed text when it is one absolute http/https URL with a host, no whitespace, at most 2048 chars; else null. */
export function loneLink(text) {
  if (typeof text !== "string") {
    return null;
  }
  const url = text.trim();
  if (url.length === 0 || url.length > maxUrl || /\s/.test(url)) {
    return null;
  }
  const match = /^https?:\/\/([^/?#]*)/i.exec(url);
  if (!match) {
    return null;
  }
  const authority = match[1];
  const host = authority.slice(authority.lastIndexOf("@") + 1);
  if (!/^(?:\[[0-9A-Fa-f:.]+\]|[^\s@:[\]]+)(?::\d{1,5})?$/.test(host)) {
    return null;
  }
  return url;
}

// True when the position is in a code block or inside inline code.
function inCode(state, pos) {
  const $pos = state.doc.resolve(pos);
  return $pos.parent.type.spec.code || $pos.marks().some(m => m.type.spec.code);
}

// A pasted task line (job 329): optional spaces or tabs, -, * or +, one space, a box ([ ], [x], [X], { } or {x}), one
// space, then text.
const taskLine = /^[ \t]*[-*+] (?:\[[ xX]\]|\{[ x]\}) \S/;
const maxTaskLines = 500;

/**
 * The Markdown of pasted text when every line is a task line (taskLine), after CRLF/CR become "\n" and trailing line
 * ends are dropped; 1 to 500 lines. Otherwise null.
 */
export function taskText(text) {
  const lines = String(text).replace(/\r\n?/g, "\n").replace(/\n+$/, "").split("\n");
  if (lines.length > maxTaskLines || !lines.every(line => taskLine.test(line))) {
    return null;
  }
  return lines.join("\n") + "\n";
}

/**
 * The transaction a paste of text makes, or null for the default paste: always null for a plain paste
 * (Ctrl/Cmd+Shift+V) and outside a paragraph or inside inline code. A table (tableText) replaces the selection and goes
 * in as its own paragraph between the text before and after the cursor; a lone link (loneLink) replaces the selection
 * as unmarked text and carries linkMeta; task lines (taskText) replace the selection as task items of their kind (an
 * empty paragraph is replaced whole).
 */
export function smartPaste(state, text, plain) {
  if (plain) {
    return null;
  }
  const { $from, from } = state.selection;
  if ($from.parent.type !== schema.nodes.paragraph || inCode(state, from)) {
    return null;
  }
  const table = tableText(text);
  if (table !== null) {
    return tableTransaction(state, table);
  }
  const url = loneLink(text);
  if (url !== null) {
    const tr = state.tr.replaceSelectionWith(schema.text(url), false);
    return tr.setMeta(linkMeta, url).scrollIntoView();
  }
  const tasks = taskText(text);
  if (tasks !== null) {
    return taskTransaction(state, tasks);
  }
  const linked = linkedSlice(text);
  if (linked !== null) {
    return state.tr.replaceSelection(linked).scrollIntoView();
  }
  return null;
}

// An inline [text](url) in pasted text (job 354): a label without brackets or a line break, then a destination
// without whitespace whose parentheses nest one level at most.
const inlineLink = /\[[^\[\]\n]+\]\((?:[^\s()]|\([^\s()]*\))+\)/g;

/**
 * The content of a link at raw ([text](url) in source form) in text, when the parser reads exactly raw as one link to
 * a webHref URL and the character before it is no "!" (an image) or "\\" (an escape); else null. The input rule and
 * the paste share it.
 */
export function webLinkContent(raw, before) {
  if (before === "!" || before === "\\") {
    return null;
  }
  const found = sourceLink(raw);
  return found !== null && webHref(found.link.attrs.href) ? found.content : null;
}

/**
 * The slice a normal paste of text inserts when it holds at least one inline [text](url) to a web link (job 354): one
 * paragraph per line, each such link a link mark saved as its source, every other character plain text. Else null.
 */
export function linkedSlice(text) {
  if (typeof text !== "string" || !text.includes("](")) {
    return null;
  }
  const lines = text.replace(/\r\n?/g, "\n").split("\n");
  let found = false;
  const paragraphs = lines.map(line => {
    const nodes = [];
    let last = 0;
    for (const match of line.matchAll(inlineLink)) {
      const content = webLinkContent(match[0], match.index > 0 ? line[match.index - 1] : "");
      if (content === null) {
        continue;
      }
      if (match.index > last) {
        nodes.push(schema.text(line.slice(last, match.index)));
      }
      content.forEach(node => nodes.push(node));
      last = match.index + match[0].length;
      found = true;
    }
    if (last < line.length) {
      nodes.push(schema.text(line.slice(last)));
    }
    return schema.nodes.paragraph.create(null, nodes);
  });
  return found ? new Slice(Fragment.from(paragraphs), 1, 1) : null;
}

function taskTransaction(state, markdown) {
  const content = parse(markdown).content;
  const { $from, empty } = state.selection;
  if (empty && $from.parent.content.size === 0) {
    const tr = state.tr.replaceWith($from.before(), $from.after(), content);
    return tr.setSelection(Selection.near(tr.doc.resolve($from.before() + content.size), -1)).scrollIntoView();
  }
  return state.tr.replaceSelection(new Slice(content, 0, 0)).scrollIntoView();
}

function tableTransaction(state, table) {
  const tr = state.tr;
  if (!state.selection.empty) {
    tr.deleteSelection();
  }
  const $pos = tr.doc.resolve(tr.selection.from);
  if ($pos.parent.type !== schema.nodes.paragraph) {
    return null;
  }
  const parent = $pos.parent;
  const offset = $pos.parentOffset;
  const blocks = [];
  if (offset > 0) {
    blocks.push(parent.cut(0, offset));
  }
  const tablePara = schema.nodes.paragraph.create(null, schema.text(table));
  blocks.push(tablePara);
  if (offset < parent.content.size) {
    blocks.push(parent.cut(offset));
  }
  const start = $pos.before();
  tr.replaceWith(start, $pos.after(), Fragment.from(blocks));
  const tableStart = start + (offset > 0 ? blocks[0].nodeSize : 0);
  tr.setSelection(TextSelection.create(tr.doc, tableStart + 1 + table.length));
  return tr.scrollIntoView();
}

// True when from..to holds exactly url as text without marks.
function holdsUrl(doc, from, to, url) {
  if (from < 0 || to > doc.content.size || from >= to || doc.textBetween(from, to, "\n", "￼") !== url) {
    return false;
  }
  let plainText = true;
  doc.nodesBetween(from, to, node => {
    if (node.isInline && (!node.isText || node.marks.length > 0)) {
      plainText = false;
    }
    return !node.isInline;
  });
  return plainText;
}

/**
 * The transaction that turns from..to (still exactly url as unmarked text) into title with a link mark to url, as its
 * own undo step; null when the range changed or the title is empty. The title is text only, never HTML.
 */
export function linkTitleTransaction(state, from, to, url, title) {
  if (typeof url !== "string" || typeof title !== "string" || title.trim().length === 0) {
    return null;
  }
  if (!holdsUrl(state.doc, from, to, url)) {
    return null;
  }
  const text = schema.text(title, [schema.marks.link.create({ href: url })]);
  const tr = state.tr.replaceWith(from, to, text);
  return savesAsLink(tr.doc.resolve(from).parent, url) ? closeHistory(tr) : null;
}

// The texts of the runs linked to url in a textblock, in order.
function linkRuns(block, url) {
  const runs = [];
  let open = false;
  block.forEach(node => {
    const linked = node.isText && node.marks.some(m => m.type === schema.marks.link && m.attrs.href === url);
    if (linked && open) {
      runs[runs.length - 1] += node.text;
    } else if (linked) {
      runs.push(node.text);
    }
    open = linked;
  });
  return runs;
}

// True when the textblock, saved and opened again, keeps its links to url as the same texts: a title whose text would
// turn into a tag, a note link or anything else that breaks the link is not applied.
function savesAsLink(block, url) {
  const wrapped = schema.topNodeType.create(null, schema.nodes.paragraph.create(null, block.content));
  const again = parse(serialize(wrapped)).firstChild;
  const before = linkRuns(block, url);
  const after = again ? linkRuns(again, url) : [];
  return before.length === after.length && before.every((run, i) => run === after[i]);
}

// The doc position at a text offset of doc (block boundaries count as one "\n"); the end for an offset past it.
function positionAt(doc, offset) {
  for (let pos = 0; pos <= doc.content.size; pos++) {
    if (doc.resolve(pos).parent.inlineContent && doc.textBetween(0, pos, "\n", "￼").length === offset) {
      return pos;
    }
  }
  return Selection.atEnd(doc).from;
}

function startState(start) {
  const markdown = typeof start === "string" ? start : "";
  const parsed = parse(markdown);
  const doc = schema.topNodeType.create({ trailingNewline: markdown.endsWith("\n") }, parsed.content);
  return EditorState.create({ doc });
}

/**
 * Test hook, DOM-free: the result of pasting text into the Markdown start with the cursor at the text offset cursor
 * (the end when left out) and, given end, the selection up to that offset: JSON { markdown, link }, markdown being the
 * serialized document after smartPaste's transaction (null for the default paste) and link the URL the editor would
 * pass to OnPasteLink (or null).
 */
export function pastedMarkdown(text, plain, start, cursor, end) {
  let state = startState(start);
  const anchor = typeof cursor === "number" ? positionAt(state.doc, cursor) : Selection.atEnd(state.doc).from;
  const head = typeof end === "number" ? positionAt(state.doc, end) : anchor;
  state = state.apply(state.tr.setSelection(TextSelection.create(state.doc, anchor, head)));
  const tr = smartPaste(state, text, !!plain);
  if (tr === null) {
    return JSON.stringify({ markdown: null, link: null });
  }
  const link = tr.getMeta(linkMeta);
  return JSON.stringify({ markdown: serialize(state.apply(tr).doc), link: typeof link === "string" ? link : null });
}

/**
 * Test hook, DOM-free (job 354): the document after pasting text at the end of start, as JSON in docJson's shape; the
 * default paste (smartPaste gives null) inserts text as unmarked text, one paragraph per line, as ProseMirror does.
 */
export function pastedDocJson(text, plain, start) {
  let state = startState(start);
  state = state.apply(state.tr.setSelection(Selection.atEnd(state.doc)));
  let tr = smartPaste(state, String(text), !!plain);
  if (tr === null) {
    const paragraphs = String(text).replace(/\r\n?/g, "\n").split("\n")
      .map(line => schema.nodes.paragraph.create(null, line.length > 0 ? schema.text(line) : null));
    tr = state.tr.replaceSelection(new Slice(Fragment.from(paragraphs), 1, 1));
  }
  return JSON.stringify(state.apply(tr).doc.toJSON());
}

/**
 * Test hook, DOM-free: the Markdown of a paragraph holding only url once setLinkTitle gave it title, after edit (when
 * given) changed the text while the title was on its way: "before" / "after" types text next to the URL, "inside" types
 * into it, "bold" marks it, "delete" removes it. The pending range is mapped the way the page maps it.
 */
export function titledMarkdown(url, title, edit) {
  const text = String(url);
  const doc = schema.topNodeType.create({ trailingNewline: false }, schema.nodes.paragraph.create(null, schema.text(text)));
  let state = EditorState.create({ doc });
  let from = 1;
  let to = 1 + text.length;
  const edits = {
    before: tr => tr.insertText("x ", from),
    after: tr => tr.insertText(" y", to),
    inside: tr => tr.insertText("x", from + 1),
    bold: tr => tr.addMark(from, to, schema.marks.strong.create()),
    delete: tr => tr.delete(from, to),
  };
  if (edits[edit]) {
    const tr = edits[edit](state.tr);
    from = tr.mapping.map(from, 1);
    to = tr.mapping.map(to, -1);
    state = state.apply(tr);
  }
  const tr = linkTitleTransaction(state, from, to, url, title);
  return serialize((tr === null ? state : state.apply(tr)).doc);
}
