// Decisions (job 343, the !d grammar of job 349), DOM-free: the marker and reference rules of the C# DecisionParser,
// mirrored char for char (the same lines, fences, code spans, BOM and .NET whitespace), so the page replays a save's
// numbering and links exactly the paragraphs the index reads; plus the folder line, the picker and the Copy reference
// button. The tests run both on the same fixtures.
import { isNetWhite } from "./markdown.js";

export const Marker = "!d";
export const Token = "#!d";
export const Alphabet = "0123456789abcdefghjkmnpqrstvwxyz";
export const PrefixLength = 4;
const FolderKey = "decision-folder";
export const MaxNumber = 999999999;
export const MaxTextLength = 1000;
export const MaxReferences = 500;
const MaxEdits = 10000;
const Fence = "```";
const Bom = "﻿";

const LetterRe = /^\p{L}$/u;
const LetterOrDigitRe = /^[\p{L}\p{Nd}]$/u;

function isLetter(c) { return c !== undefined && LetterRe.test(c); }
function isLetterOrDigit(c) { return c !== undefined && LetterOrDigitRe.test(c); }
function isSpaceOrTab(c) { return c === " " || c === "\t"; }

function netTrimStart(text) {
  let i = 0;
  while (i < text.length && isNetWhite(text[i])) {
    i++;
  }
  return text.slice(i);
}

function netTrimEnd(text) {
  let end = text.length;
  while (end > 0 && isNetWhite(text[end - 1])) {
    end--;
  }
  return text.slice(0, end);
}

function isFence(line) { return netTrimStart(line).startsWith(Fence); }

// DecisionParser.Lines: each line with the index of its first char; one trailing "\r" is not part of the line.
function lines(markdown) {
  const found = [];
  let start = 0;
  for (;;) {
    const end = markdown.indexOf("\n", start);
    const stop = end < 0 ? markdown.length : end;
    const length = stop > start && markdown[stop - 1] === "\r" ? stop - start - 1 : stop - start;
    found.push({ start, line: markdown.substr(start, length) });
    if (end < 0) {
      return found;
    }
    start = end + 1;
  }
}

// TagParser.IndexedLinesOutsideFences: split on "\n", every trailing "\r" dropped, fence lines and their insides left out.
function linesOutsideFences(markdown) {
  const found = [];
  let inFence = false;
  let start = 0;
  for (const raw of markdown.split("\n")) {
    const lineStart = start;
    start += raw.length + 1;
    const line = raw.replace(/\r+$/, "");
    if (isFence(line)) {
      inFence = !inFence;
      continue;
    }
    if (!inFence) {
      found.push({ start: lineStart, line });
    }
  }
  return found;
}

// The count of ASCII digits at start (1-9 first, at most 9), or 0.
function digits(text, start) {
  if (start >= text.length || text[start] < "1" || text[start] > "9") {
    return 0;
  }
  let end = start + 1;
  while (end < text.length && end - start < 9 && text[end] >= "0" && text[end] <= "9") {
    end++;
  }
  return end - start;
}

// DecisionParser.MarkerStart: where the marker starts on line (after the BOM, indent and list marker) and whether a
// list marker came first.
function markerStart(line) {
  let i = line.length > 0 && line[0] === Bom ? 1 : 0;
  while (i < line.length && isSpaceOrTab(line[i])) {
    i++;
  }
  let listed = false;
  if (i + 1 < line.length && (line[i] === "-" || line[i] === "*" || line[i] === "+") && line[i + 1] === " ") {
    listed = true;
    i += 2;
  }
  return { i, listed };
}

function isDigit(c) { return c !== undefined && c >= "0" && c <= "9"; }

/** DecisionFolder.IsPrefixChar: an Alphabet char in either case (ASCII only). */
export function isPrefixChar(c) {
  return c !== undefined && c.charCodeAt(0) < 128 && Alphabet.includes(c.toLowerCase());
}

/** DecisionFolder.IsPrefix: exactly 4 Alphabet chars, any case. */
export function isPrefix(text) {
  return typeof text === "string" && text.length === PrefixLength && [...text].every(isPrefixChar);
}

