import iconsData from "../../public/icons/icons.json";

export const ICONS = Object.fromEntries(
  Object.entries(iconsData).map(([key, { emoji }]) => [key, emoji])
) as Record<keyof typeof iconsData, string>;

export type IconKey = keyof typeof iconsData;
