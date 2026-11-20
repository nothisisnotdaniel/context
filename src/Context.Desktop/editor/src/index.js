// The bundle's entry: everything the page and the tests use hangs off globalThis.ContextEditor.
import { roundTrip, isLossless, inlineNodes, docJson, splitTitle, outline, attributeRows, editAttributes, attributeSplit } from "./markdown.js";
import { chipDom, headingDom } from "./schema.js";
import { opensChip, typedBlock, typedMarkdown, commandMarkdown, typedAtMarkdown, toggledMarkdown, carriedMarkdown, changedCodeBlocksJson, copyText, toggleInlineCodeIn, toggleCodeBlockIn, mount, getMarkdown, isDirty, markClean, markCleanIf, destroy, focus, insertAttachment, headings, scrollToHeading, attributes, setAttributes, setLinkTitle } from "./view.js";
import { pastedMarkdown, titledMarkdown } from "./paste.js";
import { isMermaidFence, mermaidResultJson, shouldRender } from "./mermaid.js";
import { tokensJson, languageOptionsJson, withLanguage } from "./highlight.js";

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
  attributeRows,
  editAttributes,
  // Test hook: { attrs, body } of a body (the text after splitTitle's head), as JSON.
  attributeSplitJson: body => JSON.stringify(attributeSplit(body)),
  typedBlock,
  typedMarkdown,
  chipDom,
  headingDom,
  opensChip,
  pastedMarkdown,
  titledMarkdown,
  // Mermaid diagrams (job 263): the fence test, the frame reply check (over JSON text) and the size rule.
  isMermaidFence,
  mermaidResultJson,
  shouldRender,
  // Code (job 283): the selection-marked command and typing hooks, the changed-block rule, syntax tokens, the language
  // picker's options and params rule, and the copy fallback chain (given its clipboard functions).
  commandMarkdown,
  typedAtMarkdown,
  // Job 329: ticking a task item keeps its kind.
  toggledMarkdown,
  // Job 330: marking an open checkbox for the next meeting (- [>]) and back.
  carriedMarkdown,
  changedCodeBlocksJson,
  tokensJson,
  languageOptionsJson,
  withLanguage,
  copyText,
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
  attributes,
  setAttributes,
  setLinkTitle,
  toggleInlineCode: toggleInlineCodeIn,
  toggleCodeBlock: toggleCodeBlockIn,
});
