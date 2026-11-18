// The editor in the page: one ProseMirror view per host element. DOM is built only through ProseMirror's
// renderer and document.createElement; tag, link, mention and attachment PDF clicks (and Enter on a focused chip) and
// dropped files are reported to .NET, which decides what to open or store.
import { EditorState, Selection, TextSelection } from "prosemirror-state";
import { EditorView } from "prosemirror-view";
import { keymap } from "prosemirror-keymap";
import { history, undo, redo } from "prosemirror-history";
import { baseKeymap, toggleMark, setBlockType, chainCommands } from "prosemirror-commands";
import { splitListItem, liftListItem, sinkListItem } from "prosemirror-schema-list";
import { inputRules, wrappingInputRule, textblockTypeInputRule, InputRule } from "prosemirror-inputrules";
import { schema } from "./schema.js";
import { parse, serialize, splitTitle, joinTitle, headingList, normalizePerson, isNetWhite } from "./markdown.js";

const editors = new WeakMap();

// The limits .NET checks again: NoteAttachments.MaxBytes and MaxFilesPerDrop.
const maxAttachmentBytes = 10 * 1024 * 1024;
const maxFilesPerDrop = 10;

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
  return inputRules({
    rules: [
      wrappingInputRule(/^\s*>\s$/, schema.nodes.blockquote),
      wrappingInputRule(/^\s*-\s$/, schema.nodes.bullet_list),
      wrappingInputRule(/^(\d+)\.\s$/, schema.nodes.ordered_list, m => ({ order: +m[1] })),
      // "# " gives level 2 too: the page title is the only level-1 heading.
      textblockTypeInputRule(/^(#{1,6})\s$/, schema.nodes.heading, m => ({ level: Math.max(m[1].length, 2) })),
      textblockTypeInputRule(/^```$/, schema.nodes.code_block),
      tagRule,
      linkRule,
      mentionRule,
      mentionParenRule,
    ],
  });
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

// Tells .NET when the editor's dirty state flipped (never per keystroke); it only shows a marker there.
function reportDirty(editor) {
  const dirty = !editor.view.state.doc.eq(editor.clean);
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
  let state = EditorState.create({ doc: schema.topNodeType.create(null, schema.nodes.paragraph.create()), plugins: [plugin] });
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
  return state.doc;
}

/**
 * Opens markdown in a new editor inside element; tag, link, mention and attachment clicks, drops and dirty flips call
 * back into dotnet. images ([{ src, mime, base64 }], checked by .NET) are shown inline through blob: URLs. With a
 * name, a first line "# <name>" (and the blank lines after it) is not shown: it stays on the editor as head and
 * getMarkdown writes it back unchanged.
 */
export function mount(element, markdown, dotnet, images, name) {
  destroy(element);
  const { head, body } = JSON.parse(splitTitle(markdown, name));
  const doc = parse(body);
  const editor = { view: null, head, clean: doc, dirty: false, dotnet, images: new Map(), dropPos: null };
  for (const image of images || []) {
    if (image && typeof image.src === "string" && !editor.images.has(image.src)) {
      editor.images.set(image.src, blobUrl(image.mime, image.base64));
    }
  }
  const state = EditorState.create({
    doc,
    plugins: [
      rules(),
      history(),
      keymap({
        "Mod-z": undo,
        "Shift-Mod-z": redo,
        "Mod-y": redo,
        "Mod-b": toggleMark(schema.marks.strong),
        "Mod-i": toggleMark(schema.marks.em),
        "Mod-`": toggleMark(schema.marks.code),
        "Enter": splitItem,
        "Tab": sinkListItem(schema.nodes.list_item),
        "Shift-Tab": liftListItem(schema.nodes.list_item),
        "Shift-Ctrl-0": setBlockType(schema.nodes.paragraph),
        "Mod-s": swallow,
        "Mod-k": swallow,
        "Ctrl-k": swallow,
        "Shift-Enter": chainCommands((s, d) => {
          if (d) {
            d(s.tr.replaceSelectionWith(schema.nodes.hard_break.create()).scrollIntoView());
          }
          return true;
        }),
      }),
      keymap(baseKeymap),
    ],
  });
  const view = new EditorView(element, {
    state,
    nodeViews: { list_item: listItemView, image: imageView(editor) },
    attributes: { class: "note-editor-body", spellcheck: "true" },
    dispatchTransaction(tr) {
      view.updateState(view.state.apply(tr));
      if (tr.docChanged) {
        if (editor.dropPos !== null) {
          editor.dropPos = tr.mapping.map(editor.dropPos);
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
    handleDOMEvents: {
      // A keydown on a focused chip never reaches the keymaps: Enter opens the chip like a click, every other key
      // keeps its browser default (Tab moves on) and changes no text.
      keydown(view, event) {
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

/** The editor's text as Markdown: the hidden title line first, unchanged. */
export function getMarkdown(element) {
  const editor = editors.get(element);
  return editor ? joinTitle(editor.head, serialize(editor.view.state.doc)) : "";
}

/** True when the document changed since it was opened or last marked clean. */
export function isDirty(element) {
  const editor = editors.get(element);
  return editor ? !editor.view.state.doc.eq(editor.clean) : false;
}

/** Marks the current document as saved. */
export function markClean(element) {
  const editor = editors.get(element);
  if (editor) {
    editor.clean = editor.view.state.doc;
    reportDirty(editor);
  }
}

/**
 * Marks the document clean only when its Markdown still equals markdown (the text a save just wrote), and returns
 * whether it did. Compares and marks in one turn, so an edit typed while the save ran keeps the editor dirty.
 */
export function markCleanIf(element, markdown) {
  const editor = editors.get(element);
  if (!editor || joinTitle(editor.head, serialize(editor.view.state.doc)) !== markdown) {
    return false;
  }
  editor.clean = editor.view.state.doc;
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