/**
 * DecisionParser.NumberToken: the number token at start of text, <p>-<n> (prefix lower-cased) or a plain <n>, as
 * { prefix, number, length }, or null. The char after it is the caller's to check.
 */
export function numberToken(text, start) {
  if (start + PrefixLength + 1 < text.length && text[start + PrefixLength] === "-"
    && isPrefix(text.substr(start, PrefixLength))) {
    const at = start + PrefixLength + 1;
    const count = digits(text, at);
    if (count > 0 && !isDigit(text[at + count])) {
      return { prefix: text.substr(start, PrefixLength).toLowerCase(), number: Number(text.substr(at, count)), length: at + count - start };
    }
  }
  const count = digits(text, start);
  if (count === 0 || isDigit(text[start + count])) {
    return null;
  }
  return { prefix: null, number: Number(text.substr(start, count)), length: count };
}

/** A token without #!d: "1234" without a prefix, else "ab12-1234". */
export function label(prefix, number) {
  return (prefix === null || prefix === undefined ? "" : prefix + "-") + String(number);
}

// DecisionParser.MarkerOf: { at, end, tokenAt, tokenLength, prefix, number (null when bare), listed }, or null when
// line is no marker line.
function marker(line) {
  const { i, listed } = markerStart(line);
  if (!line.startsWith(Marker, i)) {
    return null;
  }
  const after = i + Marker.length;
  if (after < line.length && !isSpaceOrTab(line[after])) {
    return null;
  }
  const tokenAt = after + 1;
  if (after < line.length) {
    const token = numberToken(line, tokenAt);
    if (token !== null && (tokenAt + token.length === line.length || isSpaceOrTab(line[tokenAt + token.length]))) {
      return { at: i, end: tokenAt + token.length, tokenAt, tokenLength: token.length, prefix: token.prefix, number: token.number, listed };
    }
  }
  return { at: i, end: after, tokenAt: after, tokenLength: 0, prefix: null, number: null, listed };
}

/**
 * The marker that starts a paragraph's text (no BOM, indent or list marker: the editor's paragraph text), as
 * { tokenAt, tokenLength, end, prefix, number }, or null.
 */
export function leadingMarker(text) {
  const line = String(text);
  const newline = line.indexOf("\n");
  const found = marker(newline < 0 ? line : line.slice(0, newline));
  return found === null || found.at !== 0 || found.listed ? null : found;
}

function isListLine(line) {
  let i = 0;
  while (i < line.length && isSpaceOrTab(line[i])) {
    i++;
  }
  return i + 1 < line.length && (line[i] === "-" || line[i] === "*" || line[i] === "+") && line[i + 1] === " ";
}

function isAttributeLine(line) {
  if (line.length === 0 || !isLetter(line[0])) {
    return false;
  }
  let end = 1;
  while (end < line.length && (isLetterOrDigit(line[end]) || line[end] === "-" || line[end] === "_")) {
    end++;
  }
  return line.startsWith("::", end);
}

// "blank", "heading" or "text" (a fence line is told apart before).
function kind(line) {
  let i = line.length > 0 && line[0] === Bom ? 1 : 0;
  while (i < line.length && isSpaceOrTab(line[i])) {
    i++;
  }
  if (i === line.length) {
    return "blank";
  }
  let hashes = 0;
  while (i + hashes < line.length && line[i + hashes] === "#") {
    hashes++;
  }
  if (hashes >= 1 && hashes <= 6 && (i + hashes === line.length || isSpaceOrTab(line[i + hashes]))) {
    return "heading";
  }
  return "text";
}

// TagParser.Collapse: trimmed, every whitespace run one space.
function collapse(text) {
  let out = "";
  let pendingSpace = false;
  for (const c of text) {
    if (isNetWhite(c)) {
      pendingSpace = out.length > 0;
      continue;
    }
    if (pendingSpace) {
      out += " ";
      pendingSpace = false;
    }
    out += c;
  }
  return out;
}

