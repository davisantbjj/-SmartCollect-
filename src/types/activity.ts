export interface ActivityLogEntry {
  id: number;
  timestamp: string;
  channel: string;
  status: "sent" | "error" | "info";
  recipient: string;
  summary: string;
}
