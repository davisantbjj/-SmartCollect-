export type ContactStatus = "complete" | "pending" | "partial";

export interface Contact {
  company: string;
  cnpj: string;
  name: string;
  type: string;
  email: string;
  phone: string;
  titleCount: number;
  status: ContactStatus;
}