function isHighSurrogate(c) { const code = c.charCodeAt(0); return code >= 0xd800 && code <= 0xdbff; }

function decision(all, index, found) {
  const line = all[index].line;
  const parts = [];
  const first = collapse(line.slice(found.end));
  if (first.length > 0) {
    parts.push(first);
  }
  for (let i = index + 1; i < all.length; i++) {
    const next = all[i].line;
    if (isFence(next) || kind(next) !== "text") {
      break;
    }
    const nextMarker = marker(next);
    if ((nextMarker !== null && nextMarker.listed) || isListLine(next)) {
      break;
    }
    const part = collapse(next);
    if (part.length > 0) {
      parts.push(part);
    }
  }
  let text = parts.join("\n");
  if (text.length > MaxTextLength) {
    text = text.slice(0, isHighSurrogate(text[MaxTextLength - 1]) ? MaxTextLength - 1 : MaxTextLength);
  }
  const lineStart = all[index].start;
  const tokenIndex = lineStart + found.tokenAt;
  const markerIndex = lineStart + found.at;
  return {
    line: index + 1,
    prefix: found.prefix,
    number: found.number,
    text: netTrimEnd(text),
    markerIndex,
    tokenIndex,
    tokenLength: found.tokenLength,
    markerEnd: found.tokenLength > 0 ? tokenIndex + found.tokenLength : markerIndex + Marker.length,
  };
}

/** DecisionParser.Markers: every counted marker, in document order, those with an empty text included. */
export function decisionMarkers(markdown) {
  const text = String(markdown);
  const found = [];
  const all = lines(text);
  let inFence = false;
  let head = true;
  let previous = "blank";
  for (let i = 0; i < all.length; i++) {
    const line = all[i].line;
    if (isFence(line)) {
      inFence = !inFence;
      previous = "fence";
      head = false;
      continue;
    }
    if (inFence) {
      continue;
    }
    const at = marker(line);
    if (at !== null && (at.listed || head || previous === "blank" || previous === "heading" || previous === "fence")) {
      found.push(decision(all, i, at));
    }
    const lineKind = kind(line);
    head = head && (lineKind === "blank" || lineKind === "heading" || isAttributeLine(line));
    previous = lineKind;
  }
  return found;
}

/** DecisionParser.Parse: the counted markers whose text is not empty. */
export function decisions(markdown) {
  return decisionMarkers(markdown).filter(d => d.text.length > 0);
}

/**
 * Test hook: decisions(markdown) as JSON [{ line, prefix, number, text, markerIndex, tokenIndex, tokenLength,
 * markerEnd }]; markerEnd is where the note's Copy reference widget sits.
 */
export function decisionsJson(markdown) {
  return JSON.stringify(decisions(markdown));
}

// At the backtick run starting at start: the index after its matching run (same length), or after the run itself.
function skipCode(line, start) {
  let end = start;
  while (end < line.length && line[end] === "`") {
    end++;
  }
  const length = end - start;
  let i = line.indexOf("`", end);
  while (i >= 0) {
    let close = i;
    while (close < line.length && line[close] === "`") {
      close++;
    }
    if (close - i === length) {
      return close;
    }
    i = line.indexOf("`", close);
  }
  return end;
}

/**
 * The reference at index i of text ('#'), as { prefix, number, ref, end }, or null: #!d, then <n> or <p>-<n>, then the
 * end or a char that is no letter, digit, '-', '_' or '#'. ref is the lower-case text after #!d.
 */
export function referenceAt(text, i) {
  if (!text.startsWith(Token, i)) {
    return null;
  }
  const at = i + Token.length;
  const token = numberToken(text, at);
  if (token === null) {
    return null;
  }
  const end = at + token.length;
  const after = text[end];
  if (end < text.length && (isLetterOrDigit(after) || after === "-" || after === "_" || after === "#")) {
    return null;
  }
  return { prefix: token.prefix, number: token.number, ref: label(token.prefix, token.number), end };
}

