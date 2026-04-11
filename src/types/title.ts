export type TitleStatus = "open" | "overdue" | "sent" | "paid" | "pending" | "cancelled";

export interface Title {
  id: string;
  client: string;
  cnpj: string;
  dueDate: string;
  amount: number;
  status: TitleStatus;
  channels: string[];
  lastAction: string;
}
