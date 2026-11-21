// #!d decisions (job 343), DOM-free: the marker, reference and strip rules of the C# DecisionParser, mirrored char for
// char (the same lines, fences, code spans, BOM and .NET whitespace), so the page numbers, strips and links exactly the
// paragraphs the index reads. The tests run both on the same fixtures.
import { isNetWhite } from "./markdown.js";

export const Token = "#!d";
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

// DecisionParser.Marker: { at, length, number (null when bare), listed }, or null when line is no marker line.
function marker(line) {
  let i = line.length > 0 && line[0] === Bom ? 1 : 0;
  while (i < line.length && isSpaceOrTab(line[i])) {
    i++;
  }
  let listed = false;
  if (i + 1 < line.length && (line[i] === "-" || line[i] === "*" || line[i] === "+") && line[i + 1] === " ") {
    listed = true;
    i += 2;
  }
  if (!line.startsWith(Token, i)) {
    return null;
  }
  const digitsAt = i + Token.length;
  const count = digits(line, digitsAt);
  const end = digitsAt + count;
  if (end < line.length && !isSpaceOrTab(line[end])) {
    return null;
  }
  return { at: i, length: end - i, number: count === 0 ? null : Number(line.substr(digitsAt, count)), listed };
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
  const after = found.at + found.length;
  let rest = after;
  while (rest < line.length && isSpaceOrTab(line[rest])) {
    rest++;
  }
  let strip = rest - found.at;
  const lineStart = all[index].start;
  if (!found.listed && rest === line.length && index + 1 < all.length) {
    strip = all[index + 1].start - (lineStart + found.at);
  }
  const parts = [];
  const first = collapse(line.slice(after));
  if (first.length > 0) {
    parts.push(first);
  }
  for (let i = index + 1; i < all.length; i++) {
    const next = all[i].line;
    const nextMarker = isFence(next) ? null : marker(next);
    if (isFence(next) || kind(next) !== "text" || (nextMarker !== null && nextMarker.listed) || isListLine(next)) {
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
  return {
    line: index + 1,
    number: found.number,
    text: netTrimEnd(text),
    markerIndex: lineStart + found.at,
    markerLength: found.length,
    stripLength: strip,
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

/** Test hook: decisions(markdown) as JSON [{ line, number, text, markerIndex, markerLength, stripLength }]. */
export function decisionsJson(markdown) {
  return JSON.stringify(decisions(markdown));
}

/** DecisionParser.StripMarkers: the marker of every numbered decision removed; bare markers and references stay. */
export function stripDecisionMarkers(markdown) {
  const text = String(markdown);
  const numbered = decisions(text).filter(d => d.number !== null);
  if (numbered.length === 0) {
    return text;
  }
  let out = "";
  let at = 0;
  for (const d of numbered) {
    out += text.slice(at, d.markerIndex);
    at = d.markerIndex + d.stripLength;
  }
  return out + text.slice(at);
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
 * The reference at index i of text ('#'), as { number, end }, or null: #!d, 1-9 digits starting with 1-9, then the end
 * or a char that is no letter, digit, '-', '_' or '#'.
 */
export function referenceAt(text, i) {
  if (!text.startsWith(Token, i)) {
    return null;
  }
  const digitsAt = i + Token.length;
  const count = digits(text, digitsAt);
  const end = digitsAt + count;
  if (count === 0) {
    return null;
  }
  const after = text[end];
  if (end < text.length && (isLetterOrDigit(after) || after === "-" || after === "_" || after === "#")) {
    return null;
  }
  return { number: Number(text.substr(digitsAt, count)), end };
}

/** Whether the char before a reference allows one: none, .NET whitespace, or the BOM that starts the text. */
export function referenceMayStart(text, i) {
  return i === 0 || isNetWhite(text[i - 1]) || (i === 1 && text[0] === Bom);
}

/** DecisionParser.References: the distinct referenced numbers in order of first appearance, at most MaxReferences. */
export function decisionReferenceList(markdown) {
  const text = String(markdown);
  const markers = new Set(decisions(text).map(d => d.markerIndex));
  const found = [];
  const seen = new Set();
  for (const { start, line } of linesOutsideFences(text)) {
    let i = 0;
    while (i < line.length) {
      if (line[i] === "`") {
        i = skipCode(line, i);
        continue;
      }
      if (line[i] === "#" && referenceMayStart(line, i) && !markers.has(start + i)) {
        const ref = referenceAt(line, i);
        if (ref !== null) {
          if (!seen.has(ref.number)) {
            if (found.length >= MaxReferences) {
              return found;
            }
            seen.add(ref.number);
            found.push(ref.number);
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

/** The referenced numbers of markdown as a JSON array (see decisionReferenceList). */
export function decisionReferences(markdown) {
  return JSON.stringify(decisionReferenceList(markdown));
}

function isNumber(n) { return Number.isInteger(n) && n >= 1 && n <= MaxNumber; }

/** The edits of a save ({ bare: [n...], strip: [n...] }), checked: null unless both are short lists of valid numbers. */
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
  const { bare, strip } = edits;
  if (!Array.isArray(bare) || !Array.isArray(strip) || bare.length > MaxEdits || strip.length > MaxEdits) {
    return null;
  }
  if (!bare.every(isNumber) || !strip.every(isNumber)) {
    return null;
  }
  return { bare, strip };
}

/**
 * markdown with a save's edits applied (DecisionNumbers.Apply as the page replays it): the k-th bare decision gets
 * bare[k], and for a number listed c times in strip the last c decisions holding it lose their marker. Returns null
 * for malformed edits; extra entries (the text changed during the save) are left out.
 */
export function applyDecisionEditsText(markdown, editsJson) {
  const edits = readEdits(editsJson);
  if (edits === null) {
    return null;
  }
  const text = String(markdown);
  const found = decisions(text);
  const stripCounts = new Map();
  for (const n of edits.strip) {
    stripCounts.set(n, (stripCounts.get(n) || 0) + 1);
  }
  const stripped = new Set();
  for (let i = found.length - 1; i >= 0; i--) {
    const n = found[i].number;
    if (n !== null && (stripCounts.get(n) || 0) > 0) {
      stripCounts.set(n, stripCounts.get(n) - 1);
      stripped.add(i);
    }
  }
  let out = "";
  let at = 0;
  let bare = 0;
  for (let i = 0; i < found.length; i++) {
    const d = found[i];
    if (d.number === null && bare < edits.bare.length) {
      const end = d.markerIndex + d.markerLength;
      out += text.slice(at, end) + String(edits.bare[bare]);
      bare++;
      at = end;
    } else if (stripped.has(i)) {
      out += text.slice(at, d.markerIndex);
      at = d.markerIndex + d.stripLength;
    }
  }
  return out + text.slice(at);
}

/** Pasted text: its numbered markers stripped, unless it is the last cut of the same editor (lastCut), kept as is. */
export function pastedDecisionMarkdown(text, lastCut) {
  const pasted = String(text);
  return typeof lastCut === "string" && pasted === lastCut ? pasted : stripDecisionMarkers(pasted);
}

/** Whether text starts with numbered marker n: the token, then nothing or whitespace. */
export function startsWithMarker(text, n) {
  const token = Token + String(n);
  if (!text.startsWith(token)) {
    return false;
  }
  const after = text[token.length];
  return after === undefined || isNetWhite(after);
}

/**
 * The length of the numbered marker that starts a pasted paragraph's text (the token, the spaces and tabs after it and,
 * when nothing else is on its line, the line break), or 0.
 */
export function leadingMarkerLength(text) {
  if (!text.startsWith(Token)) {
    return 0;
  }
  const count = digits(text, Token.length);
  let end = Token.length + count;
  if (count === 0 || (end < text.length && !isSpaceOrTab(text[end]) && text[end] !== "\n")) {
    return 0;
  }
  while (end < text.length && isSpaceOrTab(text[end])) {
    end++;
  }
  if (text[end] === "\n") {
    end++;
  }
  return end;
}
