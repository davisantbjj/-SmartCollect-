export interface ActivityLogEntry {
  id: number;
  timestamp: string;
  channel: string;
  status: "sent" | "error";
  recipient: string;
  summary: string;
}
