// The bundle's entry: everything the page and the tests use hangs off globalThis.ContextEditor.
import { roundTrip, isLossless, inlineNodes, docJson, splitTitle } from "./markdown.js";
import { chipDom, headingDom } from "./schema.js";
import { opensChip, typedBlock, mount, getMarkdown, isDirty, markClean, markCleanIf, destroy, focus, insertAttachment } from "./view.js";

// A file dropped anywhere in the window must never navigate the web view: the editor handles its own drops.
if (typeof globalThis.addEventListener === "function") {
  const block = event => {
    event.preventDefault();
  };
  globalThis.addEventListener("dragover", block);
  globalThis.addEventListener("drop", block);
}

globalThis.ContextEditor = Object.freeze({
  // DOM-free: run in the tests too.
  roundTrip,
  isLossless,
  inlineNodes,
  docJson,
  splitTitle,
  typedBlock,
  chipDom,
  headingDom,
  opensChip,
  // Need a page.
  mount,
  getMarkdown,
  isDirty,
  markClean,
  markCleanIf,
  destroy,
  focus,
  insertAttachment,
});
