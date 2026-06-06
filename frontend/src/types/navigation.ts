export interface NavItem {
  id: string;
  icon: string;
  label: string;
  badge?: number;
  badgeType?: "danger" | "warn";
}

export interface NavSection {
  section: string;
  items: NavItem[];
}

export type PageId =
  | "dashboard"
  | "analytics"
  | "titles"
  | "import"
  | "contacts"
  | "sequence"
  | "templates"
  | "integration"
  | "workers"
  | "tenants";
