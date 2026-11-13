// The bundle's entry: everything the page and the tests use hangs off globalThis.ContextEditor.
import { roundTrip, isLossless, inlineNodes, docJson } from "./markdown.js";
import { chipDom } from "./schema.js";
import { opensChip, mount, getMarkdown, isDirty, markClean, destroy, focus } from "./view.js";

globalThis.ContextEditor = Object.freeze({
  // DOM-free: run in the tests too.
  roundTrip,
  isLossless,
  inlineNodes,
  docJson,
  chipDom,
  opensChip,
  // Need a page.
  mount,
  getMarkdown,
  isDirty,
  markClean,
  destroy,
  focus,
});
