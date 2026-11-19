// Smart paste (job 244): tab-separated cells become a Markdown table in its own paragraph, and a lone web link goes in
// as plain text that .NET may later turn into [page title](url). Everything here is DOM-free, so the tests run it in
// Jint; view.js wires it to the page's paste event.
import { Fragment } from "prosemirror-model";
import { EditorState, Selection, TextSelection } from "prosemirror-state";
import { closeHistory } from "prosemirror-history";
import { schema } from "./schema.js";
import { parse, serialize } from "./markdown.js";

const maxRows = 500;
const maxTabs = 49;
const maxUrl = 2048;

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

/**
 * The transaction a paste of text makes, or null for the default paste: always null for a plain paste
 * (Ctrl/Cmd+Shift+V) and outside a paragraph or inside inline code. A table (tableText) replaces the selection and goes
 * in as its own paragraph between the text before and after the cursor; a lone link (loneLink) replaces the selection
 * as unmarked text and carries linkMeta.
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
  return null;
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
