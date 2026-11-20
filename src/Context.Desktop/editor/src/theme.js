// The app theme (job 341): .NET calls setTheme with "system", "light" or "dark" at startup and after every change in
// Settings. "light" and "dark" set data-theme on the root element, which app.css keys its colour variables on; any
// other value removes it, so the page follows the OS through prefers-color-scheme again.

/** Sets or removes data-theme on the document's root element; only "light" and "dark" are kept. */
export function setTheme(name) {
  const root = globalThis.document && globalThis.document.documentElement;
  if (!root) {
    return;
  }
  if (name === "light" || name === "dark") {
    root.setAttribute("data-theme", name);
  } else {
    root.removeAttribute("data-theme");
  }
}

/**
 * The Mermaid theme for a render: "dark" when data-theme is "dark", or when there is none and the system prefers
 * dark; otherwise "default". Pure, so the tests can call it for every combination.
 */
export function mermaidTheme(dataTheme, prefersDark) {
  if (dataTheme === "dark") {
    return "dark";
  }
  if (dataTheme === null || dataTheme === undefined || dataTheme === "") {
    return prefersDark === true ? "dark" : "default";
  }
  return "default";
}

/** The Mermaid theme for the page as it is now. */
export function currentMermaidTheme() {
  const root = globalThis.document && globalThis.document.documentElement;
  const dataTheme = root ? root.getAttribute("data-theme") : null;
  const prefersDark = typeof globalThis.matchMedia === "function"
    && globalThis.matchMedia("(prefers-color-scheme: dark)").matches;
  return mermaidTheme(dataTheme, prefersDark);
}
