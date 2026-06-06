export interface ActivityLogEntry {
  id: number;
  timestamp: string;
  channel: string;
  status: "sent" | "error" | "info" | "pending" | "cancelled";
  recipient: string;
  summary: string;
}
