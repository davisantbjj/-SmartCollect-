import { colors } from "../utils/colors";
import { t } from "../i18n";
import type { Title, Contact, FunnelData, SendsData, StatusPieData, AgingData, Template, Step, NavSection, ActivityLogEntry } from "../types";

export const TITLES: Title[] = [
  { id: "TIT-001", client: "Construtora Alpha Ltda.", cnpj: "12.345.678/0001-90", dueDate: "02/04/2026", amount: 128400, status: "open", channels: ["email", "wa"], lastAction: "E-mail enviado · 29/03" },
  { id: "TIT-002", client: "Distribuidora Beta S.A.", cnpj: "98.765.432/0001-11", dueDate: "28/03/2026", amount: 97200, status: "overdue", channels: [], lastAction: "Sem contato cadastrado" },
  { id: "TIT-003", client: "Comércio Gama ME", cnpj: "55.123.456/0001-34", dueDate: "30/03/2026", amount: 74800, status: "sent", channels: ["wa"], lastAction: "WA enviado · 28/03" },
  { id: "TIT-004", client: "Logística Delta Eireli", cnpj: "33.987.654/0001-78", dueDate: "15/04/2026", amount: 52600, status: "open", channels: ["email"], lastAction: "Aguardando D-3" },
  { id: "TIT-005", client: "Serviços Épsilon Ltda.", cnpj: "77.234.567/0001-22", dueDate: "20/03/2026", amount: 41300, status: "paid", channels: ["email", "wa"], lastAction: "Pago · 27/03 ✅" },
  { id: "TIT-006", client: "Tech Zeta S.A.", cnpj: "11.222.333/0001-44", dueDate: "10/04/2026", amount: 38700, status: "pending", channels: [], lastAction: "Pendente de e-mail" },
  { id: "TIT-007", client: "Indústria Eta Ltda.", cnpj: "44.555.666/0001-55", dueDate: "15/03/2026", amount: 29800, status: "overdue", channels: ["email", "wa"], lastAction: "Aviso final enviado" },
  { id: "TIT-008", client: "Theta Comércio ME", cnpj: "66.777.888/0001-66", dueDate: "20/04/2026", amount: 21400, status: "open", channels: ["email"], lastAction: "Aguardando D-3" },
];

export const CONTACTS: Contact[] = [
  { company: "Construtora Alpha Ltda.", cnpj: "12.345.678/0001-90", name: "Carlos Mendes", type: "Financeiro", email: "financeiro@alpha.com.br", phone: "+55 11 99999-1234", titleCount: 3, status: "complete" },
  { company: "Distribuidora Beta S.A.", cnpj: "98.765.432/0001-11", name: "—", type: "—", email: "—", phone: "—", titleCount: 2, status: "pending" },
  { company: "Comércio Gama ME", cnpj: "55.123.456/0001-34", name: "Ana Souza", type: "Sócia", email: "—", phone: "+55 21 98888-5678", titleCount: 1, status: "partial" },
];

export const funnelData: FunnelData[] = [
  { month: "Out", receivable: 2100, overdue: 780, recovered: 180 },
  { month: "Nov", receivable: 2250, overdue: 820, recovered: 220 },
  { month: "Dez", receivable: 2400, overdue: 910, recovered: 265 },
  { month: "Jan", receivable: 2180, overdue: 860, recovered: 290 },
  { month: "Fev", receivable: 2320, overdue: 840, recovered: 310 },
  { month: "Mar", receivable: 2400, overdue: 890, recovered: 340 },
];

export const sendsData: SendsData[] = [
  { day: "15/3", email: 42, wa: 28 }, { day: "16/3", email: 55, wa: 35 },
  { day: "17/3", email: 38, wa: 30 }, { day: "18/3", email: 60, wa: 42 },
  { day: "19/3", email: 72, wa: 55 }, { day: "20/3", email: 18, wa: 10 },
  { day: "21/3", email: 12, wa: 8 },  { day: "22/3", email: 48, wa: 32 },
  { day: "23/3", email: 63, wa: 48 }, { day: "24/3", email: 57, wa: 40 },
  { day: "25/3", email: 71, wa: 52 }, { day: "26/3", email: 25, wa: 18 },
  { day: "27/3", email: 19, wa: 14 }, { day: "28/3", email: 66, wa: 44 },
];

export const statusPie: StatusPieData[] = [
  { name: t("statusPie.open"), value: 123, color: colors.accent },
  { name: t("statusPie.sent"), value: 89, color: colors.accent },
  { name: t("statusPie.paid"), value: 47, color: colors.success },
  { name: t("statusPie.cancelled"), value: 12, color: colors.text3 },
  { name: t("statusPie.pending"), value: 13, color: colors.warn },
];

export const agingData: AgingData[] = [
  { range: t("aging.dueSoon"), value: 1200, color: colors.success },
  { range: t("aging.1to30"), value: 380, color: colors.warn },
  { range: t("aging.31to60"), value: 240, color: colors.accent },
  { range: t("aging.61to90"), value: 150, color: colors.accent },
  { range: t("aging.over90"), value: 120, color: colors.danger },
];

