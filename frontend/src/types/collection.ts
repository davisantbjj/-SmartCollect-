export interface Template {
  channel: "email" | "wa" | "both";
  name: string;
  trigger: string;
  preview: string;
  selected: boolean;
}

export interface Step {
  label: string;
  color: string;
  title: string;
  description: string;
  channels: string[];
}
