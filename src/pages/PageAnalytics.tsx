import { useEffect, useMemo, useState } from "react";
import {
  BarChart, Bar, PieChart, Pie, Cell,
  XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer
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
}: {
  showToast: ShowToast;
  session: StoredSession;
  selectedTenantId?: string;
}) => {
  const [loading, setLoading] = useState(true);
  const [summary, setSummary] = useState({ totalReceivable: 0, totalOverdue: 0, totalPaid: 0, recoveryRate: 0 });
  const [statusBreakdown, setStatusBreakdown] = useState({ open: 0, pendingData: 0, overdue: 0, paid: 0, cancelled: 0 });
  const [sends, setSends] = useState<{ day: string; email: number; wa: number }[]>([]);
  const [channel, setChannel] = useState({ emailSent: 0, emailDelivered: 0, emailViewed: 0, whatsAppSent: 0, whatsAppDelivered: 0, whatsAppViewed: 0 });
  const receivableColor = "#FDE047";
  const overdueColor = colors.accent;

  const isMaster = session.role === "Master";

  useEffect(() => {
    let cancelled = false;

    async function load() {
      try {
        setLoading(true);
        const tenantId = isMaster ? (selectedTenantId || undefined) : (session.tenantId || undefined);
        const [s, sb, snd, ch] = await Promise.all([
          getDashboardSummary(tenantId),
          getDashboardStatusBreakdown(tenantId),
          getDashboardSendsPerDay(tenantId),
          getDashboardChannelMetrics(tenantId),
        ]);

        if (cancelled) return;

        setSummary(s);
        setStatusBreakdown(sb);
        setSends(snd.items.map(i => ({ day: toBrDayLabel(i.day), email: i.emailCount, wa: i.whatsAppCount })));
        setChannel(ch);
      } catch (err) {
        const msg = err instanceof ApiError ? err.message : "Falha ao carregar analytics.";
        if (!cancelled) showToast(`${ICONS.cross} ${msg}`, "error");
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
  const criticalTitles = statusBreakdown.overdue + statusBreakdown.pendingData;
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

  return (
    <div className="animate-fade-up">
      {isMaster && (
        <div className="mb-4 px-4 py-2.5 bg-accent/8 border border-accent/20 rounded-[10px] text-sm text-accent font-semibold flex items-center gap-2">
          {ICONS.analytics} {selectedTenantId ? "Analytics da empresa selecionada" : "Analytics agregado de todas as empresas"}
        </div>
      )}

      <div className="grid grid-cols-4 gap-3.5 mb-6">
        {[
          { value: `${paymentRate.toFixed(1)}%`, label: t("analytics.recoveryRate"), subtitle: `${statusBreakdown.paid.toLocaleString("pt-BR")} pagos de ${totalOperational.toLocaleString("pt-BR")}`, color: colors.success },
          { value: `${criticalTitles.toLocaleString("pt-BR")}`, label: t("analytics.criticalTitles"), subtitle: `${safePercent(criticalTitles, totalTitles || 1)}% do total de títulos`, color: overdueColor },
          { value: new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL", maximumFractionDigits: 0 }).format(summary.totalOverdue), label: t("analytics.overdueAmount"), subtitle: `${overduePortfolioRate}% da carteira a receber`, color: overdueColor },
          { value: `${avgDeliveryRate.toFixed(1)}%`, label: t("analytics.avgDeliveryRate"), subtitle: `${totalDelivered.toLocaleString("pt-BR")} de ${totalSent.toLocaleString("pt-BR")} entregues`, color: colors.accent },
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
              { label: "E-mail", sent: channel.emailSent, delivery: emailDeliveryRate, read: emailReadRate, color: colors.accent },
              { label: "WhatsApp", sent: channel.whatsAppSent, delivery: waDeliveryRate, read: waReadRate, color: colors.wa },
            ].map(ch => (
              <div key={ch.label} className="bg-surface-2 border border-border-subtle rounded-[10px] p-3">
                <div className="font-bold text-sm mb-2">{ch.label}</div>
                <div className="text-xs text-text-muted mb-1">Enviados: <strong className="text-text-primary">{ch.sent.toLocaleString("pt-BR")}</strong></div>
                <div className="text-xs text-text-muted mb-1">Entregues: <strong style={{ color: ch.color }}>{ch.delivery}%</strong></div>
                <div className="h-[3px] bg-surface-3 rounded-sm mb-2">
                  <div className="h-full rounded-sm" style={{ width: `${Math.min(ch.delivery, 100)}%`, background: ch.color }} />
                </div>
                <div className="text-xs text-text-muted">Leituras: <strong style={{ color: ch.color }}>{ch.read}%</strong></div>
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
            <BarChart data={sends}>
              <CartesianGrid strokeDasharray="3 3" stroke={colors.border} />
              <XAxis dataKey="day" tick={{ fill: colors.text3, fontSize: 10 }} axisLine={false} tickLine={false} />
              <YAxis tick={{ fill: colors.text3, fontSize: 11 }} axisLine={false} tickLine={false} />
              <Tooltip content={<ChartTooltip />} cursor={{ fill: "rgba(220, 38, 38, 0.08)" }} />
              <Legend wrapperStyle={{ fontSize: 11, paddingTop: 8 }} />
              <Bar dataKey="email" name={t("channel.email")} stackId="a" fill={`${colors.accent}cc`} radius={[0, 0, 0, 0]} />
              <Bar dataKey="wa" name={t("channel.whatsapp")} stackId="a" fill="#25d36699" radius={[4, 4, 0, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </div>
      </div>
    </div>
  );
};