export const TEMPLATES: Template[] = [
  { channel: "email", name: "Lembrete D-3", trigger: "3 dias antes do vencimento", preview: "Olá {{NomeCliente}}, seu título de {{Valor}} vence em 3 dias ({{DataVencimento}}). Acesse o boleto para quitar em dia.", selected: true },
  { channel: "wa", name: "Cobrança D+1", trigger: "1 dia após vencimento", preview: "Olá {{NomeCliente}}! 📋 Identificamos que o título {{CodigoTitulo}} de {{Valor}} venceu ontem. Segue boleto: {{LinkBoleto}}", selected: false },
  { channel: "email", name: "Aviso Final D+10", trigger: "10 dias após vencimento", preview: "Prezado(a) {{NomeCliente}}, após diversas tentativas de contato, informamos que o valor de {{Valor}} encontra-se em aberto.", selected: false },
  { channel: "wa", name: "Agradecimento Pagamento", trigger: "Após confirmação de pagamento", preview: "🎉 Recebemos seu pagamento! Obrigado, {{NomeCliente}}. O título {{CodigoTitulo}} já está quitado em nosso sistema.", selected: false },
];

export const VARS = ["{{NomeCliente}}", "{{CNPJ}}", "{{Valor}}", "{{DataVencimento}}", "{{CodigoTitulo}}", "{{LinkBoleto}}", "{{DiasAtraso}}", "{{NomeEmpresa}}"];

export const STEPS: Step[] = [
  { label: "D-3", color: colors.accent, title: t("step.preExpiration"), description: t("step.preExpirationDesc"), channels: ["email", "wa"] },
  { label: "D-1", color: colors.accent, title: t("step.dayBefore"), description: t("step.dayBeforeDesc"), channels: ["email"] },
  { label: "D+1", color: colors.accent, title: t("step.postExpiration"), description: t("step.postExpirationDesc"), channels: ["email", "wa"] },
  { label: "D+7", color: colors.accent, title: t("step.secondCollection"), description: t("step.secondCollectionDesc"), channels: ["wa"] },
  { label: "D+10", color: colors.accent, title: t("step.finalWarning"), description: t("step.finalWarningDesc"), channels: ["email", "wa"] },
];

export const NAV: NavSection[] = [
  { section: t("nav.overview"), items: [
    { id: "dashboard", icon: "dashboard", label: t("nav.dashboard") },
    { id: "analytics", icon: "analytics", label: t("nav.analytics") },
  ]},
  { section: t("nav.collection"), items: [
    { id: "titles", icon: "document", label: t("nav.titles"), badge: 23, badgeType: "danger" },
    { id: "import", icon: "upload", label: t("nav.importData") },
    { id: "contacts", icon: "users", label: t("nav.contactsCRM"), badge: 5, badgeType: "warn" },
  ]},
  { section: t("nav.configuration"), items: [
    { id: "sequence", icon: "settings", label: t("nav.collectionSequence") },
    { id: "templates", icon: "mail", label: t("nav.templates") },
    { id: "integration", icon: "link", label: t("nav.integrationSMTP") },
  ]},
];

export const PAGE_META: Record<string, [string, string]> = {
  dashboard:   [t("pageMeta.dashboard.title"), t("pageMeta.dashboard.subtitle")],
  analytics:   [t("pageMeta.analytics.title"), t("pageMeta.analytics.subtitle")],
  titles:      [t("pageMeta.titles.title"), t("pageMeta.titles.subtitle")],
  import:      [t("pageMeta.import.title"), t("pageMeta.import.subtitle")],
  contacts:    [t("pageMeta.contacts.title"), t("pageMeta.contacts.subtitle")],
  sequence:    [t("pageMeta.sequence.title"), t("pageMeta.sequence.subtitle")],
  templates:   [t("pageMeta.templates.title"), t("pageMeta.templates.subtitle")],
  integration: [t("pageMeta.integration.title"), t("pageMeta.integration.subtitle")],
};

export const ACTIVITY_LOG: ActivityLogEntry[] = [
  { id: 1, timestamp: "14:32:05", channel: "wa", status: "sent", recipient: "Construtora Alpha", summary: "Lembrete: Seu título de R$ 128.400 vence em 3 dias. Acesse o boleto para quitar." },
  { id: 2, timestamp: "14:28:42", channel: "instagram", status: "sent", recipient: "Distribuidora Beta", summary: "Aviso de cobrança: Identificamos que o título de R$ 97.200 venceu ontem." },
  { id: 3, timestamp: "14:25:18", channel: "wa", status: "error", recipient: "Comércio Gama", summary: "Falha ao enviar: Contato não disponível ou número inválido." },
  { id: 4, timestamp: "14:22:37", channel: "email", status: "sent", recipient: "Logística Delta", summary: "Template: Aviso pré-vencimento de R$ 52.600. Prazo: 2 dias." },
  { id: 5, timestamp: "14:18:15", channel: "wa", status: "sent", recipient: "Serviços Épsilon", summary: "Confirmação de pagamento: Título de R$ 41.300 já foi quitado. Obrigado!" },
  { id: 6, timestamp: "14:12:08", channel: "instagram", status: "sent", recipient: "Tech Zeta", summary: "Mensagem de follow-up enviada. Aguardando retorno do cliente." },
  { id: 7, timestamp: "14:05:33", channel: "wa", status: "error", recipient: "Indústria Eta", summary: "Erro de API: Falha na conexão com servidor de mensagens." },
  { id: 8, timestamp: "13:58:47", channel: "email", status: "sent", recipient: "Theta Comércio", summary: "Último aviso antes de cobrança judicial de R$ 21.400." },
  { id: 9, timestamp: "13:52:19", channel: "wa", status: "sent", recipient: "Construtora Alpha", summary: "Solicitação de documento: Fatura de referência para validação." },
  { id: 10, timestamp: "13:45:06", channel: "instagram", status: "sent", recipient: "Beta Logística", summary: "Proposta de parcelamento para débito em aberto de R$ 15.800." },
];
