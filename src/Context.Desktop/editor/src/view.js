// The editor in the page: one ProseMirror view per host element. DOM is built only through ProseMirror's
// renderer and document.createElement; tag and link clicks are reported to .NET, which decides what to open.
import { EditorState } from "prosemirror-state";
import { EditorView } from "prosemirror-view";
import { keymap } from "prosemirror-keymap";
import { history, undo, redo } from "prosemirror-history";
import { baseKeymap, toggleMark, setBlockType, chainCommands } from "prosemirror-commands";
import { splitListItem, liftListItem, sinkListItem } from "prosemirror-schema-list";
import { inputRules, wrappingInputRule, textblockTypeInputRule, InputRule } from "prosemirror-inputrules";
import { schema } from "./schema.js";
import { parse, serialize } from "./markdown.js";

const editors = new WeakMap();

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
  return inputRules({
    rules: [
      wrappingInputRule(/^\s*>\s$/, schema.nodes.blockquote),
      wrappingInputRule(/^\s*-\s$/, schema.nodes.bullet_list),
      wrappingInputRule(/^(\d+)\.\s$/, schema.nodes.ordered_list, m => ({ order: +m[1] })),
      textblockTypeInputRule(/^(#{1,6})\s$/, schema.nodes.heading, m => ({ level: m[1].length })),
      textblockTypeInputRule(/^```$/, schema.nodes.code_block),
      tagRule,
      linkRule,
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

/** Opens markdown in a new editor inside element; tag and link clicks call back into dotnet. */
export function mount(element, markdown, dotnet) {
  destroy(element);
  const doc = parse(markdown);
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
    nodeViews: { list_item: listItemView },
    attributes: { class: "note-editor-body", spellcheck: "true" },
    handleClickOn(view, pos, node) {
      if (node.type === schema.nodes.tag) {
        dotnet.invokeMethodAsync("OnTag", node.attrs.raw).catch(() => {});
        return true;
      }
      if (node.type === schema.nodes.noteLink) {
        dotnet.invokeMethodAsync("OnLink", node.attrs.raw).catch(() => {});
        return true;
      }
      return false;
    },
  });
  editors.set(element, { view, clean: doc });
}

/** The editor's text as Markdown. */
export function getMarkdown(element) {
  const editor = editors.get(element);
  return editor ? serialize(editor.view.state.doc) : "";
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
  }
}

/** Removes the editor from element. */
export function destroy(element) {
  const editor = editors.get(element);
  if (editor) {
    editor.view.destroy();
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
