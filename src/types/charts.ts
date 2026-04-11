export interface FunnelData {
  month: string;
  receivable: number;
  overdue: number;
  recovered: number;
}

export interface SendsData {
  day: string;
  email: number;
  wa: number;
}

export interface StatusPieData {
  name: string;
  value: number;
  color: string;
}

export interface AgingData {
  range: string;
  value: number;
  color: string;
}
