// The sidebar widths (job 490): .NET calls setSidebars with the left and right width in CSS pixels at startup and on
// every resize. Each one that is an integer inside its side's bounds (kept equal to SidebarLayout in Context.Core) is
// set as --left-width / --right-width on the root element, which app.css builds the grid from; anything else is ignored.

const LeftMin = 160;
const LeftMax = 480;
const RightMin = 200;
const RightMax = 520;

function inRange(value, min, max) {
  return typeof value === "number" && Number.isInteger(value) && value >= min && value <= max;
}

/** Sets --left-width and --right-width (as "<n>px") on the root element; an invalid value leaves its variable alone. */
export function setSidebars(left, right) {
  const root = globalThis.document && globalThis.document.documentElement;
  if (!root || !root.style || typeof root.style.setProperty !== "function") {
    return;
  }
  if (inRange(left, LeftMin, LeftMax)) {
    root.style.setProperty("--left-width", `${left}px`);
  }
  if (inRange(right, RightMin, RightMax)) {
    root.style.setProperty("--right-width", `${right}px`);
  }
}
