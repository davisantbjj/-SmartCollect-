import { useEffect, useMemo, useState } from "react";
import {
  BarChart, Bar, PieChart, Pie, Cell,
  XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer, ComposedChart, Line
} from "recharts";
import { ICONS } from "../utils/icons";
import { colors } from "../utils/colors";
import { CardHeader, ChartTooltip } from "../components/UI";
import { t } from "../i18n";
import {
  ApiError,
  getDashboardSummary,
  getDashboardStatusBreakdown,
  getDashboardSendsPerDay,
  getDashboardChannelMetrics,
  getDashboardCriticalMetrics,
  type StoredSession,
} from "../services/api";
import type { ShowToast } from "../types";

function safePercent(n: number, d: number) {
  if (d <= 0) return 0;
  return Number(((n / d) * 100).toFixed(1));
}

function toBrDayLabel(isoDay: string) {
  const parsed = new Date(`${isoDay}T00:00:00`);
  if (Number.isNaN(parsed.getTime())) return isoDay;
  return parsed.toLocaleDateString("pt-BR", { day: "2-digit", month: "2-digit" });
}

export const PageAnalytics = ({
  showToast,
  session,
  selectedTenantId,
  isDark,
}: {
  showToast: ShowToast;
  session: StoredSession;
  selectedTenantId?: string;
  isDark: boolean;
}) => {
  const [loading, setLoading] = useState(true);
  const [summary, setSummary] = useState({ totalReceivable: 0, totalOverdue: 0, totalPaid: 0, recoveryRate: 0 });
  const [statusBreakdown, setStatusBreakdown] = useState({ open: 0, pendingData: 0, overdue: 0, paid: 0, cancelled: 0 });
  const [sends, setSends] = useState<{ day: string; email: number; wa: number }[]>([]);
  const [channel, setChannel] = useState({ emailSent: 0, emailDelivered: 0, emailViewed: 0, whatsAppSent: 0, whatsAppDelivered: 0, whatsAppViewed: 0 });
  const [critical, setCritical] = useState({ criticalTitles: 0, overdueBaseTitles: 0, recoveredTitles: 0, recoveryRate: 0, trend: [] as { month: string; recoveryRate: number; recoveredTitles: number; overdueBaseTitles: number }[] });
  const receivableColor = "#FDE047";
  const paymentColor = colors.success;
  const overdueColor = colors.accent;
  const deliveryColor = "#4FC3F7";
  const recoveredColor = colors.success;

  const isMaster = session.role === "Master";

  useEffect(() => {
    let cancelled = false;

    async function load() {
      try {
        setLoading(true);
        const tenantId = isMaster ? (selectedTenantId || undefined) : (session.tenantId || undefined);
        const [s, sb, snd, ch, cm] = await Promise.all([
          getDashboardSummary(tenantId),
          getDashboardStatusBreakdown(tenantId),
          getDashboardSendsPerDay(tenantId),
          getDashboardChannelMetrics(tenantId),
          getDashboardCriticalMetrics(tenantId),
        ]);

        if (cancelled) return;

        setSummary(s);
        setStatusBreakdown(sb);
        setSends(snd.items.map(i => ({ day: toBrDayLabel(i.day), email: i.emailCount, wa: i.whatsAppCount })));
        setChannel(ch);
        setCritical({
          ...cm,
          trend: cm.trend.map(item => ({
            ...item,
            month: `${item.month.slice(5, 7)}/${item.month.slice(2, 4)}`,
          })),
        });
      } catch (err) {
        const msg = err instanceof ApiError ? err.message : t("analyticsPage.errors.load");
        if (!cancelled) showToast(`${msg}`, "error");
      } finally {
        if (!cancelled) setLoading(false);
      }
    }

    void load();
    return () => { cancelled = true; };
  }, [showToast, isMaster, selectedTenantId, session.tenantId]);

  const statusPie = useMemo(() => {
    const total = Math.max(statusBreakdown.open + statusBreakdown.pendingData + statusBreakdown.overdue + statusBreakdown.paid + statusBreakdown.cancelled, 1);
    return [
      { name: t("dashboard.statusOpen"), value: statusBreakdown.open, color: receivableColor },
      { name: t("dashboard.statusPending"), value: statusBreakdown.pendingData, color: "#F97316" },
      { name: t("dashboard.statusOverdue"), value: statusBreakdown.overdue, color: overdueColor },
      { name: t("dashboard.statusPaid"), value: statusBreakdown.paid, color: colors.success },
      { name: t("dashboard.statusCancelled"), value: statusBreakdown.cancelled, color: colors.text3 },
    ];
  }, [statusBreakdown]);

  const totalOperational = statusBreakdown.open + statusBreakdown.pendingData + statusBreakdown.overdue + statusBreakdown.paid;
  const totalTitles = totalOperational + statusBreakdown.cancelled;
  const criticalTitles = critical.criticalTitles;
  const criticalShare = safePercent(criticalTitles, totalTitles || 1);
  const criticalColor = criticalShare >= 30 ? "#FDD835" : "#FF9800";
  const overduePortfolioRate = safePercent(summary.totalOverdue, summary.totalReceivable || 1);
  const totalSent = channel.emailSent + channel.whatsAppSent;
  const totalDelivered = channel.emailDelivered + channel.whatsAppDelivered;
  const avgDeliveryRate = safePercent(totalDelivered, totalSent || 1);
  const emailDeliveryRate = safePercent(channel.emailDelivered, channel.emailSent || 1);
  const emailReadRate = safePercent(channel.emailViewed, channel.emailSent || 1);
  const waDeliveryRate = safePercent(channel.whatsAppDelivered, channel.whatsAppSent || 1);
  const waReadRate = safePercent(channel.whatsAppViewed, channel.whatsAppSent || 1);
  const paymentRate = totalOperational > 0
    ? Number(((statusBreakdown.paid / totalOperational) * 100).toFixed(1))
    : Number(summary.recoveryRate.toFixed(1));
  const chartThemeKey = isDark ? "dark" : "light";
  const gridStroke = isDark ? "rgba(255, 255, 255, 0.2)" : colors.border2;
  const recoveryTrend = useMemo(() => (
    critical.trend.map(item => {
      const recoveredRate = safePercent(item.recoveredTitles, item.overdueBaseTitles);
      return {
        month: item.month,
        recoveredRate,
        remainingRate: Math.max(0, 100 - recoveredRate),
      };
    })
  ), [critical.trend]);

  return (
    <div className="animate-fade-up">
      {isMaster && (
        <div className="mb-4 px-4 py-2.5 bg-accent/8 border border-accent/20 rounded-[10px] text-sm text-accent font-semibold flex items-center gap-2">
          {ICONS.analytics} {selectedTenantId ? t("analyticsPage.master.selectedTenant") : t("analyticsPage.master.allTenants")}
        </div>
      )}

      <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-5 gap-3.5 mb-6">
        {[
          { value: `${paymentRate.toFixed(1)}%`, label: t("analytics.recoveryRate"), subtitle: `${statusBreakdown.paid.toLocaleString("pt-BR")} ${t("analyticsPage.metrics.paidOf")} ${totalOperational.toLocaleString("pt-BR")}`, color: paymentColor },
          { value: `${criticalTitles.toLocaleString("pt-BR")}`, label: t("analytics.criticalTitles"), subtitle: `${criticalShare}% ${t("analyticsPage.metrics.ofTotalTitles")} • ${t("analyticsPage.metrics.criticalRule")}`, color: criticalColor },
          { value: new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL", maximumFractionDigits: 0 }).format(summary.totalOverdue), label: t("analytics.overdueAmount"), subtitle: `${overduePortfolioRate}% ${t("analyticsPage.metrics.overdueShare")}`, color: overdueColor },
          { value: `${critical.recoveredTitles.toLocaleString("pt-BR")}`, label: t("analytics.recovered"), subtitle: `${critical.overdueBaseTitles.toLocaleString("pt-BR")} ${t("analyticsPage.metrics.recoveredOfOverdue")}`, color: recoveredColor },
          { value: `${avgDeliveryRate.toFixed(1)}%`, label: t("analytics.avgDeliveryRate"), subtitle: `${totalDelivered.toLocaleString("pt-BR")} ${t("analyticsPage.metrics.deliveredOf")} ${totalSent.toLocaleString("pt-BR")} ${t("analyticsPage.metrics.delivered")}`, color: deliveryColor },
        ].map((metric, index) => (
          <div key={index} className="bg-surface border border-border-subtle rounded-[14px] overflow-hidden p-[22px] text-center">
            <div className="font-extrabold text-[30px] tracking-[-1px] mb-[5px]" style={{ color: metric.color }}>{metric.value}</div>
            <div className="text-[12.5px] text-text-secondary mb-[3px]">{metric.label}</div>
            <div className="text-[11px] text-text-muted">{metric.subtitle}</div>
          </div>
        ))}
      </div>

      <div className="grid grid-cols-2 gap-4 mb-4">
        <div className="bg-surface border border-border-subtle rounded-[14px] overflow-hidden">
          <CardHeader title={t("dashboard.channelEffectiveness")} subtitle={t("dashboard.engagementMetrics")} />
          <div className="p-5 grid grid-cols-2 gap-3">
            {[
              { label: t("channel.email"), sent: channel.emailSent, delivery: emailDeliveryRate, read: emailReadRate, color: colors.accent },
              { label: t("channel.whatsapp"), sent: channel.whatsAppSent, delivery: waDeliveryRate, read: waReadRate, color: colors.wa },
            ].map(ch => (
              <div key={ch.label} className="bg-surface-2 border border-border-subtle rounded-[10px] p-3">
                <div className="font-bold text-sm mb-2">{ch.label}</div>
                <div className="text-xs text-text-muted mb-1">{t("dashboard.sent")}: <strong className="text-text-primary">{ch.sent.toLocaleString("pt-BR")}</strong></div>
                <div className="text-xs text-text-muted mb-1">{t("dashboard.delivered")}: <strong style={{ color: ch.color }}>{ch.delivery}%</strong></div>
                <div className="h-[3px] bg-surface-3 rounded-sm mb-2">
                  <div className="h-full rounded-sm" style={{ width: `${Math.min(ch.delivery, 100)}%`, background: ch.color }} />
                </div>
                <div className="text-xs text-text-muted">{t("analyticsPage.metrics.reads")}: <strong style={{ color: ch.color }}>{ch.read}%</strong></div>
              </div>
            ))}
          </div>
        </div>

        <div className="bg-surface border border-border-subtle rounded-[14px] overflow-hidden">
          <CardHeader title={t("analytics.criticalityShare")} />
          <div className="p-5 flex items-center justify-center flex-col gap-4">
            <ResponsiveContainer width="100%" height={200}>
              <PieChart>
                <Pie data={statusPie} cx="50%" cy="50%" innerRadius={55} outerRadius={85} paddingAngle={3} dataKey="value">
                  {statusPie.map((item, index) => <Cell key={index} fill={item.color} />)}
                </Pie>
                <Tooltip contentStyle={{ background: colors.surface2, border: `1px solid ${colors.border2}`, borderRadius: 8, fontSize: 12 }} />
              </PieChart>
            </ResponsiveContainer>
            <div className="flex flex-wrap gap-2.5 justify-center">
              {statusPie.map(item => (
                <div key={item.name} className="flex items-center gap-[5px] text-[11px]">
                  <span className="w-2 h-2 rounded-full shrink-0" style={{ background: item.color }} />
                  <span className="text-text-secondary">{item.name}</span>
                  <span className="font-bold">{item.value}</span>
                </div>
              ))}
            </div>
          </div>
        </div>
      </div>

      <div className="bg-surface border border-border-subtle rounded-[14px] overflow-hidden">
        <CardHeader title={t("analytics.sendsPerDay")} />
        <div className="p-5">
          <ResponsiveContainer width="100%" height={180}>
            <BarChart data={sends} key={chartThemeKey}>
              <CartesianGrid strokeDasharray="3 3" stroke={gridStroke} />
              <XAxis dataKey="day" tick={{ fill: colors.text3, fontSize: 10 }} axisLine={false} tickLine={false} />
              <YAxis tick={{ fill: colors.text3, fontSize: 11 }} axisLine={false} tickLine={false} />
              <Tooltip content={<ChartTooltip />} cursor={{ fill: `${colors.accent}1A` }} />
              <Legend wrapperStyle={{ fontSize: 11, paddingTop: 8 }} />
              <Bar dataKey="email" name={t("channel.email")} stackId="a" fill={colors.accent} radius={[0, 0, 0, 0]} />
              <Bar dataKey="wa" name={t("channel.whatsapp")} stackId="a" fill={colors.wa} radius={[4, 4, 0, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </div>
      </div>

      <div className="bg-surface border border-border-subtle rounded-[14px] overflow-hidden mt-4">
        <CardHeader title={t("analytics.recoveryByAging")} subtitle={`${critical.recoveredTitles.toLocaleString("pt-BR")} / ${critical.overdueBaseTitles.toLocaleString("pt-BR")} • ${critical.recoveryRate.toFixed(1)}%`} />
        <div className="p-5">
          <ResponsiveContainer width="100%" height={220}>
            <BarChart data={recoveryTrend} barGap={6} key={`${chartThemeKey}-recovery`}>
              <CartesianGrid strokeDasharray="4 4" stroke={gridStroke} />
              <XAxis dataKey="month" tick={{ fill: colors.text2, fontSize: 11 }} axisLine={false} tickLine={false} tickMargin={8} />
              <YAxis domain={[0, 100]} ticks={[0, 25, 50, 75, 100]} tick={{ fill: colors.text2, fontSize: 11 }} axisLine={false} tickLine={false} tickMargin={6} />
              <Tooltip content={<ChartTooltip />} cursor={{ fill: `${colors.accent}14` }} />
              <Legend wrapperStyle={{ fontSize: 11, paddingTop: 10 }} iconType="circle" iconSize={8} />
              <Bar dataKey="recoveredRate" name={t("analytics.recovered")} stackId="a" fill={recoveredColor} barSize={28} radius={[6, 6, 0, 0]} />
              <Bar dataKey="remainingRate" name="Em atraso" stackId="a" fill={colors.warn} barSize={28} radius={[6, 6, 0, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </div>
      </div>
    </div>
  );
};
