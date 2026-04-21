import { type ReactNode, useEffect } from "react";
import { createPortal } from "react-dom";
import { ICONS } from "../utils/icons";
import { colors } from "../utils/colors";
import { formatBRL } from "../utils/formatters";
import { t } from "../i18n";
import type { TitleStatus, ContactStatus, ToastType, ActivityLogEntry } from "../types";

// ---- Loading ----
export const LoadingState = ({ label = "Carregando..." }: { label?: string }) => (
  <div className="flex items-center justify-center gap-3 py-10 text-sm text-text-muted">
    <span className="w-4 h-4 border-2 border-border-subtle-2 border-t-accent rounded-full animate-spin" />
    <span>{label}</span>
  </div>
);

// ---- Badge ----
const badgeConfig: Record<string, { label: string; cls: string }> = {
  open:      { label: t("badge.open"),      cls: "open" },
  overdue:   { label: t("badge.overdue"),   cls: "overdue" },
  sent:      { label: t("badge.sent"),      cls: "sent" },
  paid:      { label: t("badge.paid"),      cls: "paid" },
  pending:   { label: t("badge.pending"),   cls: "pending" },
  cancelled: { label: t("badge.cancelled"), cls: "cancelled" },
  complete:  { label: t("badge.complete"),  cls: "paid" },
  partial:   { label: t("badge.partial"),   cls: "pending" },
};

const badgeStyles: Record<string, { bg: string; text: string; dot: string }> = {
  open:      { bg: "bg-[#FDE0471F]", text: "text-[#FDE047]", dot: "bg-[#FDE047]" },
  overdue:   { bg: "bg-danger/12", text: "text-danger", dot: "bg-danger" },
  sent:      { bg: "bg-success/12", text: "text-success", dot: "bg-success" },
  paid:      { bg: "bg-success/12", text: "text-success", dot: "bg-success" },
  pending:   { bg: "bg-warn/12", text: "text-warn", dot: "bg-warn" },
  cancelled: { bg: "bg-text-muted/12", text: "text-text-muted", dot: "bg-text-muted" },
};

export const Badge = ({ status }: { status: TitleStatus | ContactStatus | string }) => {
  const config = badgeConfig[status] || { label: status, cls: "open" };
  const style = badgeStyles[config.cls] || badgeStyles.open;
  return (
    <span className={`inline-flex items-center gap-[5px] px-[9px] py-[3px] rounded-full text-[11px] font-bold ${style.bg} ${style.text}`}>
      <span className={`w-[5px] h-[5px] rounded-full shrink-0 ${style.dot}`} />
      {config.label}
    </span>
  );
};

// ---- ChannelPills ----
export const ChannelPills = ({ channels }: { channels: string[] }) => (
  <div className="flex gap-[5px]">
    {channels.includes("email") && (
      <span className="bg-accent/12 text-accent text-[10px] font-bold px-[7px] py-[2px] rounded-full">{ICONS.email} {t("channel.email")}</span>
    )}
    {channels.includes("wa") && (
      <span className="bg-wa/12 text-wa text-[10px] font-bold px-[7px] py-[2px] rounded-full">{ICONS.chat} {t("channel.wa")}</span>
    )}
    {channels.length === 0 && <span className="text-text-muted text-xs">{ICONS.emdash}</span>}
  </div>
);

// ---- Button ----
type ButtonVariant = "primary" | "secondary" | "danger" | "ghost";
interface ButtonProps {
  children: ReactNode;
  variant?: ButtonVariant;
  size?: "sm" | "md";
  onClick?: () => void;
  className?: string;
  disabled?: boolean;
  type?: "button" | "submit" | "reset";
}

const buttonVariants: Record<ButtonVariant, string> = {
  primary:   "bg-accent text-white hover:bg-[#B91C1C] hover:-translate-y-px",
  secondary: "bg-surface-2 text-text-secondary border border-border-subtle-2 hover:bg-surface-3 hover:text-text-primary",
  danger:    "bg-danger/10 text-danger border border-danger/30 hover:bg-danger/[0.16]",
  ghost:     "bg-transparent text-text-secondary hover:text-text-primary",
};

