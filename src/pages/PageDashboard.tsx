import { useEffect, useMemo, useRef, useState } from "react";
import {
  BarChart, Bar, AreaChart, Area,
  XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer
} from "recharts";
import { ICONS } from "../utils/icons";
import { colors } from "../utils/colors";
import { t } from "../i18n";
import { formatBRL, formatBRLFull } from "../utils/formatters";
import { KpiCard, CardHeader, ChartTooltip, Button, ActivityLog } from "../components/UI";
import {
  getDashboardSummary, getDashboardFunnel, getDashboardAging,
  getDashboardTopDefaulters, getDashboardSendsPerDay,
  getDashboardChannelMetrics, getDashboardActivityLog, getDashboardStatusBreakdown,
  type StoredSession,
} from "../services/api";
import type { ShowToast } from "../types";
import type { ActivityLogEntry } from "../types/activity";

function safePercent(n: number, d: number) {
  if (d <= 0) return 0;
  return Number(((n / d) * 100).toFixed(1));
}

function toBrDayLabel(isoDay: string) {
  const parsed = new Date(`${isoDay}T00:00:00`);
  if (Number.isNaN(parsed.getTime())) return isoDay;
  return parsed.toLocaleDateString("pt-BR", { day: "2-digit", month: "2-digit" });
}

