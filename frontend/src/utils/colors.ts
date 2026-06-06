/** Reads a Tailwind CSS color variable from :root and returns its computed value */
function getCssColor(variable: string): string {
  return getComputedStyle(document.documentElement).getPropertyValue(variable).trim();
}

/** Lazy-loaded color map that reads from the CSS variables defined in styles/index.css */
export const colors = {
  get accent()   { return getCssColor("--color-accent"); },
  get danger()   { return getCssColor("--color-danger"); },
  get warn()     { return getCssColor("--color-warn"); },
  get success()  { return getCssColor("--color-success"); },
  get text()     { return getCssColor("--color-text-primary"); },
  get text2()    { return getCssColor("--color-text-secondary"); },
  get text3()    { return getCssColor("--color-text-muted"); },
  get surface()  { return getCssColor("--color-surface"); },
  get surface2() { return getCssColor("--color-surface-2"); },
  get surface3() { return getCssColor("--color-surface-3"); },
  get border()   { return getCssColor("--color-border-subtle"); },
  get border2()  { return getCssColor("--color-border-subtle-2"); },
  get wa()       { return getCssColor("--color-wa"); },
  get open()     { return getCssColor("--badge-open-bg"); },
  get openAccent() { return getCssColor("--badge-open-accent"); },
  get openSoft() { return getCssColor("--badge-open-soft"); },
};
