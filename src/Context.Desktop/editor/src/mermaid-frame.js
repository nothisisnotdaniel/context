// The Mermaid renderer frame (job 263), bundled to wwwroot/mermaid/mermaid-frame.js and loaded only by
// wwwroot/mermaid/frame.html, which the page embeds with sandbox="allow-scripts" (an opaque origin, no access to the
// page) under a CSP with no network. It takes { type: "render", id, text, theme } from its parent only and answers
// { type: "rendered", id, svg } or { type: "failed", id }; Mermaid's error text never leaves the frame.
import mermaid from "mermaid";

// Job 341: the page chooses the theme for each render (its Settings choice or the OS); only "dark" and "default" are
// taken, anything else counts as "default".
function themeOf(value) {
  return value === "dark" ? "dark" : "default";
}

function configure(theme) {
  mermaid.initialize({
    startOnLoad: false,
    securityLevel: "strict",
    htmlLabels: false,
    flowchart: { htmlLabels: false },
    theme: themeOf(theme),
  });
}

configure("default");

// The SVG as a standalone image: well-formed XML (XMLSerializer), with its size from the viewBox so an <img> shows it
// at its drawn size instead of the 300x150 default for a "100%" width.
function standalone(svg) {
  const doc = new DOMParser().parseFromString(svg, "text/html");
  const root = doc.querySelector("svg");
  if (root === null) {
    return null;
  }
  const box = (root.getAttribute("viewBox") || "").trim().split(/[\s,]+/).map(Number);
  if (box.length === 4 && box.every(Number.isFinite) && box[2] > 0 && box[3] > 0) {
    root.setAttribute("width", String(Math.ceil(box[2])));
    root.setAttribute("height", String(Math.ceil(box[3])));
    root.style.removeProperty("max-width");
  }
  root.setAttribute("xmlns", "http://www.w3.org/2000/svg");
  return new XMLSerializer().serializeToString(root);
}

let count = 0;
// One render at a time: Mermaid keeps state per render.
let queue = Promise.resolve();

async function render(id, text, theme) {
  let reply;
  try {
    count++;
    configure(theme);
    const { svg } = await mermaid.render("diagram-" + count, text);
    const image = standalone(svg);
    reply = image === null ? { type: "failed", id } : { type: "rendered", id, svg: image };
  } catch {
    reply = { type: "failed", id };
  }
  // Mermaid leaves a temporary element behind after a failed render.
  const left = document.getElementById("ddiagram-" + count);
  if (left !== null) {
    left.remove();
  }
  // "*": the page's origin may be a custom scheme ("app://") whose origin is opaque and can't be named; the reply goes
  // only to window.parent, the page that embeds this frame, and holds nothing the page didn't send.
  window.parent.postMessage(reply, "*");
}

window.addEventListener("message", event => {
  if (event.source !== window.parent) {
    return;
  }
  const data = event.data;
  if (data === null || typeof data !== "object" || data.type !== "render" || !Number.isInteger(data.id) || typeof data.text !== "string") {
    return;
  }
  const theme = data.theme;
  queue = queue.then(() => render(data.id, data.text, theme));
});
