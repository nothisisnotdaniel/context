// npm run build: bundles src/index.js into one IIFE at ../wwwroot/editor/editor.js and copies the styles next to it, and
// bundles Mermaid with src/mermaid-frame.js into ../wwwroot/mermaid/mermaid-frame.js, the renderer frame's only script.
import { build } from "esbuild";
import { readFileSync, writeFileSync, mkdirSync } from "node:fs";

const out = new URL("../wwwroot/editor/", import.meta.url);
mkdirSync(out, { recursive: true });

await build({
  entryPoints: ["src/index.js"],
  bundle: true,
  format: "iife",
  target: "es2020",
  minify: true,
  legalComments: "none",
  charset: "utf8",
  outfile: new URL("editor.js", out).pathname,
});

const css = readFileSync("node_modules/prosemirror-view/style/prosemirror.css", "utf8") + "\n" + readFileSync("src/editor.css", "utf8");
writeFileSync(new URL("editor.css", out), css);

const frameOut = new URL("../wwwroot/mermaid/", import.meta.url);
mkdirSync(frameOut, { recursive: true });

await build({
  entryPoints: ["src/mermaid-frame.js"],
  bundle: true,
  format: "iife",
  target: "es2020",
  minify: true,
  legalComments: "none",
  charset: "utf8",
  outfile: new URL("mermaid-frame.js", frameOut).pathname,
});