/** Whether the char before a reference allows one: none, .NET whitespace, or the BOM that starts the text. */
export function referenceMayStart(text, i) {
  return i === 0 || isNetWhite(text[i - 1]) || (i === 1 && text[0] === Bom);
}

/**
 * DecisionParser.References: the distinct references ("1234", "ab12-1234") in order of first appearance, at most
 * MaxReferences. A marker starts with "!", so it is never one.
 */
export function decisionReferenceList(markdown) {
  const text = String(markdown);
  const found = [];
  const seen = new Set();
  if (!text.includes(Token)) {
    return found;
  }
  for (const { line } of linesOutsideFences(text)) {
    let i = 0;
    while (i < line.length) {
      if (line[i] === "`") {
        i = skipCode(line, i);
        continue;
      }
      if (line[i] === "#" && referenceMayStart(line, i)) {
        const ref = referenceAt(line, i);
        if (ref !== null) {
          if (!seen.has(ref.ref)) {
            if (found.length >= MaxReferences) {
              return found;
            }
            seen.add(ref.ref);
            found.push(ref.ref);
          }
          i = ref.end;
          continue;
        }
      }
      i++;
    }
  }
  return found;
}

/** The references of markdown as a JSON array of strings (see decisionReferenceList). */
export function decisionReferences(markdown) {
  return JSON.stringify(decisionReferenceList(markdown));
}

/** DecisionParser.TryParseReference: "1234", "#!d1234", "ab12-1234", "#!dab12-1234" (any case) as its ref, or null. */
export function parseReference(text) {
  if (typeof text !== "string") {
    return null;
  }
  let trimmed = text.trim();
  if (trimmed.slice(0, Token.length).toLowerCase() === Token.toLowerCase()) {
    trimmed = trimmed.slice(Token.length);
  }
  const token = numberToken(trimmed, 0);
  return token !== null && token.length === trimmed.length ? label(token.prefix, token.number) : null;
}

const GuidRe = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;

// A token .NET wrote: <p>-<n> in lower case.
function isOwnToken(text) {
  const token = typeof text === "string" ? numberToken(text, 0) : null;
  return token !== null && token.prefix !== null && token.length === text.length && text === text.toLowerCase();
}

/**
 * The edits of a save ({ set: [[k, old token or null, new token]...], folder: guid or null }), checked: null unless
 * set is a short list of such rows (k a decision index, new a lower-case <p>-<n>) and folder a lower-case GUID or null.
 */
export function readEdits(editsJson) {
  let edits;
  try {
    edits = JSON.parse(String(editsJson));
  } catch {
    return null;
  }
  if (!edits || typeof edits !== "object") {
    return null;
  }
  const { set, folder } = edits;
  if (!Array.isArray(set) || set.length > MaxEdits) {
    return null;
  }
  for (const row of set) {
    if (!Array.isArray(row) || row.length !== 3 || !Number.isInteger(row[0]) || row[0] < 0 || row[0] > MaxEdits
      || (row[1] !== null && (typeof row[1] !== "string" || row[1].length > 20)) || !isOwnToken(row[2])) {
      return null;
    }
  }
  if (folder !== null && folder !== undefined && (typeof folder !== "string" || !GuidRe.test(folder))) {
    return null;
  }
  return { set, folder: folder || null };
}

// The token of decision d as written, lower case; null when bare.
function tokenOf(d) {
  return d.number === null ? null : label(d.prefix, d.number);
}

/**
 * markdown with a save's edits applied (DecisionNumbers.Apply as the page replays it): the k-th decision whose token
 * (lower case, null when bare) is still old gets the new token (a bare one " <token>" after "!d"), every other char
 * stays; then, with a folder and no decision-folder:: line yet, withFolderLine. Entries whose decision changed during
 * the save are left out. Returns null for malformed edits.
 */
export function applyDecisionEditsText(markdown, editsJson) {
  const edits = readEdits(editsJson);
  if (edits === null) {
    return null;
  }
  const out = applySet(String(markdown), edits.set);
  return edits.folder !== null && !hasFolderLine(out) ? withFolderLine(out, edits.folder) : out;
}

