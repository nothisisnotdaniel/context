// Glossary terms in the editor (job 380), DOM-free. .NET sends the known terms (each one line, already cleaned) with
// their hover titles; matchTerms finds them in a text block's text: case-sensitive, whole words (no letter or digit
// right before or after), left to right without overlaps, the longest term winning at one position.

export const maxTerms = 500;
export const maxTermLength = 100;
export const maxTitleLength = 20000;

const letterOrDigit = /^[\p{L}\p{Nd}]$/u;

function isWordChar(ch) {
  return ch !== undefined && letterOrDigit.test(ch);
}

// The terms of json: [[term, title], ...] (or plain term strings, title ""), as [{ term, title }], or null when
// malformed (not an array, over maxTerms entries, a term empty or over maxTermLength, a title not a bounded string).
export function readGlossary(json) {
  let value;
  try {
    value = JSON.parse(String(json));
  } catch {
    return null;
  }
  if (!Array.isArray(value) || value.length > maxTerms) {
    return null;
  }
  const terms = [];
  for (const entry of value) {
    const pair = typeof entry === "string" ? [entry, ""] : entry;
    if (!Array.isArray(pair) || pair.length !== 2) {
      return null;
    }
    const [term, title] = pair;
    if (typeof term !== "string" || term.length === 0 || term.length > maxTermLength
      || typeof title !== "string" || title.length > maxTitleLength) {
      return null;
    }
    terms.push({ term, title });
  }
  return terms;
}

// The terms bucketed by their first char, longest first within a bucket: Map<char, [{ term, index }]>.
export function bucketTerms(terms) {
  const buckets = new Map();
  terms.forEach((entry, index) => {
    const first = entry.term[0];
    if (!buckets.has(first)) {
      buckets.set(first, []);
    }
    buckets.get(first).push({ term: entry.term, index });
  });
  for (const list of buckets.values()) {
    list.sort((a, b) => b.term.length - a.term.length || a.index - b.index);
  }
  return buckets;
}

// The matches of the bucketed terms in text as [from, to, termIndex], left to right, never overlapping.
export function matchTerms(text, buckets) {
  const found = [];
  let i = 0;
  while (i < text.length) {
    const list = isWordChar(text[i - 1]) ? undefined : buckets.get(text[i]);
    let hit = null;
    if (list !== undefined) {
      for (const { term, index } of list) {
        if (text.startsWith(term, i) && !isWordChar(text[i + term.length])) {
          hit = [i, i + term.length, index];
          break;
        }
      }
    }
    if (hit !== null) {
      found.push(hit);
      i = hit[1];
    } else {
      i++;
    }
  }
  return found;
}

/**
 * Test hook and API: the matches of the terms in termsJson (as setGlossary takes it, or plain term strings) in text,
 * as JSON [[from, to, termIndex], ...]; "[]" for malformed terms.
 */
export function glossaryMatches(text, termsJson) {
  const terms = readGlossary(termsJson);
  if (terms === null) {
    return "[]";
  }
  return JSON.stringify(matchTerms(String(text), bucketTerms(terms)));
}
