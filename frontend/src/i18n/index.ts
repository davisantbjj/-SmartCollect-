import ptBR from "./pt-BR.json";

type NestedRecord = { [key: string]: string | NestedRecord };

const translations: NestedRecord = ptBR;

export function t(path: string): string {
  const keys = path.split(".");
  let current: string | NestedRecord = translations;
  for (const key of keys) {
    if (typeof current === "string" || current == null) return path;
    current = current[key];
  }
  return typeof current === "string" ? current : path;
}