export const PageDashboard = ({
  showToast,
  session,
  selectedTenantId,
  onViewAllDefaulters,
}: {
  showToast: ShowToast;
  session: StoredSession;
  selectedTenantId?: string;
  onViewAllDefaulters?: () => void;
}) => {
  const [loading, setLoading] = useState(true);
  const [summary, setSummary] = useState({ totalReceivable: 0, totalOverdue: 0, totalPaid: 0, recoveryRate: 0 });
  const [statusBreakdown, setStatusBreakdown] = useState({ open: 0, pendingData: 0, overdue: 0, paid: 0, cancelled: 0 });
  const [funnel, setFunnel] = useState<{ month: string; receivable: number; overdue: number; recovered: number }[]>([]);
  const [aging, setAging] = useState<{ label: string; value: number; color: string }[]>([]);
  const [topDefaulters, setTopDefaulters] = useState<{ rank: number; name: string; cnpj: string; value: number }[]>([]);
  const [sends, setSends] = useState<{ day: string; email: number; wa: number }[]>([]);
  const [channelMetrics, setChannelMetrics] = useState({ emailSent: 0, emailDelivered: 0, emailViewed: 0, whatsAppSent: 0, whatsAppDelivered: 0, whatsAppViewed: 0 });
  const [activityLogs, setActivityLogs] = useState<ActivityLogEntry[]>([]);
  const partialFailureWarnedRef = useRef(false);
  const fullFailureWarnedRef = useRef(false);

  const isMaster = session.role === "Master";

  useEffect(() => {
    let cancelled = false;

    async function load() {
      setLoading(true);
      try {
        const tid = isMaster ? undefined : session.tenantId || undefined;
        const effectiveTenantId = isMaster ? (selectedTenantId || undefined) : tid;
        const results = await Promise.allSettled([
          getDashboardSummary(effectiveTenantId),
          getDashboardStatusBreakdown(effectiveTenantId),
          getDashboardFunnel(effectiveTenantId),
          getDashboardAging(effectiveTenantId),
          getDashboardTopDefaulters(effectiveTenantId),
          getDashboardSendsPerDay(effectiveTenantId),
          getDashboardChannelMetrics(effectiveTenantId),
          getDashboardActivityLog(effectiveTenantId),
        ]);
        if (cancelled) return;

        const [s, sb, f, a, top, snds, ch, act] = results;
        const failed = results.filter(r => r.status === "rejected").length;

        setSummary(s.status === "fulfilled"
          ? s.value
          : { totalReceivable: 0, totalOverdue: 0, totalPaid: 0, recoveryRate: 0 });

        setStatusBreakdown(sb.status === "fulfilled"
          ? sb.value
          : { open: 0, pendingData: 0, overdue: 0, paid: 0, cancelled: 0 });

        setFunnel(f.status === "fulfilled"
          ? f.value.items.map(i => ({ month: i.month, receivable: i.receivable, overdue: i.overdue, recovered: i.recovered }))
          : []);

        setAging(a.status === "fulfilled"
          ? a.value.items.map(i => ({ label: i.range, value: i.value, color: i.color }))
          : []);

        setTopDefaulters(top.status === "fulfilled"
          ? top.value.items.map((i, idx) => ({ rank: idx + 1, name: i.clientName, cnpj: i.taxId, value: i.totalAmount }))
          : []);

        setSends(snds.status === "fulfilled"
          ? snds.value.items.map(i => ({ day: toBrDayLabel(i.day), email: i.emailCount, wa: i.whatsAppCount }))
          : []);

        setChannelMetrics(ch.status === "fulfilled"
          ? ch.value
          : { emailSent: 0, emailDelivered: 0, emailViewed: 0, whatsAppSent: 0, whatsAppDelivered: 0, whatsAppViewed: 0 });

        setActivityLogs(act.status === "fulfilled"
          ? act.value.items.map((i, idx) => {
              const normalizedChannel = i.channel.toLowerCase();

              return {
                id: idx + 1,
                timestamp: new Date(i.timestamp).toLocaleTimeString("pt-BR", { hour12: false }),
                channel: normalizedChannel.includes("both") || normalizedChannel.includes("ambos")
                  ? "both"
                  : normalizedChannel.includes("whatsapp")
                    ? "wa"
                    : normalizedChannel.includes("email")
                      ? "email"
                      : "system",
                status: i.status.toLowerCase().includes("error")
                  ? "error"
                  : i.status.toLowerCase().includes("info")
                    ? "info"
                    : "sent",
                recipient: i.recipient,
                summary: i.summary,
              };
            })
          : []);

        if (failed === results.length)
        {
          if (!fullFailureWarnedRef.current)
          {
            showToast(`${ICONS.cross} ${t("dashboardPage.errors.load")}`, "error");
            fullFailureWarnedRef.current = true;
          }
        }
        else
        {
          fullFailureWarnedRef.current = false;
          if (failed > 0 && !partialFailureWarnedRef.current)
          {
            showToast(`${ICONS.warning} ${t("dashboardPage.errors.partial")}`, "warn");
            partialFailureWarnedRef.current = true;
          }

          if (failed === 0)
            partialFailureWarnedRef.current = false;
        }
      } catch {
        if (!cancelled) showToast(`${ICONS.cross} ${t("dashboardPage.errors.load")}`, "error");
      } finally {
        if (!cancelled) setLoading(false);
      }
    }

    void load();
    const refresh = window.setInterval(() => { void load(); }, 30000);

    return () => {
      cancelled = true;
      window.clearInterval(refresh);
    };
  }, [showToast, isMaster, session.tenantId, selectedTenantId]);

  const maxAging = useMemo(() => Math.max(...aging.map(i => i.value), 1), [aging]);
  const emailDeliveryRate = safePercent(channelMetrics.emailDelivered, channelMetrics.emailSent);
  const emailReadRate     = safePercent(channelMetrics.emailViewed, channelMetrics.emailSent);
  const waDeliveryRate    = safePercent(channelMetrics.whatsAppDelivered, channelMetrics.whatsAppSent);
  const waReadRate        = safePercent(channelMetrics.whatsAppViewed, channelMetrics.whatsAppSent);
  const areaData = sends.map(i => ({ month: i.day, email: i.email, wa: i.wa }));
  const receivableColor = "#FDE047";
  const overdueColor = colors.accent;
  const totalAnalyzedTitles = statusBreakdown.open + statusBreakdown.pendingData + statusBreakdown.overdue + statusBreakdown.paid;
  const paymentRate = totalAnalyzedTitles > 0
    ? Number(((statusBreakdown.paid / totalAnalyzedTitles) * 100).toFixed(1))
    : Number(summary.recoveryRate.toFixed(1));
  const receivableShare = safePercent(summary.totalReceivable, summary.totalReceivable + summary.totalPaid);
  const overdueShare = safePercent(summary.totalOverdue, summary.totalReceivable || 1);
  const paidShare = safePercent(summary.totalPaid, summary.totalReceivable + summary.totalPaid);
  const pendingColor = "#F97316";

  const criticalityItems = [
    { label: t("dashboard.statusOverdue"), value: statusBreakdown.overdue, tone: t("dashboardPage.tones.high"), color: overdueColor, bg: "bg-danger/10" },
    { label: t("dashboard.statusPending"), value: statusBreakdown.pendingData, tone: t("dashboardPage.tones.medium"), color: pendingColor, bg: "bg-orange-500/10" },
    { label: t("dashboard.statusOpen"), value: statusBreakdown.open, tone: t("dashboardPage.tones.monitor"), color: receivableColor, bg: "bg-warn/10" },
    { label: t("dashboard.statusPaid"), value: statusBreakdown.paid, tone: t("dashboardPage.tones.low"), color: colors.success, bg: "bg-success/10" },
    { label: t("dashboard.statusCancelled"), value: statusBreakdown.cancelled, tone: t("dashboardPage.tones.neutral"), color: colors.text3, bg: "bg-surface-2" },
  ];

  return (
    <div className="animate-fade-up">
      {isMaster && (
        <div className="mb-4 px-4 py-2.5 bg-accent/8 border border-accent/20 rounded-[10px] text-sm text-accent font-semibold flex items-center gap-2">
          {ICONS.dashboard} {selectedTenantId ? t("dashboardPage.master.selectedTenant") : t("dashboardPage.master.allTenants")}
        </div>
      )}

      {/* KPIs */}
      <div className="grid grid-cols-4 gap-4 mb-6">
        <KpiCard label={t("dashboard.totalReceivable")}  value={formatBRL(summary.totalReceivable)} icon={ICONS.money}     delta={loading ? "..." : `${receivableShare}% ${t("dashboardPage.kpi.receivableShare")}`} deltaDir="up"   accentColor={receivableColor} delay={1} />
        <KpiCard label={t("dashboard.totalOverdue")}     value={formatBRL(summary.totalOverdue)}    icon={ICONS.warning}   delta={loading ? "..." : `${overdueShare}% ${t("dashboardPage.kpi.overdueShare")}`}  deltaDir="down" accentColor={overdueColor} delay={2} />
        <KpiCard label={t("dashboard.totalPaid")}        value={formatBRL(summary.totalPaid)}       icon={ICONS.checkmark} delta={loading ? "..." : `${paidShare}% ${t("dashboardPage.kpi.paidShare")}`} deltaDir="up"   accentColor={colors.success} delay={3} />
        <KpiCard label={t("dashboard.recoveryRate")}     value={`${paymentRate.toFixed(1)}%`} icon={ICONS.dashboard} delta={t("dashboardPage.kpi.current")} deltaDir="up" accentColor={colors.success} delay={4} />
      </div>

      <div className="bg-surface border border-border-subtle rounded-[14px] overflow-hidden mb-6">
        <CardHeader title={t("dashboard.criticalityMatrix")} subtitle={t("dashboard.criticalitySubtitle")} />
        <div className="p-5 grid grid-cols-5 gap-3">
          {criticalityItems.map(item => (
            <div key={item.label} className={`rounded-[10px] border border-border-subtle p-3 ${item.bg}`}>
              <div className="text-[11px] text-text-secondary mb-1">{item.label}</div>
              <div className="text-xl font-extrabold" style={{ color: item.color }}>{item.value.toLocaleString("pt-BR")}</div>
              <div className="text-[11px] font-semibold mt-1" style={{ color: item.color }}>{item.tone}</div>
            </div>
          ))}
        </div>
      </div>

      {/* Charts row */}
      <div className="grid grid-cols-[1.7fr_1fr] gap-4 mb-6">
        <div className="bg-surface border border-border-subtle rounded-[14px] overflow-hidden">
          <CardHeader title={t("dashboard.recoveryFunnel")} subtitle={t("dashboard.monthlyEvolution")} />
          <div className="p-5">
            <ResponsiveContainer width="100%" height={210}>
              <BarChart data={funnel} barGap={3}>
                <CartesianGrid strokeDasharray="3 3" stroke={colors.border} />
                <XAxis dataKey="month" tick={{ fill: colors.text3, fontSize: 11 }} axisLine={false} tickLine={false} />
                <YAxis tick={{ fill: colors.text3, fontSize: 11 }} axisLine={false} tickLine={false} />
                <Tooltip content={<ChartTooltip />} cursor={{ fill: "rgba(220, 38, 38, 0.08)" }} />
                <Legend wrapperStyle={{ fontSize: 11, paddingTop: 10 }} />
                <Bar dataKey="receivable" name={t("dashboard.receivable")} fill={receivableColor} radius={[4,4,0,0]} fillOpacity={0.7} />
                <Bar dataKey="overdue"    name={t("dashboard.overdue")}    fill={overdueColor}  radius={[4,4,0,0]} fillOpacity={0.7} />
                <Bar dataKey="recovered"  name={t("dashboard.totalPaid")}  fill={colors.success} radius={[4,4,0,0]} />
              </BarChart>
            </ResponsiveContainer>
          </div>
        </div>

        <div className="bg-surface border border-border-subtle rounded-[14px] overflow-hidden">
          <CardHeader title={t("dashboard.agingList")} subtitle={t("dashboard.agingSubtitle")} />
          <div className="p-5 flex flex-col gap-3.5">
            {aging.length === 0 && !loading && (
              <div className="text-xs text-text-muted">{t("dashboardPage.aging.empty")}</div>
            )}
            {aging.map(item => (
              <div key={item.label}>
                <div className="flex justify-between mb-[7px] text-[12.5px]">
                  <span className="text-text-secondary">{item.label}</span>
                  <span className="font-bold" style={{ color: item.color }}>{formatBRL(item.value)}</span>
                </div>
                <div className="h-[5px] bg-surface-3 rounded-[3px] overflow-hidden">
                  <div
                    className="h-full rounded-[3px] transition-[width] duration-1000"
                    style={{ width: `${(item.value / maxAging) * 100}%`, background: item.color }}
                  />
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>

      {/* Bottom row */}
      <div className="grid grid-cols-2 gap-4">
        {/* Top defaulters */}
        <div className="bg-surface border border-border-subtle rounded-[14px] overflow-hidden">
          <CardHeader
            title={<>{ICONS.trophy} {t("dashboard.topDefaulters")}</>}
            subtitle={t("dashboard.byOutstandingAmount")}
            right={<Button variant="secondary" size="sm" onClick={onViewAllDefaulters} disabled={!onViewAllDefaulters}>{t("common.viewAll")}</Button>}
          />
          <div className="px-5 py-3.5 flex flex-col gap-[9px]">
            {topDefaulters.length === 0 && !loading && (
              <div className="text-xs text-text-muted">{t("dashboardPage.defaulters.empty")}</div>
            )}
            {topDefaulters.map(item => (
              <div key={item.rank} className="flex items-center gap-3 bg-surface-2 rounded-[9px] px-[13px] py-[10px] border border-border-subtle">
                <span className={`font-extrabold text-sm w-[22px] text-center ${item.rank <= 2 ? "text-accent" : "text-text-muted"}`}>{item.rank}</span>
                <div className="flex-1 min-w-0">
                  <div className="text-[13px] font-medium whitespace-nowrap overflow-hidden text-ellipsis">{item.name}</div>
                  <div className="text-[11px] text-text-muted mt-px">{item.cnpj}</div>
                </div>
                <span className="font-extrabold text-[13px] text-danger whitespace-nowrap">{formatBRLFull(item.value)}</span>
              </div>
            ))}
          </div>
        </div>

        {/* Channel effectiveness */}
        <div className="bg-surface border border-border-subtle rounded-[14px] overflow-hidden">
          <CardHeader title={<>{ICONS.satellite} {t("dashboard.channelEffectiveness")}</>} subtitle={t("dashboard.engagementMetrics")} />
          <div className="p-5">
            <div className="grid grid-cols-2 gap-3 mb-5">
              {[
                { name: t("channel.email"),   icon: ICONS.email, color: colors.accent, sent: channelMetrics.emailSent,     delivery: emailDeliveryRate, read: emailReadRate, readLabel: t("dashboard.opened") },
                { name: t("channel.whatsapp"), icon: ICONS.chat,  color: colors.wa,    sent: channelMetrics.whatsAppSent,   delivery: waDeliveryRate,    read: waReadRate,    readLabel: t("dashboard.read")   },
              ].map(ch => (
                <div key={ch.name} className="bg-surface-2 rounded-[10px] p-3.5 border border-border-subtle">
                  <div className="flex items-center gap-[7px] mb-3.5 font-bold text-[13px]">
                    <span>{ch.icon}</span> {ch.name}
                  </div>
                  {[
                    { label: t("dashboard.sent"),      value: ch.sent.toLocaleString("pt-BR"), bar: null as number | null },
                    { label: t("dashboard.delivered"), value: `${ch.delivery}%`,               bar: ch.delivery },
                    { label: ch.readLabel,             value: `${ch.read}%`,                   bar: ch.read    },
                  ].map(row => (
                    <div key={row.label} className="mb-2.5">
                      <div className={`flex justify-between text-xs ${row.bar !== null ? "mb-[5px]" : ""}`}>
                        <span className="text-text-secondary">{row.label}</span>
                        <span className="font-bold" style={{ color: row.bar !== null ? ch.color : colors.text }}>{row.value}</span>
                      </div>
                      {row.bar !== null && (
                        <div className="h-[3px] bg-surface-3 rounded-sm">
                          <div className="h-full rounded-sm" style={{ width: `${Math.min(row.bar, 100)}%`, background: ch.color }} />
                        </div>
                      )}
                    </div>
                  ))}
                </div>
              ))}
            </div>

            <ResponsiveContainer width="100%" height={120}>
              <AreaChart data={areaData}>
                <CartesianGrid strokeDasharray="3 3" stroke={colors.border} />
                <XAxis dataKey="month" tick={{ fill: colors.text3, fontSize: 10 }} axisLine={false} tickLine={false} />
                <YAxis hide />
                <Tooltip content={<ChartTooltip />} />
                <Area type="monotone" dataKey="email" name={t("channel.email")}    stroke={colors.accent} fill={`${colors.accent}22`} strokeWidth={2} />
                <Area type="monotone" dataKey="wa"    name={t("channel.whatsapp")}  stroke={colors.wa}     fill={`${colors.wa}22`}     strokeWidth={2} />
              </AreaChart>
            </ResponsiveContainer>
          </div>
        </div>
      </div>

      {/* Activity log */}
      <div className="mt-6">
        <ActivityLog logs={activityLogs} />
      </div>
    </div>
  );
};