export const Button = ({ children, variant = "secondary", size = "md", onClick, className = "", disabled = false, type = "button" }: ButtonProps) => {
  const sizeClass = size === "sm" ? "px-3 py-1.5 text-xs" : "px-[18px] py-[9px] text-[13px]";
  return (
    <button
      type={type}
      disabled={disabled}
      className={`inline-flex items-center gap-[7px] border-none rounded-[9px] font-sans font-semibold transition-all duration-[170ms] whitespace-nowrap ${sizeClass} ${buttonVariants[variant]} ${disabled ? "opacity-50 cursor-not-allowed" : "cursor-pointer"} ${className}`}
      onClick={disabled ? undefined : onClick}
    >{children}</button>
  );
};

// ---- FormInput ----
interface FormInputProps extends React.InputHTMLAttributes<HTMLInputElement> {
  label?: string;
}

export const FormInput = ({ label, ...props }: FormInputProps) => (
  <div>
    {label && <label className="block text-[11px] font-bold tracking-[0.6px] uppercase text-text-muted mb-[5px]">{label}</label>}
    <input
      {...props}
      className="bg-surface-2 border border-border-subtle-2 rounded-lg px-[13px] py-[9px] text-[13px] text-text-primary outline-none w-full transition-[border-color] duration-[180ms] focus:border-accent focus:ring-3 focus:ring-accent/10"
    />
  </div>
);

// ---- FormSelect ----
interface FormSelectProps extends React.SelectHTMLAttributes<HTMLSelectElement> {
  label?: string;
  children: ReactNode;
}

export const FormSelect = ({ label, children, ...props }: FormSelectProps) => (
  <div>
    {label && <label className="block text-[11px] font-bold tracking-[0.6px] uppercase text-text-muted mb-[5px]">{label}</label>}
    <select
      className="bg-surface-2 border border-border-subtle-2 rounded-lg px-[13px] py-[9px] text-[13px] text-text-primary outline-none w-full appearance-none cursor-pointer bg-no-repeat bg-[right_12px_center]"
      style={{ backgroundImage: `url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='10' height='7' fill='none'%3E%3Cpath d='M1 1l4 4 4-4' stroke='%234d6380' stroke-width='1.5' stroke-linecap='round'/%3E%3C/svg%3E")` }}
      {...props}
    >{children}</select>
  </div>
);

// ---- CardHeader ----
export const CardHeader = ({ title, subtitle, right }: { title: ReactNode; subtitle?: string; right?: ReactNode }) => (
  <div className="flex items-center justify-between px-[22px] pt-[18px] pb-[14px] border-b border-border-subtle">
    <div>
      <div className="font-extrabold text-sm tracking-[-0.2px]">{title}</div>
      {subtitle && <div className="text-xs text-text-muted mt-[3px]">{subtitle}</div>}
    </div>
    {right}
  </div>
);

// ---- Toast ----
export const Toast = ({ message, type, visible }: { message: string; type: ToastType; visible: boolean }) => {
  const icons: Record<ToastType, string> = { success: ICONS.checkmark, info: ICONS.info, warn: ICONS.warning, error: ICONS.cross };
  const normalizedMessage = message.trimStart();
  const hasLeadingIcon = Object.values(ICONS).some(icon => normalizedMessage.startsWith(icon));

  return (
    <div className={`fixed bottom-6 right-6 z-[9999] bg-surface-2 border border-border-subtle-2 rounded-[10px] px-[18px] py-3 flex items-center gap-2.5 text-[13px] font-medium shadow-[0_12px_36px_rgba(0,0,0,0.5)] max-w-[340px] transition-transform duration-300 ease-[cubic-bezier(.4,0,.2,1)] ${visible ? "translate-x-0" : "translate-x-[140%]"}`}>
      {!hasLeadingIcon && <span className="text-[15px]">{icons[type] || ICONS.checkmark}</span>}
      {message}
    </div>
  );
};

