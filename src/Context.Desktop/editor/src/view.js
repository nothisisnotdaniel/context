// The editor in the page: one ProseMirror view per host element. DOM is built only through ProseMirror's
// renderer and document.createElement; tag, link, mention and attachment PDF clicks (and Enter on a focused chip) and
// dropped files are reported to .NET, which decides what to open or store. A pasted lone link is reported too (job 244):
// .NET may answer with its page title through setLinkTitle.
import { EditorState, Selection, TextSelection, Plugin, PluginKey } from "prosemirror-state";
import { EditorView, Decoration, DecorationSet } from "prosemirror-view";
import { Fragment } from "prosemirror-model";
import { keymap } from "prosemirror-keymap";
import { history, undo, redo, closeHistory } from "prosemirror-history";
import { baseKeymap, toggleMark, setBlockType, chainCommands, newlineInCode } from "prosemirror-commands";
import { splitListItem, liftListItem, sinkListItem } from "prosemirror-schema-list";
import { inputRules, wrappingInputRule, textblockTypeInputRule, InputRule } from "prosemirror-inputrules";
import { schema } from "./schema.js";
import { smartPaste, linkMeta, linkTitleTransaction } from "./paste.js";
import { codeBlockView, mermaidEditing, isMermaidFence } from "./mermaid.js";
import { tokens, firstWord, languageOptions, withLanguage } from "./highlight.js";
import { parse, serialize, splitTitle, joinTitle, headingList, attributeSplit, attributeBlockRowsJson, editAttributes, normalizePerson, isNetWhite } from "./markdown.js";

const editors = new WeakMap();

// The limits .NET checks again: NoteAttachments.MaxBytes and MaxFilesPerDrop.
const maxAttachmentBytes = 10 * 1024 * 1024;
const maxFilesPerDrop = 10;

// The pasted links an editor still waits on a title for; the oldest is dropped past this.
const maxPendingLinks = 20;

// An attachment PDF link: the only href a click opens (through .NET, in the system viewer).
const attachmentPdf = /^\.attachments\/[^/\\]+\.pdf$/;

// A blob: URL for base64 bytes .NET checked (magic bytes and extension); never a URL from the note text.
function blobUrl(mime, base64) {
  const text = atob(base64);
  const bytes = new Uint8Array(text.length);
  for (let i = 0; i < text.length; i++) {
    bytes[i] = text.charCodeAt(i);
  }
  return URL.createObjectURL(new Blob([bytes], { type: mime }));
}

// An image whose src .NET gave bytes for is an <img> with a blob: URL; any other stays the grey placeholder.
function imageView(editor) {
  return node => {
    const alt = node.attrs.alt || "";
    const url = editor.images.get(node.attrs.src);
    if (url) {
      const img = document.createElement("img");
      img.src = url;
      img.alt = alt;
      img.title = alt;
      return { dom: img };
    }
    const span = document.createElement("span");
    span.className = "image-placeholder grey";
    span.title = node.attrs.src;
    span.textContent = alt ? "[image: " + alt + "]" : "[image]";
    return { dom: span };
  };
}

// Reads the dropped files (each at most maxAttachmentBytes; a bigger one is not read) and makes one .NET call.
async function sendDrop(dotnet, files) {
  const count = files.length;
  const sent = [];
  if (count <= maxFilesPerDrop) {
    for (const file of files) {
      let bytes = null;
      if (file.size <= maxAttachmentBytes) {
        try {
          bytes = new Uint8Array(await file.arrayBuffer());
        } catch {
          bytes = null;
        }
      }
      sent.push({ name: file.name, size: file.size, bytes });
    }
  }
  await dotnet.invokeMethodAsync("OnDrop", count, sent).catch(() => {});
}

// The href of an attachment PDF link at pos, or null.
function attachmentAt(view, pos) {
  const node = view.state.doc.nodeAt(pos);
  const marks = (node ? node.marks : []).concat(view.state.doc.resolve(pos).marks());
  for (const mark of marks) {
    if (mark.type === schema.marks.link && attachmentPdf.test(mark.attrs.href || "")) {
      return mark.attrs.href;
    }
  }
  return null;
}

// Keys the page handles (Ctrl/Cmd+S saves, Ctrl/Cmd+K opens the palette): the editor does nothing with them and
// the keydown bubbles on to the page.
const swallow = () => true;

function splitItem(state, dispatch) {
  const { $from } = state.selection;
  for (let depth = $from.depth; depth > 0; depth--) {
    const node = $from.node(depth);
    if (node.type === schema.nodes.list_item) {
      const attrs = node.attrs.checked === null ? null : { checked: false };
      return splitListItem(schema.nodes.list_item, attrs)(state, dispatch);
    }
  }
  return false;
}

function inCode(state, start) {
  const $start = state.doc.resolve(start);
  return $start.parent.type.spec.code || $start.marks().some(m => m.type.spec.code);
}

// True when pos starts its textblock, follows a hard break (a line break in the markdown) or follows whitespace.
function afterWhite(state, pos) {
  const $pos = state.doc.resolve(pos);
  if ($pos.parentOffset === 0) {
    return true;
  }
  const before = $pos.nodeBefore;
  if (before === null) {
    return false;
  }
  return before.type === schema.nodes.hard_break || (before.isText && isNetWhite(before.text[before.text.length - 1]));
}