/** text with the token rows of readEdits(...).set applied (see applyDecisionEditsText); the folder line is not added. */
export function applySet(text, set) {
  const found = decisions(text);
  const changes = [];
  for (const [k, old, token] of set) {
    const d = found[k];
    if (d !== undefined && tokenOf(d) === old && !changes.some(c => c.d === d)) {
      changes.push({ d, token });
    }
  }
  changes.sort((a, b) => a.d.tokenIndex - b.d.tokenIndex);
  let out = "";
  let at = 0;
  for (const { d, token } of changes) {
    out += text.slice(at, d.tokenIndex) + (d.number === null ? " " + token : token);
    at = d.tokenIndex + d.tokenLength;
  }
  return out + text.slice(at);
}

/** Whether markdown has a decision-folder:: line with a value outside fences (DecisionFolder.NoteGuid not null). */
export function hasFolderLine(markdown) {
  const key = FolderKey + "::";
  for (const { line } of linesOutsideFences(String(markdown))) {
    if (line.slice(0, key.length).toLowerCase() === key && collapse(line.slice(key.length)).length > 0) {
      return true;
    }
  }
  return false;
}

// DecisionFolder.IsTitleLine: "#", a space or tab, then text that is not blank.
function isTitleLine(line) {
  return line.length > 2 && line[0] === "#" && isSpaceOrTab(line[1]) && netTrimStart(line.slice(2)).length > 0;
}

/**
 * DecisionFolder.WithFolderLine: markdown with "decision-folder:: <guid>" added after a leading "# " title line (as
 * the first line of the attribute block when only blank lines lie between the title and a key:: line), else as line 1,
 * with the note's first line ending. A BOM stays first.
 */
export function withFolderLine(markdown, guid) {
  const text = String(markdown);
  const firstNewline = text.indexOf("\n");
  const ending = firstNewline > 0 && text[firstNewline - 1] === "\r" ? "\r\n" : "\n";
  const line = FolderKey + ":: " + String(guid);
  const start = text.length > 0 && text[0] === Bom ? 1 : 0;
  const first = firstNewline < 0 ? text.slice(start) : text.slice(start, firstNewline).replace(/\r+$/, "");
  if (isTitleLine(first)) {
    if (firstNewline < 0) {
      return text + ending + line + ending;
    }
    let at = firstNewline + 1;
    let scan = at;
    while (scan < text.length) {
      const end = text.indexOf("\n", scan);
      const current = end < 0 ? text.slice(scan) : text.slice(scan, end);
      if (netTrimStart(current).length > 0) {
        if (isAttributeLine(current)) {
          at = scan;
        }
        break;
      }
      if (end < 0) {
        break;
      }
      scan = end + 1;
    }
    return text.slice(0, at) + line + ending + text.slice(at);
  }
  return text.slice(0, start) + line + ending + text.slice(start);
}

// The picker (job 349): pure, so the tests run it without a page.
export const MaxPickerRows = 20;
export const NoMatchText = "No matching decisions.";
const MaxQueryLength = 30;

function isQueryChar(c) { return isDigit(c) || isPrefixChar(c) || c === "-"; }

/**
 * The typed text after a #!d that ends at pos (the #!d at a line start or after whitespace, then only digits, prefix
 * chars and "-", at most 30), or null when there is none or pos is inside a code span or a fence.
 */
export function pickerQuery(text, pos) {
  const all = String(text);
  const at = Math.max(0, Math.min(Number(pos) || 0, all.length));
  const lineStart = all.lastIndexOf("\n", at - 1) + 1;
  let inFence = false;
  for (const { line } of lines(all.slice(0, lineStart))) {
    if (isFence(line)) {
      inFence = !inFence;
    }
  }
  const lineEnd = all.indexOf("\n", at);
  const line = all.slice(lineStart, lineEnd < 0 ? all.length : lineEnd).replace(/\r$/, "");
  if (inFence || isFence(line)) {
    return null;
  }
  const offset = at - lineStart;
  let i = 0;
  while (i < offset) {
    if (line[i] === "`") {
      const end = skipCode(line, i);
      if (end > offset) {
        // A matched span that holds pos, or a run with no match (plain text) that ends after it.
        return codeHolds(line, i, end, offset) ? null : scanBack(line, offset);
      }
      i = end;
      continue;
    }
    i++;
  }
  return scanBack(line, offset);
}

