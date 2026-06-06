import React, { type ComponentType, SVGProps } from "react";
import {
  AlertTriangle,
  ArrowDown,
  ArrowUp,
  BarChart3,
  Bell,
  Camera,
  Check,
  CheckCircle2,
  ChevronDown,
  Circle,
  ClipboardList,
  Clock3,
  Edit3,
  Eye,
  FileText,
  FlaskConical,
  FolderOpen,
  Gift,
  Hourglass,
  Info,
  Link2,
  Mail,
  MailCheck,
  MessageCircle,
  Moon,
  Package,
  Pencil,
  PieChart,
  Plus,
  RefreshCw,
  Repeat2,
  Satellite,
  Search,
  Settings,
  Square,
  Sun,
  Trophy,
  Upload,
  Users,
  Wallet,
  X,
  XCircle,
} from "lucide-react";

const iconMap = {
  dashboard: BarChart3,
  analytics: PieChart,
  document: FileText,
  upload: Upload,
  users: Users,
  settings: Settings,
  mail: Mail,
  link: Link2,
  chat: MessageCircle,
  camera: Camera,
  money: Wallet,
  warning: AlertTriangle,
  warningLight: AlertTriangle,
  checkmark: CheckCircle2,
  email: Mail,
  bell: Bell,
  dot: Circle,
  arrowUp: ArrowUp,
  arrowDown: ArrowDown,
  close: X,
  cross: XCircle,
  info: Info,
  mailbox: MailCheck,
  folder: FolderOpen,
  outbox: Upload,
  clipboard: ClipboardList,
  trophy: Trophy,
  satellite: Satellite,
  search: Search,
  downArrow: ChevronDown,
  calendar: Clock3,
  package: Package,
  refresh: RefreshCw,
  repeat: Repeat2,
  timer: Clock3,
  stop: Square,
  floppy: Check,
  abcLetters: Pencil,
  testTube: FlaskConical,
  eye: Eye,
  envelope: Mail,
  emdash: Square,
  party: Gift,
  pencil: Edit3,
  plus: Plus,
  sun: Sun,
  moon: Moon,
  hourglass: Hourglass,
} satisfies Record<string, ComponentType<SVGProps<SVGSVGElement>>>;

export type IconKey = keyof typeof iconMap;

export const Icon = ({
  name,
  size = "1em",
  strokeWidth = 2,
  className = "",
  ...props
}: {
  name: IconKey;
  size?: number | string;
  strokeWidth?: number;
  className?: string;
} & SVGProps<SVGSVGElement>) => {
  const Component = iconMap[name];
  return (
    <Component
      aria-hidden="true"
      focusable="false"
      width={size}
      height={size}
      strokeWidth={strokeWidth}
      className={`inline-block shrink-0 align-[-0.125em] ${className}`}
      {...props}
    />
  );
};

export const ICONS = Object.fromEntries(
  Object.keys(iconMap).map(key => [key, <Icon key={key} name={key as IconKey} />])
) as Record<IconKey, any>;