// True when from..end holds only text without marks and the typed character gets none: what a mention chip replaces
// must serialize as exactly the typed characters.
function plainText(state, from, end) {
  const typed = state.storedMarks || state.doc.resolve(end).marks();
  if (typed.length > 0) {
    return false;
  }
  let plain = true;
  state.doc.nodesBetween(from, end, node => {
    if (node.isInline && (!node.isText || node.marks.length > 0)) {
      plain = false;
    }
    return true;
  });
  return plain;
}

function rules() {
  const tagWord = new RegExp("(^|\\s)(#\\p{L}[\\p{L}\\p{Nd}_-]*)([\\s.,;:!?)])$", "u");
  const tagRule = new InputRule(tagWord, (state, match, start, end) => {
    const from = start + match[1].length;
    const raw = match[2];
    if (inCode(state, from) || raw.length - 1 > 50) {
      return null;
    }
    const node = schema.nodes.tag.create({ raw, tag: raw.slice(1).toLowerCase() });
    return state.tr.replaceWith(from, end, [node, schema.text(match[3])]);
  });
  const linkRule = new InputRule(/\[\[([^\[\]\n]+)\]\]$/, (state, match, start, end) => {
    const target = match[1].trim();
    if (inCode(state, start) || target.length === 0 || target.length > 103 || state.doc.textBetween(start - 1, start) === "#") {
      return null;
    }
    return state.tr.replaceWith(start, end, schema.nodes.noteLink.create({ raw: match[0], target }));
  });
  // @[Person name] as markdown.js and PersonParser read it: '@' at the start of the textblock or after whitespace,
  // '[', text up to the first ']' on the line, a name normalizePerson accepts; only plain text, never in code.
  const mentionRule = new InputRule(/(^|[^])@\[([^\]\n\ufffc]*)\]$/, (state, match, start, end) => {
    const from = start + match[1].length;
    const before = match[1];
    if (before === "" || before === "\ufffc" ? !afterWhite(state, from) : !isNetWhite(before)) {
      return null;
    }
    const name = normalizePerson(match[2]);
    if (name === null || inCode(state, from) || !plainText(state, from, end)) {
      return null;
    }
    return state.tr.replaceWith(from, end, schema.nodes.mention.create({ raw: match[0].slice(match[1].length), name }));
  });
  // '(' straight after a mention chip: the parser reads "@[Name](" as text, so the chip turns back into its raw text.
  const mentionParenRule = new InputRule(/\ufffc\($/, (state, match, start, end) => {
    const node = state.doc.nodeAt(start);
    if (!node || node.type !== schema.nodes.mention || start + node.nodeSize !== end) {
      return null;
    }
    return state.tr.replaceWith(start, end, schema.text(node.attrs.raw + "("));
  });
  // The block rules never fire in a code block (inputrules skips rules without inCode there) nor in inline code: the
  // builders take no options, so inCodeMark is set on each returned rule.
  const blockRules = [
    wrappingInputRule(/^\s*>\s$/, schema.nodes.blockquote),
    wrappingInputRule(/^\s*-\s$/, schema.nodes.bullet_list),
    wrappingInputRule(/^(\d+)\.\s$/, schema.nodes.ordered_list, m => ({ order: +m[1] })),
    // "# " gives level 2 too: the page title is the only level-1 heading.
    textblockTypeInputRule(/^(#{1,6})\s$/, schema.nodes.heading, m => ({ level: Math.max(m[1].length, 2) })),
    // ``` and an optional language (no whitespace, no backtick) at the start of the line, then Space (job 290).
    textblockTypeInputRule(/^```([^\s`]*) $/, schema.nodes.code_block, m => ({ params: m[1] })),
  ];
  for (const rule of blockRules) {
    rule.inCodeMark = false;
  }
  return inputRules({
    rules: [
      ...blockRules,
      inlineCodeRule(),
      tagRule,
      linkRule,
      mentionRule,
      mentionParenRule,
    ],
  });
}

