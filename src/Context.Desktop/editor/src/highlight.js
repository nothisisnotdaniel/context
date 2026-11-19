// Syntax colours for code blocks (job 283). DOM-free: lowlight (on highlight.js) gives a tree, and tokens() turns it
// into [from, to, className] ranges that view.js draws as class decorations. No HTML string is ever built or read.
// Only the languages below are registered, and the language always comes from the fence: no auto-detection.
import { createLowlight } from "lowlight";
import bash from "highlight.js/lib/languages/bash";
import csharp from "highlight.js/lib/languages/csharp";
import css from "highlight.js/lib/languages/css";
import javascript from "highlight.js/lib/languages/javascript";
import json from "highlight.js/lib/languages/json";
import powershell from "highlight.js/lib/languages/powershell";
import python from "highlight.js/lib/languages/python";
import sql from "highlight.js/lib/languages/sql";
import typescript from "highlight.js/lib/languages/typescript";
import xml from "highlight.js/lib/languages/xml";
import yaml from "highlight.js/lib/languages/yaml";

/** The longest code block text that is highlighted; a longer one stays plain. */
export const MaxHighlightChars = 20000;

/** The registered languages: the name written into the fence, the name the picker shows and the accepted aliases. */
export const Languages = Object.freeze([
  { name: "bash", label: "Bash", aliases: ["sh", "shell", "zsh"] },
  { name: "csharp", label: "C#", aliases: ["cs", "c#"] },
  { name: "css", label: "CSS", aliases: [] },
  { name: "javascript", label: "JavaScript", aliases: ["js"] },
  { name: "json", label: "JSON", aliases: [] },
  { name: "powershell", label: "PowerShell", aliases: ["ps1", "pwsh"] },
  { name: "python", label: "Python", aliases: ["py"] },
  { name: "sql", label: "SQL", aliases: [] },
  { name: "typescript", label: "TypeScript", aliases: ["ts"] },
  { name: "xml", label: "XML / HTML", aliases: ["html"] },
  { name: "yaml", label: "YAML", aliases: ["yml"] },
]);

const lowlight = createLowlight({ bash, csharp, css, javascript, json, powershell, python, sql, typescript, xml, yaml });

// Every accepted word (lower case) to the registered name.
const byWord = new Map();
for (const language of Languages) {
  byWord.set(language.name, language.name);
  for (const alias of language.aliases) {
    byWord.set(alias, language.name);
  }
}

function asciiLower(text) {
  return text.replace(/[A-Z]/g, c => c.toLowerCase());
}

/** The first whitespace-separated word of a fence's info string ("" when there is none). */
export function firstWord(params) {
  if (typeof params !== "string") {
    return "";
  }
  const trimmed = params.trim();
  return trimmed.length === 0 ? "" : trimmed.split(/\s+/)[0];
}

/** The registered name for a language word or alias in any ASCII case, or null. */
export function languageOf(word) {
  if (typeof word !== "string" || word.length === 0) {
    return null;
  }
  return byWord.get(asciiLower(word)) || null;
}

/**
 * The highlighted ranges of text in lang: [from, to, className] triples, offsets into text, sorted and not
 * overlapping, className the innermost hljs-* classes. [] for an empty, unknown or Mermaid language, for text over
 * MaxHighlightChars, and when lowlight fails; it never throws.
 */
export function tokens(text, lang) {
  const name = languageOf(lang);
  if (name === null || typeof text !== "string" || text.length === 0 || text.length > MaxHighlightChars) {
    return [];
  }
  let tree;
  try {
    tree = lowlight.highlight(name, text);
  } catch {
    return [];
  }
  const out = [];
  let offset = 0;
  const walk = (node, className) => {
    if (node.type === "text") {
      const length = node.value.length;
      if (className !== null && length > 0) {
        out.push([offset, offset + length, className]);
      }
      offset += length;
      return;
    }
    let inner = className;
    if (node.type === "element") {
      const classes = ((node.properties && node.properties.className) || []).filter(c => typeof c === "string" && c.startsWith("hljs-"));
      if (classes.length > 0) {
        inner = classes.join(" ");
      }
    }
    for (const child of node.children || []) {
      walk(child, inner);
    }
  };
  try {
    walk(tree, null);
  } catch {
    return [];
  }
  // A tree whose text is not exactly the input can't give offsets into it.
  return offset === text.length ? out : [];
}

/** Test hook: tokens(text, lang) as JSON. */
export function tokensJson(text, lang) {
  return JSON.stringify(tokens(text, lang));
}

/**
 * The language picker's options for a block with these params, as [{ value, label, selected }]: Plain text (value
 * ""), every registered language by display name, and the block's first word when it is unknown, so it is never lost.
 */
export function languageOptions(params) {
  const word = firstWord(params);
  const known = languageOf(word);
  const options = [{ value: "", label: "Plain text", selected: word.length === 0 }];
  for (const language of Languages) {
    options.push({ value: language.name, label: language.label, selected: known === language.name });
  }
  if (word.length > 0 && known === null) {
    options.push({ value: word, label: word, selected: true });
  }
  return options;
}

/** Test hook: languageOptions(params) as JSON. */
export function languageOptionsJson(params) {
  return JSON.stringify(languageOptions(params));
}

/**
 * The params after choosing a language: choice replaces only the first word (or becomes the params when there is
 * none); "" (Plain text) removes the first word and keeps the rest.
 */
export function withLanguage(params, choice) {
  const text = typeof params === "string" ? params : "";
  const word = typeof choice === "string" ? choice.trim() : "";
  const match = /^(\s*)(\S+)(.*)$/s.exec(text);
  if (match === null) {
    return word;
  }
  if (word.length === 0) {
    return match[3].replace(/^\s+/, "");
  }
  return match[1] + word + match[3];
}
