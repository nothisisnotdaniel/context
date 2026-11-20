// Mermaid diagrams in the page editor (job 263). A ```mermaid (or ~~~mermaid) fence is drawn by Mermaid, which runs only
// in one hidden, sandboxed renderer frame (wwwroot/mermaid/frame.html, "allow-scripts" only, so it has an opaque origin
// and no access to this page). The page sends the fence's text there and takes back only an SVG string that passes
// mermaidResult, shown as an <img> from a blob: URL, where SVG can run no script and load nothing. Rendering never
// writes to the document: the node view only shows or hides the code next to the picture.
import { Plugin, TextSelection } from "prosemirror-state";
import { Decoration, DecorationSet } from "prosemirror-view";

/** The longest fence text that is sent to the frame; a longer one shows the hint. */
export const MaxDiagramChars = 50000;

/** The longest SVG string the page takes from the frame. */
export const MaxSvgChars = 5000000;

const FrameSource = "mermaid/frame.html";
const DrawingHint = "Drawing diagram…";
const FailedHint = "Can't draw this diagram.";

/** True when the first whitespace-separated word of the fence info is "mermaid" in any ASCII case. */
export function isMermaidFence(params) {
  if (typeof params !== "string") {
    return false;
  }
  const word = params.trim().split(/\s+/)[0];
  return word.length === 7 && word.replace(/[A-Z]/g, c => c.toLowerCase()) === "mermaid";
}

/** True when text is worth sending to the frame: not empty or whitespace only, and at most MaxDiagramChars long. */
export function shouldRender(text) {
  return typeof text === "string" && text.length <= MaxDiagramChars && text.trim().length > 0;
}

/**
 * What a reply from the frame means for the request with id: { kind: "svg", svg } for a drawn diagram whose SVG is a
 * string of at most MaxSvgChars that starts with "<svg" after leading whitespace, { kind: "failed" } for a failure or a
 * reply with any other svg, and { kind: "ignore" } for another id, a non-object or an unknown type.
 */
export function mermaidResult(data, id) {
  if (data === null || typeof data !== "object" || Array.isArray(data) || data.id !== id) {
    return { kind: "ignore" };
  }
  if (data.type === "failed") {
    return { kind: "failed" };
  }
  if (data.type !== "rendered") {
    return { kind: "ignore" };
  }
  const svg = data.svg;
  if (typeof svg !== "string" || svg.length > MaxSvgChars || !/^\s*<svg/.test(svg)) {
    return { kind: "failed" };
  }
  return { kind: "svg", svg };
}

/** Test hook: mermaidResult of the parsed json; a json that can't be parsed is ignored. */
export function mermaidResultJson(json, id) {
  let data;
  try {
    data = JSON.parse(json);
  } catch {
    return { kind: "ignore" };
  }
  return mermaidResult(data, id);
}

// The one renderer frame of the page, made the first time a Mermaid block is shown.
let renderer = null;
let lastId = 0;

function frame() {
  if (renderer !== null) {
    return renderer;
  }
  const element = document.createElement("iframe");
  element.setAttribute("sandbox", "allow-scripts");
  element.className = "mermaid-frame";
  element.setAttribute("aria-hidden", "true");
  element.tabIndex = -1;
  renderer = { element, loaded: false, waiting: [], blocks: new Map() };
  const current = renderer;
  element.addEventListener("load", () => {
    current.loaded = true;
    for (const message of current.waiting.splice(0)) {
      element.contentWindow.postMessage(message, "*");
    }
  });
  // Only the frame's own messages count; each must name a request that is still open.
  globalThis.addEventListener("message", event => {
    if (element.contentWindow === null || event.source !== element.contentWindow) {
      return;
    }
    const data = event.data;
    const id = data !== null && typeof data === "object" ? data.id : undefined;
    const block = current.blocks.get(id);
    if (block !== undefined) {
      block.reply(data, id);
    }
  });
  element.src = FrameSource;
  document.body.appendChild(element);
  return renderer;
}

// Sends one request to the frame (queued until the frame has loaded). The frame has an opaque origin, so "*" is the
// only target it can be given; it is always this page's own frame element.
function send(block, text) {
  const current = frame();
  const id = ++lastId;
  current.blocks.set(id, block);
  const message = { type: "render", id, text };
  if (current.loaded && current.element.contentWindow !== null) {
    current.element.contentWindow.postMessage(message, "*");
  } else {
    current.waiting.push(message);
  }
  return id;
}

function forget(id) {
  if (renderer !== null && id !== 0) {
    renderer.blocks.delete(id);
  }
}