// `text` becomes inline code when the closing backtick is typed (job 290): a span of at least one character with no
// backtick, chip or line break, whose opening backtick follows neither a backtick nor a backslash. Never in a code
// block, never over text that already has the code mark (inCodeMark false checks the whole span). The next typed
// character gets no code mark.
function inlineCodeRule() {
  const rule = new InputRule(/(^|[^`\\])`([^`\ufffc]+)`$/, (state, match, start, end) => {
    const from = start + match[1].length;
    let plain = true;
    state.doc.nodesBetween(from, end, node => {
      if (node.isInline && !node.isText) {
        plain = false;
      }
      return true;
    });
    if (!plain) {
      return null;
    }
    const marks = state.doc.resolve(from + 1).marks();
    const code = schema.marks.code.create();
    const tr = state.tr.replaceWith(from, end, schema.text(match[2], code.addToSet(marks)));
    return tr.setStoredMarks(code.removeFromSet(marks));
  });
  rule.inCodeMark = false;
  return rule;
}

// A paragraph whose whole text is ``` or ```lang (lang: no whitespace, no backtick).
const fenceLine = /^```([^\s`]*)$/;

// Enter on a fence line (job 290): the paragraph becomes an empty code block with that language, caret inside, and
// nothing is split off. Not for text with the code mark or a paragraph that holds anything but one text node.
function enterFence(state, dispatch) {
  const { $from, $to } = state.selection;
  const para = $from.parent;
  if (para.type !== schema.nodes.paragraph || !$from.sameParent($to) || para.childCount !== 1) {
    return false;
  }
  const only = para.firstChild;
  const match = only.isText && only.marks.every(m => !m.type.spec.code) ? fenceLine.exec(only.text) : null;
  if (!match) {
    return false;
  }
  if (dispatch) {
    const start = $from.before();
    const tr = state.tr.replaceWith(start, start + para.nodeSize, schema.nodes.code_block.create({ params: match[1] }));
    tr.setSelection(TextSelection.create(tr.doc, start + 1));
    dispatch(tr.scrollIntoView());
  }
  return true;
}

// ---- Code (job 283): inline code and code blocks, their commands, syntax colours and the code-block header. ----

/** Toggles the code mark on the selection, or (empty selection) the stored mark, so the next typed text is code or not. */
export const toggleInlineCode = toggleMark(schema.marks.code);

// The text a leaf node stands for when its block becomes code: a chip's raw text, a line break, an image's markdown.
function leafText(leaf) {
  if (leaf.type === schema.nodes.hard_break) {
    return "\n";
  }
  if (typeof leaf.attrs.raw === "string") {
    return leaf.attrs.raw;
  }
  if (leaf.type === schema.nodes.image) {
    return "![" + (leaf.attrs.alt || "") + "](" + leaf.attrs.src + ")";
  }
  return "";
}

function blockText(node, to) {
  return node.textBetween(0, to === undefined ? node.content.size : to, "", leafText);
}

// A code block back to paragraphs: one per line, the selection kept on the same characters.
function codeToParagraphs(state, dispatch) {
  const { $from, $to } = state.selection;
  if (!$from.sameParent($to)) {
    return false;
  }
  const block = $from.parent;
  const start = $from.before();
  const text = block.textContent;
  const paragraphs = text.split("\n").map(line => schema.nodes.paragraph.create(null, line.length > 0 ? schema.text(line) : null));
  const $start = state.doc.resolve(start);
  if (!$start.parent.canReplace($start.index(), $start.index() + 1, Fragment.from(paragraphs))) {
    return false;
  }
  if (dispatch) {
    const at = offset => {
      const lines = text.slice(0, offset).split("\n");
      let pos = start;
      for (let i = 0; i < lines.length - 1; i++) {
        pos += paragraphs[i].nodeSize;
      }
      return pos + 1 + lines[lines.length - 1].length;
    };
    const tr = state.tr.replaceWith(start, start + block.nodeSize, paragraphs);
    tr.setSelection(TextSelection.create(tr.doc, at($from.parentOffset), at($to.parentOffset)));
    dispatch(tr.scrollIntoView());
  }
  return true;
}

/**
 * In a code block, makes each of its lines a paragraph again. Elsewhere, makes the paragraphs and headings the
 * selection touches (siblings only) one code block without a language, one line per block.
 */
export function toggleCodeBlock(state, dispatch) {
  const { $from, $to } = state.selection;
  if ($from.parent.type === schema.nodes.code_block) {
    return codeToParagraphs(state, dispatch);
  }
  const blocks = [];
  state.doc.nodesBetween($from.pos, $to.pos, (node, pos) => {
    if (node.isTextblock) {
      blocks.push({ node, pos });
      return false;
    }
    return true;
  });
  if (blocks.length === 0 || blocks.some(b => b.node.type !== schema.nodes.paragraph && b.node.type !== schema.nodes.heading)) {
    return false;
  }
  const $first = state.doc.resolve(blocks[0].pos);
  const parentStart = $first.start();
  if (blocks.some(b => state.doc.resolve(b.pos).start() !== parentStart)) {
    return false;
  }
  const texts = blocks.map(b => blockText(b.node));
  const text = texts.join("\n");
  const node = schema.nodes.code_block.create(null, text.length > 0 ? schema.text(text) : null);
  if (!$first.parent.canReplace($first.index(), $first.index() + blocks.length, Fragment.from(node))) {
    return false;
  }
  if (dispatch) {
    const last = blocks[blocks.length - 1];
    const from = blocks[0].pos;
    const offsetOf = pos => {
      let prefix = 0;
      for (let i = 0; i < blocks.length; i++) {
        const start = blocks[i].pos + 1;
        const end = blocks[i].pos + blocks[i].node.nodeSize - 1;
        if (pos <= end) {
          return prefix + (pos <= start ? 0 : blockText(blocks[i].node, pos - start).length);
        }
        prefix += texts[i].length + 1;
      }
      return text.length;
    };
    const tr = state.tr.replaceWith(from, last.pos + last.node.nodeSize, node);
    tr.setSelection(TextSelection.create(tr.doc, from + 1 + offsetOf($from.pos), from + 1 + offsetOf($to.pos)));
    dispatch(tr.scrollIntoView());
  }
  return true;
}

// Every code block of doc in document order, with its position.
function codeBlocks(doc) {
  const found = [];
  doc.descendants((node, pos) => {
    if (node.type === schema.nodes.code_block) {
      found.push({ node, pos });
      return false;
    }
    return !node.isTextblock;
  });
  return found;
}

/**
 * The indexes (document order, in newDoc) of the code blocks whose text or params differ from the block at the same
 * index of oldDoc, or that oldDoc has no block for. DOM-free.
 */
export function changedCodeBlocks(oldDoc, newDoc) {
  const before = codeBlocks(oldDoc);
  const changed = [];
  codeBlocks(newDoc).forEach(({ node }, index) => {
    const old = before[index];
    if (!old || (old.node !== node && (old.node.attrs.params !== node.attrs.params || old.node.textContent !== node.textContent))) {
      changed.push(index);
    }
  });
  return changed;
}

// The class decorations of one code block: none for Mermaid or an unknown language.
function blockDecorations(node, pos) {
  if (isMermaidFence(node.attrs.params)) {
    return [];
  }
  return tokens(node.textContent, firstWord(node.attrs.params))
    .map(([from, to, className]) => Decoration.inline(pos + 1 + from, pos + 1 + to, { class: className }));
}

const highlightKey = new PluginKey("codeHighlight");

// Syntax colours as inline class decorations; only the blocks a transaction changed are highlighted again. The
// document (and so the saved markdown) is never touched.
function codeHighlight() {
  return new Plugin({
    key: highlightKey,
    state: {
      init(config, state) {
        const decorations = [];
        for (const { node, pos } of codeBlocks(state.doc)) {
          decorations.push(...blockDecorations(node, pos));
        }
        return DecorationSet.create(state.doc, decorations);
      },
      apply(tr, set, oldState, newState) {
        if (!tr.docChanged) {
          return set;
        }
        let next = set.map(tr.mapping, tr.doc);
        const changed = changedCodeBlocks(oldState.doc, newState.doc);
        if (changed.length === 0) {
          return next;
        }
        const blocks = codeBlocks(newState.doc);
        for (const index of changed) {
          const { node, pos } = blocks[index];
          next = next.remove(next.find(pos + 1, pos + node.nodeSize - 1));
          next = next.add(newState.doc, blockDecorations(node, pos));
        }
        return next;
      },
    },
    props: {
      decorations(state) {
        return highlightKey.getState(state);
      },
    },
  });
}

/**
 * Copies text: writeText(text) first; when it is missing, throws or rejects, execCopy(text). Resolves to "Copied", or
 * to "Can't copy" when both fail (a false result or a throw); never rejects. DOM-free.
 */
export function copyText(text, options) {
  const { writeText, execCopy } = options || {};
  const fallback = () => {
    try {
      return typeof execCopy === "function" && execCopy(text) ? "Copied" : "Can't copy";
    } catch {
      return "Can't copy";
    }
  };
  if (typeof writeText !== "function") {
    return Promise.resolve(fallback());
  }
  let written;
  try {
    written = Promise.resolve(writeText(text));
  } catch {
    return Promise.resolve(fallback());
  }
  return written.then(() => "Copied", () => fallback());
}

// The page's clipboard: navigator.clipboard.writeText when there is one.
function clipboardWrite() {
  const clipboard = globalThis.navigator && globalThis.navigator.clipboard;
  return clipboard && typeof clipboard.writeText === "function" ? text => clipboard.writeText(text) : undefined;
}

// The fallback copy: the text (a value, never markup) in a hidden textarea outside the editor, selected and copied.
function copyThroughSelection(text) {
  const area = document.createElement("textarea");
  area.value = text;
  area.readOnly = true;
  area.className = "code-copy-buffer";
  area.style.position = "fixed";
  area.style.left = "-10000px";
  area.style.opacity = "0";
  const active = document.activeElement;
  document.body.appendChild(area);
  try {
    area.select();
    return document.execCommand("copy");
  } finally {
    area.remove();
    if (active && typeof active.focus === "function") {
      active.focus();
    }
  }
}

// A code block that is not Mermaid: a non-editable header (language picker, Copy) above pre > code.
function plainCodeView(node, view, getPos) {
  const dom = document.createElement("div");
  dom.className = "code-block";
  const header = document.createElement("div");
  header.className = "code-header";
  header.contentEditable = "false";
  const select = document.createElement("select");
  select.className = "code-language";
  select.setAttribute("aria-label", "Language");
  const copy = document.createElement("button");
  copy.type = "button";
  copy.className = "code-copy";
  copy.textContent = "Copy";
  header.appendChild(select);
  header.appendChild(copy);
  const pre = document.createElement("pre");
  const code = document.createElement("code");
  pre.appendChild(code);
  dom.appendChild(header);
  dom.appendChild(pre);

  let shown = null;
  const fill = () => {
    if (shown === node.attrs.params) {
      return;
    }
    shown = node.attrs.params;
    const options = languageOptions(shown);
    select.replaceChildren(...options.map(o => {
      const option = document.createElement("option");
      option.value = o.value;
      option.textContent = o.label;
      return option;
    }));
    select.value = (options.find(o => o.selected) || options[0]).value;
  };
  fill();

  // One transaction, its own undo step; the dirty flag follows from the changed document.
  select.addEventListener("change", () => {
    const pos = getPos();
    const params = withLanguage(node.attrs.params, select.value);
    if (typeof pos !== "number" || params === node.attrs.params) {
      return;
    }
    view.dispatch(closeHistory(view.state.tr.setNodeMarkup(pos, null, { ...node.attrs, params })));
  });
  // Copy changes only the button's text, never the document.
  copy.addEventListener("click", event => {
    event.preventDefault();
    copyText(node.textContent, { writeText: clipboardWrite(), execCopy: copyThroughSelection }).then(label => {
      copy.textContent = label;
    });
  });
  copy.addEventListener("mouseleave", () => {
    copy.textContent = "Copy";
  });

  return {
    dom,
    contentDOM: code,
    update(next) {
      if (next.type !== node.type || isMermaidFence(next.attrs.params)) {
        return false;
      }
      node = next;
      fill();
      return true;
    },
    stopEvent(event) { return header.contains(event.target); },
    ignoreMutation(mutation) {
      if (header.contains(mutation.target)) {
        return true;
      }
      return mutation.type !== "selection" && !code.contains(mutation.target) && mutation.target !== pre;
    },
  };
}

// Mermaid fences keep their diagram view; every other code block gets the header.
function codeView(node, view, getPos) {
  return codeBlockView(node, view, getPos) || plainCodeView(node, view, getPos);
}

// A line break: "\n" in a code block, a hard_break node anywhere else.
const lineBreak = chainCommands(newlineInCode, (state, dispatch) => {
  if (dispatch) {
    dispatch(state.tr.replaceSelectionWith(schema.nodes.hard_break.create()).scrollIntoView());
  }
  return true;
});

// The editor's own key bindings, in front of baseKeymap. Enter in a code block is a newline first, so a code block in
// a list item never splits the item; Mod-Enter (baseKeymap's exitCode) still leaves the block.
function editorKeys() {
  return {
    "Mod-z": undo,
    "Shift-Mod-z": redo,
    "Mod-y": redo,
    "Mod-b": toggleMark(schema.marks.strong),
    "Mod-i": toggleMark(schema.marks.em),
    "Mod-e": toggleInlineCode,
    "Mod-`": toggleInlineCode,
    "Mod-Alt-c": toggleCodeBlock,
    "Enter": chainCommands(newlineInCode, enterFence, splitItem),
    "Tab": sinkListItem(schema.nodes.list_item),
    "Shift-Tab": liftListItem(schema.nodes.list_item),
    "Shift-Ctrl-0": setBlockType(schema.nodes.paragraph),
    "Mod-s": swallow,
    "Mod-k": swallow,
    "Ctrl-k": swallow,
    "Shift-Enter": lineBreak,
  };
}

// A task item: a clickable box (not an input, so the browser never toggles it on its own) and the content.
function listItemView(node, view, getPos) {
  const dom = document.createElement("li");
  const content = document.createElement("div");
  let box = null;
  const paint = () => {
    dom.className = node.attrs.checked === null ? "" : node.attrs.checked ? "task done" : "task";
    if (box) {
      box.setAttribute("aria-checked", node.attrs.checked ? "true" : "false");
    }
  };
  if (node.attrs.checked !== null) {
    box = document.createElement("span");
    box.className = "task-box";
    box.setAttribute("role", "checkbox");
    box.contentEditable = "false";
    box.addEventListener("mousedown", event => {
      event.preventDefault();
      const pos = getPos();
      if (typeof pos === "number") {
        view.dispatch(view.state.tr.setNodeMarkup(pos, null, { checked: !node.attrs.checked }));
      }
    });
    dom.appendChild(box);
    content.className = "task-body";
  }
  dom.appendChild(content);
  paint();
  return {
    dom,
    contentDOM: content,
    update(next) {
      if (next.type !== node.type || (next.attrs.checked === null) !== (node.attrs.checked === null)) {
        return false;
      }
      node = next;
      paint();
      return true;
    },
    stopEvent(event) { return box !== null && event.target === box; },
    ignoreMutation(mutation) { return box !== null && mutation.target === box; },
  };
}

// True when the document or the attribute block changed since the editor was opened or last marked clean.
function editorDirty(editor) {
  return editor.attrs !== editor.cleanAttrs || !editor.view.state.doc.eq(editor.clean);
}

// The editor's whole text: the hidden head, the attribute block as kept, then the document.
function editorMarkdown(editor) {
  return joinTitle(editor.head, editor.attrs + serialize(editor.view.state.doc));
}

// Tells .NET when the editor's dirty state flipped (never per keystroke); it only shows a marker there.
function reportDirty(editor) {
  const dirty = editorDirty(editor);
  if (dirty !== editor.dirty) {
    editor.dirty = dirty;
    editor.dotnet.invokeMethodAsync("OnDirty", dirty).catch(() => {});
  }
}

/** True for the one key that opens a focused chip: Enter without Ctrl, Meta, Alt or Shift. */
export function opensChip(event) {
  return event.key === "Enter" && !event.ctrlKey && !event.metaKey && !event.altKey && !event.shiftKey;
}

// Tells .NET to open a tag, note link or mention chip; true when node is one. Clicks and Enter share it.
function openChip(node, dotnet) {
  if (node.type === schema.nodes.tag) {
    dotnet.invokeMethodAsync("OnTag", node.attrs.raw).catch(() => {});
    return true;
  }
  if (node.type === schema.nodes.noteLink) {
    dotnet.invokeMethodAsync("OnLink", node.attrs.raw).catch(() => {});
    return true;
  }
  if (node.type === schema.nodes.mention) {
    dotnet.invokeMethodAsync("OnPerson", node.attrs.raw).catch(() => {});
    return true;
  }
  return false;
}

// The tag, noteLink or mention node a focused chip element stands for, or null.
function chipNode(view, target) {
  if (!target || !target.classList || !target.classList.contains("chip")) {
    return null;
  }
  try {
    const node = view.state.doc.nodeAt(view.posAtDOM(target, 0));
    return node && (node.type === schema.nodes.tag || node.type === schema.nodes.noteLink || node.type === schema.nodes.mention)
      ? node
      : null;
  } catch {
    return null;
  }
}

/**
 * The doc an empty paragraph becomes when text is typed into it through the editor's input rules, as JSON in the
 * shape docJson gives. DOM-free: the rules run on an EditorState with no view.
 */
export function typedBlock(text) {
  return JSON.stringify(typedDoc(text).toJSON());
}

/** The markdown serialize writes for the doc typedBlock(text) builds. DOM-free. */
export function typedMarkdown(text) {
  return serialize(typedDoc(text));
}

function typedDoc(text) {
  const plugin = rules();
  const state = EditorState.create({ doc: schema.topNodeType.create(null, schema.nodes.paragraph.create()), plugins: [plugin] });
  return typeText(state, plugin, text).doc;
}

// Types text one character at a time through the input rules of plugin, as the view does; returns the new state.
function typeText(start, plugin, text) {
  let state = start;
  const typing = {
    get state() { return state; },
    composing: false,
    dispatch(tr) { state = state.apply(tr); },
  };
  for (const ch of Array.from(String(text))) {
    const { from, to } = state.selection;
    if (!plugin.props.handleTextInput(typing, from, to, ch)) {
      typing.dispatch(state.tr.insertText(ch, from, to));
    }
  }
  return state;
}

const SelectionOpen = "\u27e6";
const SelectionClose = "\u27e7";

// The text position of the first marker in doc, or -1.
function markerPos(doc, marker) {
  let found = -1;
  doc.descendants((node, pos) => {
    if (found !== -1) {
      return false;
    }
    if (node.isText) {
      const at = node.text.indexOf(marker);
      if (at !== -1) {
        found = pos + at;
      }
    }
    return true;
  });
  return found;
}

// The state the test hooks start from: markdown parsed, the ⟦ ⟧ markers taken out and made the selection (a lone
// ⟦ is a caret), with the page editor's input rules, highlighting, history and keymaps.
function markedState(markdown) {
  const plugin = rules();
  let doc = parse(String(markdown));
  const close = markerPos(doc, SelectionClose);
  if (close !== -1) {
    doc = EditorState.create({ doc }).tr.delete(close, close + 1).doc;
  }
  const open = markerPos(doc, SelectionOpen);
  if (open === -1) {
    throw new Error("No \u27e6 marker in the markdown.");
  }
  doc = EditorState.create({ doc }).tr.delete(open, open + 1).doc;
  const head = close === -1 ? open : close - 1;
  const state = EditorState.create({
    doc,
    selection: TextSelection.create(doc, open, head),
    plugins: [plugin, codeHighlight(), history(), keymap(editorKeys()), keymap(baseKeymap)],
  });
  return { state, plugin };
}

/**
 * Test hook: the markdown after running the editor's binding for key (editorKeys, then baseKeymap) on markdown with the
 * selection marked by ⟦ ⟧ (or a caret by a lone ⟦), then typing typed through the input rules. before, when given, is
 * typed through the input rules at the caret before the key runs. DOM-free.
 */
export function commandMarkdown(markdown, key, typed, before) {
  const marked = markedState(markdown);
  let state = before === undefined ? marked.state : typeText(marked.state, marked.plugin, before);
  const commands = [editorKeys()[key], baseKeymap[key]].filter(Boolean);
  if (commands.length === 0) {
    throw new Error("No binding for " + key + ".");
  }
  // As the page's keymaps: baseKeymap runs only when the editor's own binding does not handle the key.
  chainCommands(...commands)(state, tr => { state = state.apply(tr); });
  state = typeText(state, marked.plugin, typed === undefined ? "" : typed);
  return serialize(state.doc);
}

/** Test hook: the markdown after typing text at the ⟦ caret (or over the ⟦ ⟧ selection) of markdown. DOM-free. */
export function typedAtMarkdown(markdown, text) {
  const marked = markedState(markdown);
  return serialize(typeText(marked.state, marked.plugin, text).doc);
}

/** Test hook: changedCodeBlocks before and after typing text at the ⟦ caret of markdown, as JSON. DOM-free. */
export function changedCodeBlocksJson(markdown, text) {
  const marked = markedState(markdown);
  const after = typeText(marked.state, marked.plugin, text);
  return JSON.stringify(changedCodeBlocks(marked.state.doc, after.doc));
}

/**
 * Opens markdown in a new editor inside element; tag, link, mention and attachment clicks, drops and dirty flips call
 * back into dotnet. images ([{ src, mime, base64 }], checked by .NET) are shown inline through blob: URLs. With a
 * name, a first line "# <name>" (and the blank lines after it) is not shown: it stays on the editor as head and
 * getMarkdown writes it back unchanged. The attribute block right after it (attributeSplit) is not shown either: it
 * stays on the editor as attrs, changed only through setAttributes, and getMarkdown writes it back before the body.
 */
export function mount(element, markdown, dotnet, images, name) {
  destroy(element);
  const { head, body: afterHead } = JSON.parse(splitTitle(markdown, name));
  const { attrs, body } = attributeSplit(afterHead);
  const doc = parse(body);
  const editor = { view: null, head, attrs, cleanAttrs: attrs, clean: doc, dirty: false, dotnet, images: new Map(), dropPos: null, links: [], shift: false, lastKey: 0 };
  for (const image of images || []) {
    if (image && typeof image.src === "string" && !editor.images.has(image.src)) {
      editor.images.set(image.src, blobUrl(image.mime, image.base64));
    }
  }
  const state = EditorState.create({
    doc,
    plugins: [
      rules(),
      mermaidEditing(),
      history(),
      codeHighlight(),
      keymap(editorKeys()),
      keymap(baseKeymap),
    ],
  });
  const view = new EditorView(element, {
    state,
    nodeViews: { list_item: listItemView, image: imageView(editor), code_block: codeView },
    attributes: { class: "note-editor-body", spellcheck: "true" },
    dispatchTransaction(tr) {
      view.updateState(view.state.apply(tr));
      if (tr.docChanged) {
        if (editor.dropPos !== null) {
          editor.dropPos = tr.mapping.map(editor.dropPos);
        }
        for (const link of editor.links) {
          link.from = tr.mapping.map(link.from, 1);
          link.to = tr.mapping.map(link.to, -1);
        }
        reportDirty(editor);
        // Every text change restarts .NET's auto-save pause; .NET reads nothing until the pause elapses.
        dotnet.invokeMethodAsync("OnChanged").catch(() => {});
      }
    },
    handleClickOn(view, pos, node) {
      return openChip(node, dotnet);
    },
    handleClick(view, pos) {
      const href = attachmentAt(view, pos);
      if (href === null) {
        return false;
      }
      dotnet.invokeMethodAsync("OnAttachment", href).catch(() => {});
      return true;
    },
    // Smart paste (paste.js): a table or a lone link gets its own transaction; anything else, and every
    // Ctrl/Cmd+Shift+V (plain, told the way ProseMirror tells it: Shift held, the key not Insert), takes the default.
    handlePaste(view, event) {
      const data = event && event.clipboardData;
      if (!data) {
        return false;
      }
      const tr = smartPaste(view.state, data.getData("text/plain"), editor.shift && editor.lastKey !== 45);
      if (tr === null) {
        return false;
      }
      view.dispatch(tr.setMeta("paste", true).setMeta("uiEvent", "paste"));
      const url = tr.getMeta(linkMeta);
      if (typeof url === "string") {
        const to = view.state.selection.from;
        editor.links.push({ url, from: to - url.length, to });
        if (editor.links.length > maxPendingLinks) {
          editor.links.shift();
        }
        dotnet.invokeMethodAsync("OnPasteLink", url).catch(() => {});
      }
      return true;
    },
    handleDOMEvents: {
      // Every keydown notes whether Shift is held (a paste then is a plain one, as ProseMirror tells it). A keydown on
      // a focused chip never reaches the keymaps: Enter opens the chip like a click, every other key keeps its browser
      // default (Tab moves on) and changes no text.
      keydown(view, event) {
        editor.shift = event.keyCode === 16 || event.shiftKey;
        editor.lastKey = event.keyCode;
        const node = chipNode(view, event.target);
        if (!node) {
          return false;
        }
        if (opensChip(event)) {
          event.preventDefault();
          openChip(node, dotnet);
        }
        return true;
      },
      // Shift let go: the next paste is not a plain one (ProseMirror's own rule).
      keyup(view, event) {
        if (event.keyCode === 16) {
          editor.shift = false;
        }
        return false;
      },
      // Dropped files: remember where they landed (the cursor when that can't be told) and hand them to .NET.
      drop(view, event) {
        const files = event.dataTransfer ? event.dataTransfer.files : null;
        if (!files || files.length === 0) {
          return false;
        }
        event.preventDefault();
        const at = view.posAtCoords({ left: event.clientX, top: event.clientY });
        editor.dropPos = at ? at.pos : view.state.selection.from;
        sendDrop(dotnet, Array.from(files));
        return true;
      },
    },
  });
  editor.view = view;
  editors.set(element, editor);
}

/**
 * Inserts the link to a stored attachment at the drop position, which then moves past it: an image node (alt name,
 * shown from base64 through a blob: URL) or the text name with a link to src.
 */
export function insertAttachment(element, src, name, isImage, mime, base64) {
  const editor = editors.get(element);
  if (!editor) {
    return;
  }
  const state = editor.view.state;
  let pos = editor.dropPos === null ? state.selection.from : Math.min(Math.max(editor.dropPos, 0), state.doc.content.size);
  const $pos = state.doc.resolve(pos);
  if (!$pos.parent.inlineContent) {
    pos = Selection.near($pos).from;
  }
  if (isImage && base64 && !editor.images.has(src)) {
    editor.images.set(src, blobUrl(mime, base64));
  }
  const node = isImage
    ? schema.nodes.image.create({ src, alt: name })
    : schema.text(name, [schema.marks.link.create({ href: src })]);
  const tr = state.tr.insert(pos, node);
  editor.dropPos = null;
  editor.view.dispatch(tr);
  editor.dropPos = pos + node.nodeSize;
}

/**
 * Gives the link pasted as url (the oldest such paste on this mount) its page title: in one transaction, its own undo
 * step, the URL text becomes title with a link mark to url, but only while the mapped range still holds exactly that URL
 * text without marks. Returns whether it did; the pending link is forgotten either way.
 */
export function setLinkTitle(element, url, title) {
  const editor = editors.get(element);
  if (!editor) {
    return false;
  }
  const index = editor.links.findIndex(link => link.url === url);
  if (index < 0) {
    return false;
  }
  const [link] = editor.links.splice(index, 1);
  const tr = linkTitleTransaction(editor.view.state, link.from, link.to, url, title);
  if (tr === null) {
    return false;
  }
  editor.view.dispatch(tr);
  return true;
}

/** The editor's text as Markdown: the hidden title line first, unchanged. */
export function getMarkdown(element) {
  const editor = editors.get(element);
  return editor ? editorMarkdown(editor) : "";
}

/** True when the document or the attribute block changed since it was opened or last marked clean. */
export function isDirty(element) {
  const editor = editors.get(element);
  return editor ? editorDirty(editor) : false;
}

/** Marks the current document as saved. */
export function markClean(element) {
  const editor = editors.get(element);
  if (editor) {
    editor.clean = editor.view.state.doc;
    editor.cleanAttrs = editor.attrs;
    reportDirty(editor);
  }
}

/**
 * Marks the document clean only when its Markdown still equals markdown (the text a save just wrote), and returns
 * whether it did. Compares and marks in one turn, so an edit typed while the save ran keeps the editor dirty.
 */
export function markCleanIf(element, markdown) {
  const editor = editors.get(element);
  if (!editor || editorMarkdown(editor) !== markdown) {
    return false;
  }
  editor.clean = editor.view.state.doc;
  editor.cleanAttrs = editor.attrs;
  reportDirty(editor);
  return true;
}

/** Removes the editor from element. */
export function destroy(element) {
  const editor = editors.get(element);
  if (editor) {
    editor.view.destroy();
    for (const url of editor.images.values()) {
      URL.revokeObjectURL(url);
    }
    editor.images.clear();
    editors.delete(element);
  }
}

/** Puts the cursor in the editor. */
export function focus(element) {
  const editor = editors.get(element);
  if (editor) {
    editor.view.focus();
  }
}

/** Toggles inline code in the editor of element (the toolbar button) and puts the cursor back in it. */
export function toggleInlineCodeIn(element) {
  runCommand(element, toggleInlineCode);
}

/** Toggles a code block in the editor of element (the toolbar button) and puts the cursor back in it. */
export function toggleCodeBlockIn(element) {
  runCommand(element, toggleCodeBlock);
}

function runCommand(element, command) {
  const editor = editors.get(element);
  if (editor) {
    command(editor.view.state, editor.view.dispatch, editor.view);
    editor.view.focus();
  }
}

/** The headings of the live document, as JSON [{ level, text }] (see headingList); "[]" without an editor. */
export function headings(element) {
  const editor = editors.get(element);
  return JSON.stringify(editor ? headingList(editor.view.state.doc) : []);
}

/**
 * Puts the cursor at the start of the index-th heading (0-based, document order) and scrolls it into view. Returns
 * false, changing nothing, for an index that is not a whole number in range or without an editor.
 */
export function scrollToHeading(element, index) {
  const editor = editors.get(element);
  if (!editor || !Number.isInteger(index) || index < 0) {
    return false;
  }
  const doc = editor.view.state.doc;
  let found = -1;
  let seen = 0;
  doc.descendants((node, pos) => {
    if (found >= 0) {
      return false;
    }
    if (node.type.name === "heading") {
      if (seen === index) {
        found = pos;
      }
      seen++;
      return false;
    }
    return true;
  });
  if (found < 0) {
    return false;
  }
  const view = editor.view;
  view.dispatch(view.state.tr.setSelection(TextSelection.create(view.state.doc, found + 1)).scrollIntoView());
  view.focus();
  const dom = view.nodeDOM(found);
  if (dom && typeof dom.scrollIntoView === "function") {
    dom.scrollIntoView({ block: "start" });
  }
  return true;
}

/** The rows of the editor's attribute block, as JSON [{ key, value }] (see attributeRows); "[]" without an editor. */
export function attributes(element) {
  const editor = editors.get(element);
  return editor ? attributeBlockRowsJson(editor.attrs) : "[]";
}

/**
 * Applies editsJson ([{ line, key, value }], see editAttributes) to the editor's attribute block and reports the dirty
 * state. Returns true; false, changing nothing, without an editor or for malformed edits.
 */
export function setAttributes(element, editsJson) {
  const editor = editors.get(element);
  if (!editor) {
    return false;
  }
  const attrs = editAttributes(editor.attrs, editsJson);
  if (typeof attrs !== "string") {
    return false;
  }
  editor.attrs = attrs;
  reportDirty(editor);
  return true;
}
