export const formatBRL = (value: number) =>
  value >= 1000000
    ? `R$ ${(value / 1000000).toFixed(1)}M`
    : value >= 1000
    ? `R$ ${(value / 1000).toFixed(0)}K`
    : `R$ ${value.toLocaleString("pt-BR")}`;

export const formatBRLFull = (value: number) =>
  `R$ ${value.toLocaleString("pt-BR", { minimumFractionDigits: 2 })}`;

export const onlyDigits = (value: string) => value.replace(/\D/g, "");

export const formatCnpj = (value: string) => {
  const d = onlyDigits(value).slice(0, 14);
  if (d.length <= 2) return d;
  if (d.length <= 5) return `${d.slice(0, 2)}.${d.slice(2)}`;
  if (d.length <= 8) return `${d.slice(0, 2)}.${d.slice(2, 5)}.${d.slice(5)}`;
  if (d.length <= 12) return `${d.slice(0, 2)}.${d.slice(2, 5)}.${d.slice(5, 8)}/${d.slice(8)}`;
  return `${d.slice(0, 2)}.${d.slice(2, 5)}.${d.slice(5, 8)}/${d.slice(8, 12)}-${d.slice(12)}`;
};