// A Mermaid code block: the code (editable, ProseMirror's contentDOM), a grey hint and the drawn diagram. The code is
// hidden while a diagram is shown and the selection is outside the block (the "mermaid-editing" decoration).
function mermaidView(node, view, getPos) {
  const dom = document.createElement("div");
  dom.className = "mermaid-block";
  const pre = document.createElement("pre");
  const code = document.createElement("code");
  pre.appendChild(code);
  const hint = document.createElement("div");
  hint.className = "mermaid-hint grey";
  hint.contentEditable = "false";
  const picture = document.createElement("div");
  picture.className = "mermaid-diagram";
  picture.contentEditable = "false";
  dom.appendChild(pre);
  dom.appendChild(hint);
  dom.appendChild(picture);

  const block = {
    text: null,
    // The id of the request in flight (0 when none), the text it carries, and whether the view is gone.
    id: 0,
    sent: null,
    gone: false,
    url: null,
    reply(data, id) {
      if (id !== block.id) {
        return;
      }
      const result = mermaidResult(data, id);
      if (result.kind === "ignore") {
        return;
      }
      forget(id);
      block.id = 0;
      if (block.gone) {
        return;
      }
      // The text changed while this one was drawn: draw the newest text instead.
      if (block.sent !== block.text) {
        block.draw();
        return;
      }
      if (result.kind === "svg") {
        show(result.svg);
      } else {
        clearPicture();
        setHint(FailedHint);
      }
    },
    draw() {
      const text = block.text;
      if (!shouldRender(text)) {
        clearPicture();
        setHint(text.trim().length === 0 ? "" : FailedHint);
        return;
      }
      setHint(DrawingHint);
      dom.classList.remove("drawn");
      if (block.id !== 0) {
        // One request per block at a time; its reply starts the next.
        return;
      }
      block.sent = text;
      block.id = send(block, text);
    },
  };

  function setHint(text) {
    hint.textContent = text;
    hint.hidden = text.length === 0;
  }

  function clearPicture() {
    picture.replaceChildren();
    dom.classList.remove("drawn");
    if (block.url !== null) {
      URL.revokeObjectURL(block.url);
      block.url = null;
    }
  }

  function show(svg) {
    clearPicture();
    block.url = URL.createObjectURL(new Blob([svg], { type: "image/svg+xml" }));
    const img = document.createElement("img");
    img.src = block.url;
    img.alt = "Diagram";
    img.draggable = false;
    picture.appendChild(img);
    setHint("");
    dom.classList.add("drawn");
  }

  // A click on the diagram puts the cursor at the start of the block's code (a selection change, never a doc change).
  picture.addEventListener("mousedown", event => {
    event.preventDefault();
    const pos = getPos();
    if (typeof pos === "number") {
      view.dispatch(view.state.tr.setSelection(TextSelection.create(view.state.doc, pos + 1)));
      view.focus();
    }
  });

  const update = next => {
    if (next.type !== node.type || !isMermaidFence(next.attrs.params)) {
      return false;
    }
    node = next;
    if (next.textContent !== block.text) {
      block.text = next.textContent;
      block.draw();
    }
    return true;
  };
  setHint("");
  update(node);

  return {
    dom,
    contentDOM: code,
    update,
    stopEvent(event) { return picture.contains(event.target); },
    ignoreMutation(mutation) {
      return mutation.type !== "selection" && !code.contains(mutation.target) && mutation.target !== pre;
    },
    destroy() {
      block.gone = true;
      forget(block.id);
      block.id = 0;
      clearPicture();
    },
  };
}

/** The code_block node view: Mermaid fences get the diagram view, every other block ProseMirror's own rendering. */
export function codeBlockView(node, view, getPos) {
  return isMermaidFence(node.attrs.params) ? mermaidView(node, view, getPos) : null;
}

/** Marks the Mermaid block that holds the selection, so its code shows above the diagram. */
export function mermaidEditing() {
  return new Plugin({
    props: {
      decorations(state) {
        const { $from, $to } = state.selection;
        const found = [];
        for (const $pos of [$from, $to]) {
          for (let depth = $pos.depth; depth > 0; depth--) {
            const node = $pos.node(depth);
            if (node.type.name === "code_block" && isMermaidFence(node.attrs.params)) {
              const start = $pos.before(depth);
              if (!found.some(d => d.from === start)) {
                found.push(Decoration.node(start, start + node.nodeSize, { class: "mermaid-editing" }));
              }
              break;
            }
          }
        }
        return found.length === 0 ? null : DecorationSet.create(state.doc, found);
      },
    },
  });
}
