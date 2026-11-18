// The bundle's entry: everything the page and the tests use hangs off globalThis.ContextEditor.
import { roundTrip, isLossless, inlineNodes, docJson, splitTitle, outline } from "./markdown.js";
import { chipDom, headingDom } from "./schema.js";
import { opensChip, typedBlock, mount, getMarkdown, isDirty, markClean, markCleanIf, destroy, focus, insertAttachment, headings, scrollToHeading } from "./view.js";

// A file dropped anywhere in the window must never navigate the web view: the editor handles its own drops.
if (typeof globalThis.addEventListener === "function") {
  const block = event => {
    event.preventDefault();
  };
  globalThis.addEventListener("dragover", block);
  globalThis.addEventListener("drop", block);
  // A right click shows no browser menu (Back, Reload, Inspect) on the app's own surface; text inputs and the note
  // editor keep the platform's text menu (Cut, Copy, Paste).
  globalThis.addEventListener("contextmenu", event => {
    const target = event.target;
    if (target && typeof target.closest === "function" && target.closest('input, [contenteditable="true"]') !== null) {
      return;
    }
    event.preventDefault();
  });
}

globalThis.ContextEditor = Object.freeze({
  // DOM-free: run in the tests too.
  roundTrip,
  isLossless,
  inlineNodes,
  docJson,
  splitTitle,
  outline,
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
  headings,
  scrollToHeading,
});
