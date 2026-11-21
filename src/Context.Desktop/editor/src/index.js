// The bundle's entry: everything the page and the tests use hangs off globalThis.ContextEditor.
import { roundTrip, isLossless, inlineNodes, docJson, webHref, splitTitle, outline, attributeRows, editAttributes, attributeSplit } from "./markdown.js";
import { chipDom, headingDom } from "./schema.js";
import { opensChip, typedBlock, typedMarkdown, commandMarkdown, typedAtMarkdown, clickedLinkJson, linkDomJson, toggledMarkdown, carriedMarkdown, changedCodeBlocksJson, copyText, copyToClipboard, toggleInlineCodeIn, toggleCodeBlockIn, mount, getMarkdown, isDirty, markClean, markCleanIf, destroy, focus, insertAttachment, headings, scrollToHeading, attributes, setAttributes, setLinkTitle, applyDecisionEdits, decisionReferencesIn, setDecisionTitles, setDecisionPrefixes, scrollToDecision, decisionEditsMarkdown, decisionRefsJson } from "./view.js";
import { decisionsJson, decisionReferences, applyDecisionEditsText, withFolderLine, pickerQuery, pickerRows, pickerKey, pickerAttrs, copyButtonAttrs } from "./decisions.js";
import { pastedMarkdown, pastedDocJson, titledMarkdown } from "./paste.js";
import { isMermaidFence, mermaidResultJson, shouldRender } from "./mermaid.js";
import { tokensJson, languageOptionsJson, withLanguage } from "./highlight.js";
import { setTheme, mermaidTheme } from "./theme.js";

// A file dropped anywhere in the window must never navigate the web view: the editor handles its own drops.
if (typeof globalThis.addEventListener === "function") {
  const block = event => {
    event.preventDefault();
  };
  globalThis.addEventListener("dragover", block);
  globalThis.addEventListener("drop", block);
  // A right click shows no browser menu (Back, Reload, Inspect) on the app's own surface; text inputs and the note
  // editor keep the platform's text menu (Cut, Copy, Paste).
  // Ctrl/Cmd+P opens the quick switcher (job 364) and must never reach the web view's print command.
  globalThis.addEventListener("keydown", event => {
    if ((event.ctrlKey || event.metaKey) && !event.altKey && !event.shiftKey && typeof event.key === "string"
      && event.key.toLowerCase() === "p") {
      event.preventDefault();
    }
  });
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
  // Web links (job 354): the one web-link rule, the paste and click hooks and the link's toDOM spec.
  webHref,
  pastedDocJson,
  clickedLinkJson,
  linkDomJson,
  // Mermaid diagrams (job 263): the fence test, the frame reply check (over JSON text) and the size rule.
  isMermaidFence,
  mermaidResultJson,
  shouldRender,
  // Theme (job 341): the root's data-theme, and the pure Mermaid theme rule.
  setTheme,
  mermaidTheme,
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
  copyToClipboard,
  // Decisions (jobs 343, 349): the parser mirror, the save edits replayed, the folder line, the reference and Copy
  // reference decorations, and the picker's pure rules (as JSON for the tests: Jint reads plain values).
  decisionsJson,
  decisionReferences,
  applyDecisionEditsText,
  withFolderLine,
  decisionEditsMarkdown,
  decisionRefsJson,
  pickerQuery,
  pickerRowsJson: (decisionsText, query) => JSON.stringify(pickerRows(JSON.parse(String(decisionsText)), query)),
  pickerKeyJson: (stateText, key) => JSON.stringify(pickerKey(JSON.parse(String(stateText)), key)),
  pickerAttrsJson: () => JSON.stringify(pickerAttrs()),
  copyButtonAttrsJson: (shownLabel, ref) => JSON.stringify(copyButtonAttrs(shownLabel, ref)),
  pickerRows,
  pickerKey,
  pickerAttrs,
  copyButtonAttrs,
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
  applyDecisionEdits,
  decisionReferencesIn,
  setDecisionTitles,
  setDecisionPrefixes,
  scrollToDecision,
  toggleInlineCode: toggleInlineCodeIn,
  toggleCodeBlock: toggleCodeBlockIn,
});
