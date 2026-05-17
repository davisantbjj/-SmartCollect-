export const formatBRL = (value: number) =>
  value >= 1000000
    ? `R$ ${(value / 1000000).toFixed(1)}M`
    : value >= 1000
    ? `R$ ${(value / 1000).toFixed(0)}K`
    : `R$ ${value.toLocaleString("pt-BR")}`;

export const formatBRLFull = (value: number) =>
  `R$ ${value.toLocaleString("pt-BR", { minimumFractionDigits: 2 })}`;

export const formatIsoDateBR = (value?: string | null) => {
  if (!value) return "";

  const match = value.match(/^(\d{4})-(\d{2})-(\d{2})/);
  if (match)
    return `${match[3]}/${match[2]}/${match[1]}`;

  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime()))
    return value;

  return parsed.toLocaleDateString("pt-BR", { timeZone: "UTC" });
};

export const formatDateTimeBR = (value?: string | null) => {
  if (!value) return "";

  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime()))
    return value;

  const pad = (part: number) => String(part).padStart(2, "0");
  const date = `${pad(parsed.getDate())}/${pad(parsed.getMonth() + 1)}/${parsed.getFullYear()}`;
  const time = `${pad(parsed.getHours())}:${pad(parsed.getMinutes())}`;
  return `${date} ${time}`;
};

export const onlyDigits = (value: string) => value.replace(/\D/g, "");

export const formatCnpj = (value: string) => {
  const d = onlyDigits(value).slice(0, 14);
  if (d.length <= 2) return d;
  if (d.length <= 5) return `${d.slice(0, 2)}.${d.slice(2)}`;
  if (d.length <= 8) return `${d.slice(0, 2)}.${d.slice(2, 5)}.${d.slice(5)}`;
  if (d.length <= 12) return `${d.slice(0, 2)}.${d.slice(2, 5)}.${d.slice(5, 8)}/${d.slice(8)}`;
  return `${d.slice(0, 2)}.${d.slice(2, 5)}.${d.slice(5, 8)}/${d.slice(8, 12)}-${d.slice(12)}`;
};