// ---- Modal ----
interface ModalProps {
  open: boolean;
  onClose: () => void;
  title: string;
  children: ReactNode;
  footer?: ReactNode;
  maxWidth?: number;
}

export const Modal = ({ open, onClose, title, children, footer, maxWidth = 560 }: ModalProps) => {
  useEffect(() => {
    if (!open) return;

    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";

    return () => {
      document.body.style.overflow = previousOverflow;
    };
  }, [open]);

  if (!open) return null;

  return createPortal(
    <div
      onClick={e => e.target === e.currentTarget && onClose()}
      className="fixed inset-0 z-[1000] bg-black/75 backdrop-blur-sm overflow-y-auto"
    >
      <div className="min-h-full flex items-center justify-center p-4 md:p-6">
        <div
          className="bg-surface border border-border-subtle-2 rounded-2xl w-full max-h-[90vh] overflow-y-auto animate-slide-down shadow-[0_24px_64px_rgba(0,0,0,0.45)]"
          style={{ maxWidth }}
        >
          <div className="flex items-center justify-between px-6 py-5 border-b border-border-subtle sticky top-0 bg-surface z-10">
            <span className="font-extrabold text-base">{title}</span>
            <button onClick={onClose} className="bg-transparent border-none cursor-pointer text-text-muted text-2xl leading-none font-light">{ICONS.close}</button>
          </div>
          <div className="p-6">{children}</div>
          {footer && <div className="flex items-center justify-end gap-2.5 px-6 py-4 border-t border-border-subtle sticky bottom-0 bg-surface">{footer}</div>}
        </div>
      </div>
    </div>,
    document.body
  );
};

// ---- KpiCard ----
interface KpiCardProps {
  label: string;
  value: string;
  icon: string;
  delta: string;
  deltaDir: "up" | "down";
  accentColor: string;
  delay?: number;
}

export const KpiCard = ({ label, value, icon, delta, deltaDir, accentColor, delay = 0 }: KpiCardProps) => (
  <div className={`bg-surface border border-border-subtle rounded-[14px] overflow-hidden p-[22px] relative transition-all duration-200 hover:border-border-subtle-2 hover:-translate-y-0.5 ${delay ? `animate-fade-up-${delay}` : "animate-fade-up"}`}>
    <div className="absolute top-0 left-0 right-0 h-0.5" style={{ background: `linear-gradient(90deg, ${accentColor}, transparent)` }} />
    <div className="flex items-center justify-between mb-3.5">
      <span className="text-xs font-semibold text-text-secondary tracking-[0.2px]">{label}</span>
      <div className="w-[34px] h-[34px] rounded-[9px] flex items-center justify-center text-[15px]" style={{ background: `${accentColor}1a` }}>{icon}</div>
    </div>
    <div className="font-sans text-[28px] font-extrabold tracking-[-0.8px] mb-2.5 leading-none">{value}</div>
    <div className="text-[11.5px] text-text-muted flex items-center gap-[5px]">
      <span className={`font-bold ${deltaDir === "up" ? "text-success" : "text-danger"}`}>{deltaDir === "up" ? ICONS.arrowUp : ICONS.arrowDown} {delta}</span>
      {t("common.vsLastMonth")}
    </div>
  </div>
);

// ---- ChartTooltip ----
export const ChartTooltip = ({ active, payload, label }: any) => {
  if (!active || !payload?.length) return null;
  return (
    <div className="bg-surface-2 border border-border-subtle-2 rounded-[10px] px-3.5 py-2.5 text-xs">
      <div className="font-bold mb-2 text-text-secondary">{label}</div>
      {payload.map((entry: any, index: number) => (
        <div key={index} style={{ color: entry.color }} className="mb-[3px]">
          {entry.name}: <strong>{typeof entry.value === "number" && entry.value > 999 ? formatBRL(entry.value * 1000) : entry.value}</strong>
        </div>
      ))}
    </div>
  );
};