// Whether the backtick run at start (skipCode gave end) is a matched span that holds offset.
function codeHolds(line, start, end, offset) {
  let run = start;
  while (run < line.length && line[run] === "`") {
    run++;
  }
  return end > run && offset < end;
}

function scanBack(line, offset) {
  const start = line.lastIndexOf(Token, offset - Token.length);
  if (start < 0 || offset - start - Token.length > MaxQueryLength || !referenceMayStart(line, start)) {
    return null;
  }
  const typed = line.slice(start + Token.length, offset);
  return [...typed].every(isQueryChar) ? typed : null;
}

/**
 * The picker rows for decisions ([{ label, ref, headline }]) and the typed query: those whose headline contains it
 * (ignoring case) or whose ref or label starts with it, at most MaxPickerRows, as { text: "Decision <label>  <headline>",
 * ref, selectable: true }; with none, the one row { text: NoMatchText, ref: null, selectable: false }.
 */
export function pickerRows(decisionList, query) {
  const q = typeof query === "string" ? query.trim().toLowerCase() : "";
  const rows = [];
  for (const d of Array.isArray(decisionList) ? decisionList : []) {
    if (rows.length >= MaxPickerRows) {
      break;
    }
    if (!d || typeof d.label !== "string" || typeof d.headline !== "string" || parseReference(d.ref) !== d.ref) {
      continue;
    }
    if (q.length === 0 || d.headline.toLowerCase().includes(q) || d.ref.startsWith(q) || d.label.toLowerCase().startsWith(q)) {
      rows.push({ text: "Decision " + d.label + "  " + d.headline, ref: d.ref, selectable: true });
    }
  }
  return rows.length > 0 ? rows : [{ text: NoMatchText, ref: null, selectable: false }];
}

/**
 * One key in the open picker. state is { rows, selected, from, to } (from..to: the #!d and the typed text). ArrowUp and
 * ArrowDown give { action: "move", selected } (clamped); Enter or Tab on a selectable row give { action: "insert", from,
 * to, insert: "#!d<ref> " }, on the no-match row { action: "none" }; Escape and space give { action: "close" }; any
 * other key { action: "pass" } (the editor handles it).
 */
export function pickerKey(state, key) {
  const rows = state && Array.isArray(state.rows) ? state.rows : [];
  const last = Math.max(0, rows.length - 1);
  const selected = Math.max(0, Math.min(state && Number.isInteger(state.selected) ? state.selected : 0, last));
  switch (key) {
    case "ArrowDown":
      return { action: "move", selected: Math.min(selected + 1, last) };
    case "ArrowUp":
      return { action: "move", selected: Math.max(selected - 1, 0) };
    case "Enter":
    case "Tab": {
      const row = rows[selected];
      const ref = row && row.selectable ? parseReference(row.ref) : null;
      if (ref === null || ref !== row.ref) {
        return { action: "none" };
      }
      return { action: "insert", from: state.from, to: state.to, insert: Token + ref + " " };
    }
    case "Escape":
    case "Esc":
    case " ":
    case "Space":
      return { action: "close" };
    default:
      return { action: "pass" };
  }
}

/** The picker popup's attributes. */
export function pickerAttrs() {
  return { class: "decision-picker", role: "listbox" };
}

/**
 * The note's Copy reference button for a decision shown as Decision <label> with the reference <ref>: its class,
 * aria-label and title, the visible text and the text it copies (#!d<ref>).
 */
export function copyButtonAttrs(shownLabel, ref) {
  const name = "Copy reference to Decision " + String(shownLabel);
  return { class: "decision-copy", "aria-label": name, title: name, text: "Copy", copy: Token + String(ref) };
}