// ---- ActivityLog ----
export const ActivityLog = ({ logs = [] }: { logs?: ActivityLogEntry[] }) => {
  const getChannelIcon = (channel: string) => {
    const icons: Record<string, { symbol: string; label: string; color: string }> = {
      both:      { symbol: ICONS.link, label: "E-mail + WhatsApp", color: colors.accent },
      wa:        { symbol: ICONS.chat, label: "WhatsApp",  color: colors.wa },
      instagram: { symbol: ICONS.camera, label: "Instagram", color: "#E4405F" },
      email:     { symbol: ICONS.mail, label: "E-mail",    color: colors.accent },
      system:    { symbol: ICONS.clipboard, label: "Sistema", color: colors.text2 },
    };
    return icons[channel] || { symbol: ICONS.info, label: channel, color: colors.text2 };
  };

  return (
    <div className="bg-surface border border-border-subtle rounded-[14px] overflow-hidden flex flex-col h-full">
      <div className="px-[22px] pt-[18px] pb-[14px] border-b border-border-subtle flex items-center justify-between">
        <div>
          <div className="font-extrabold text-sm tracking-[-0.2px] flex items-center gap-2">{ICONS.email} {t("activityLog.title")}</div>
          <div className="text-xs text-text-muted mt-[3px]">{t("activityLog.subtitle")}</div>
        </div>
        <span className="text-xs font-semibold text-text-secondary">{logs.length} {t("common.records")}</span>
      </div>

      <div className="flex-1 overflow-y-auto p-3 flex flex-col gap-2.5">
        {logs.length === 0 ? (
          <div className="flex items-center justify-center h-full text-text-muted text-[13px]">{t("activityLog.noLogs")}</div>
        ) : (
          logs.map((log) => {
            const channel = getChannelIcon(log.channel);
            const isError = log.status === "error";
            const isInfo = log.status === "info";
            return (
              <div
                key={log.id}
                className="p-3 bg-surface-2 rounded-[9px] flex gap-3 items-start transition-all duration-200"
                style={{
                  border: `1px solid ${isError ? "rgba(220,38,38,0.2)" : isInfo ? "rgba(148,163,184,0.26)" : colors.border2}`,
                  borderLeft: `4px solid ${isError ? colors.danger : isInfo ? colors.text2 : channel.color}`,
                }}
              >
                <div
                  className="w-9 h-9 rounded-lg flex items-center justify-center text-base shrink-0"
                  style={{
                    background: isError ? "rgba(220,38,38,0.12)" : isInfo ? "rgba(148,163,184,0.12)" : `${channel.color}22`,
                    border: `1px solid ${isError ? "rgba(220,38,38,0.2)" : isInfo ? "rgba(148,163,184,0.24)" : `${channel.color}44`}`,
                  }}
                >
                  {isError ? ICONS.cross : isInfo ? ICONS.info : channel.symbol}
                </div>

                <div className="flex-1 min-w-0">
                  <div className="flex items-center justify-between mb-[5px]">
                    <div className="flex items-center gap-2">
                      <span className="text-[11px] font-bold text-text-muted tracking-[0.5px]">{log.recipient}</span>
                      <span className="text-[11px] text-text-muted bg-surface-3 px-1.5 py-px rounded">{channel.label}</span>
                    </div>
                    <span className="text-[11px] text-text-muted whitespace-nowrap">{log.timestamp}</span>
                  </div>
                  <div className="text-xs text-text-secondary leading-[1.4] mb-1.5 line-clamp-2">{log.summary}</div>
                  <div className={`inline-flex items-center gap-[5px] px-2 py-[3px] rounded-[5px] text-[10px] font-bold ${isError ? "bg-danger/12 text-danger" : isInfo ? "bg-surface-3 text-text-secondary" : "bg-success/12 text-success"}`}>
                    <span className={`w-1 h-1 rounded-full inline-block ${isError ? "bg-danger" : isInfo ? "bg-text-secondary" : "bg-success"}`} />
                    {log.status === "error" ? t("activityLog.statusError") : log.status === "info" ? "HISTÓRICO" : t("activityLog.statusSent")}
                  </div>
                </div>
              </div>
            );
          })
        )}
      </div>
    </div>
  );
};
