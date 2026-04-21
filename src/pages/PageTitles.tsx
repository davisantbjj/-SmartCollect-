import { useEffect, useState, useCallback } from "react";
import { t } from "../i18n";
import { ICONS } from "../utils/icons";
import { formatBRLFull, formatIsoDateBR } from "../utils/formatters";
import { Badge, ChannelPills, Button, Modal, FormInput } from "../components/UI";
import {
  ApiError, getTitles, getClients, createTitle, sendCollection, getTitleHistory, updateTitleStatus,
  getContactsByClient,
  type TitleResponse, type ClientResponse, type StoredSession, type TitleHistoryResponse, type SendCollectionRequest, type ContactResponse,
} from "../services/api";
import type { ShowToast } from "../types";

export const PageTitles = ({
  showToast,
  session,
  selectedTenantId,
  presetStatusFilter,
  presetFilterToken,
}: {
  showToast: ShowToast;
  session: StoredSession;
  selectedTenantId?: string;
  presetStatusFilter?: string;
  presetFilterToken?: number;
}) => {
  const [titles, setTitles] = useState<TitleResponse[]>([]);
  const [clients, setClients] = useState<ClientResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState("all");
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [totalCount, setTotalCount] = useState(0);
  const [modalOpen, setModalOpen] = useState(false);
  const [viewOpen, setViewOpen] = useState(false);
  const [historyOpen, setHistoryOpen] = useState(false);
  const [historyLoading, setHistoryLoading] = useState(false);
  const [historyItems, setHistoryItems] = useState<TitleHistoryResponse[]>([]);
  const [historyTitleCode, setHistoryTitleCode] = useState("");
  const [selected, setSelected] = useState<TitleResponse | null>(null);
  const [statusDraft, setStatusDraft] = useState("");
  const [statusSaving, setStatusSaving] = useState(false);
  const [collectOpen, setCollectOpen] = useState(false);
  const [collectTarget, setCollectTarget] = useState<TitleResponse | null>(null);
  const [useQuickTemplate, setUseQuickTemplate] = useState(false);
  const [quickChannel, setQuickChannel] = useState("Email");
  const [quickSubject, setQuickSubject] = useState("");
  const [quickBody, setQuickBody] = useState("");
  const [collectContacts, setCollectContacts] = useState<ContactResponse[]>([]);
  const [collectContactsLoading, setCollectContactsLoading] = useState(false);
  const [collectRecipientMode, setCollectRecipientMode] = useState<"companyDefault" | "primary" | "all" | "custom">("companyDefault");
  const [collectCustomContactIds, setCollectCustomContactIds] = useState<string[]>([]);
  const [collecting, setCollecting] = useState(false);
  const [saving, setSaving] = useState(false);

  // Form
  const [fClientId, setFClientId] = useState("");
  const [fCode, setFCode] = useState("");
  const [fAmount, setFAmount] = useState("");
  const [fDue, setFDue] = useState("");
  const [fIssue, setFIssue] = useState("");
  const [fBoleto, setFBoleto] = useState("");

  const canWrite = session.role === "Admin" || session.role === "Worker";

  const load = useCallback(async () => {
    if (session.role === "Master" && !selectedTenantId) {
      setTitles([]);
      setTotalPages(1);
      setTotalCount(0);
      setLoading(false);
      return;
    }

    try {
      setLoading(true);
      const res = await getTitles({
        tenantId: session.role === "Master" ? selectedTenantId : undefined,
        status: statusFilter === "all" ? undefined : statusFilter,
        search: search || undefined,
        page,
        pageSize: 20,
      });
      setTitles(res.items);
      setTotalPages(res.totalPages);
      setTotalCount(res.totalCount);
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : "Erro ao carregar títulos.";
      showToast(`${ICONS.cross} ${msg}`, "error");
    } finally {
      setLoading(false);
    }
  }, [session.role, selectedTenantId, statusFilter, search, page, showToast]);

  useEffect(() => { void load(); }, [load]);

  useEffect(() => {
    getClients().then(setClients).catch(() => {});
  }, []);

  useEffect(() => {
    if (!presetFilterToken || !presetStatusFilter) return;

    const allowedStatuses = new Set(["all", "Open", "Paid", "Overdue", "Cancelled"]);
    if (!allowedStatuses.has(presetStatusFilter)) return;

    setSearch("");
    setStatusFilter(presetStatusFilter);
    setPage(1);
  }, [presetFilterToken, presetStatusFilter]);

  const handleCreate = async () => {
    if (!fClientId || !fCode || !fAmount || !fDue) {
      showToast(`${ICONS.warning} Preencha os campos obrigatórios.`, "warn");
      return;
    }
    try {
      setSaving(true);
      await createTitle({
        clientId: fClientId,
        uniqueCode: fCode,
        amount: parseFloat(fAmount.replace(",", ".")),
        dueDate: fDue,
        issueDate: fIssue || fDue,
        boletoUrl: fBoleto || undefined,
      });
      showToast(`${ICONS.checkmark} ${t("toast.titleCreated")}`, "success");
      setModalOpen(false);
      setFClientId(""); setFCode(""); setFAmount(""); setFDue(""); setFIssue(""); setFBoleto("");
      void load();
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : "Erro ao salvar título.";
      showToast(`${ICONS.cross} ${msg}`, "error");
    } finally {
      setSaving(false);
    }
  };

  const openViewModal = (title: TitleResponse) => {
    setSelected(title);
    setStatusDraft(title.status);
    setViewOpen(true);
  };

  const handleUpdateStatus = async () => {
    if (!selected) return;

    if (!statusDraft) {
      showToast(`${ICONS.warning} Selecione um status.`, "warn");
      return;
    }

    if (statusDraft === selected.status) {
      showToast(`${ICONS.info} O título já está nesse status.`, "info");
      return;
    }

    try {
      setStatusSaving(true);
      const updated = await updateTitleStatus(selected.id, { status: statusDraft });
      setSelected(updated);
      setStatusDraft(updated.status);
      showToast(`${ICONS.checkmark} Status do título atualizado.`, "success");
      void load();
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : "Erro ao atualizar status do título.";
      showToast(`${ICONS.cross} ${msg}`, "error");
    } finally {
      setStatusSaving(false);
    }
  };

  const buildDefaultQuickBody = (title: TitleResponse) =>
    [
      "Olá {{ClienteNome}},",
      "",
      `Identificamos o título {{TituloCodigo}} no valor de {{Valor}} com vencimento em {{DataVencimento}}.`,
      "Link para pagamento: {{LinkBoleto}}",
      "",
      "Se necessário, podemos ajudar com uma regularização.",
      "",
      "Atenciosamente,",
      "{{Empresa}}",
    ].join("\n");

  const toChannelPills = (channels: string[]): string[] => {
    const resolved = new Set<string>();

    channels.forEach(channel => {
      const normalized = channel.trim().toLowerCase();

      if (normalized.includes("both") || normalized.includes("ambos")) {
        resolved.add("email");
        resolved.add("wa");
        return;
      }

      if (normalized.includes("whatsapp")) {
        resolved.add("wa");
        return;
      }

      if (normalized.includes("email"))
        resolved.add("email");
    });

    return Array.from(resolved);
  };

  const openCollectModal = (title: TitleResponse) => {
    setCollectTarget(title);
    setUseQuickTemplate(false);
    setQuickChannel("Email");
    setQuickSubject(`Cobrança do título ${title.uniqueCode}`);
    setQuickBody(buildDefaultQuickBody(title));
    setCollectRecipientMode("companyDefault");
    setCollectCustomContactIds([]);
    setCollectContacts([]);
    setCollectContactsLoading(true);
    setCollectOpen(true);
    void getContactsByClient(title.clientId)
      .then(items => {
        setCollectContacts(
          [...items].sort((a, b) => Number(b.isPrimary) - Number(a.isPrimary) || a.name.localeCompare(b.name, "pt-BR"))
        );
      })
      .catch(() => {
        setCollectContacts([]);
      })
      .finally(() => setCollectContactsLoading(false));
  };

  const resolveManualContactIds = (): string[] => {
    const ordered = [...collectContacts].sort((a, b) => Number(b.isPrimary) - Number(a.isPrimary) || a.name.localeCompare(b.name, "pt-BR"));

    if (collectRecipientMode === "companyDefault") return [];
    if (collectRecipientMode === "all") return ordered.map(c => c.id);
    if (collectRecipientMode === "custom") return collectCustomContactIds;

    const primary = ordered.find(c => c.isPrimary) ?? ordered[0];
    return primary ? [primary.id] : [];
  };

  const handleCollectConfirm = async () => {
    if (!collectTarget) return;

    if (useQuickTemplate && !quickBody.trim()) {
      showToast(`${ICONS.warning} Informe a mensagem do template rápido.`, "warn");
      return;
    }

    const selectedContactIds = resolveManualContactIds();
    if (collectRecipientMode !== "companyDefault" && selectedContactIds.length === 0) {
      showToast(`${ICONS.warning} Selecione ao menos um contato para este envio.`, "warn");
      return;
    }

    try {
      setCollecting(true);
      const payload: SendCollectionRequest = {
        ...(useQuickTemplate
          ? {
              useQuickTemplate: true,
              channel: quickChannel,
              subject: quickSubject,
              body: quickBody,
            }
          : {}),
        ...(selectedContactIds.length > 0 ? { contactIds: selectedContactIds } : {}),
      };

      await sendCollection(collectTarget.id, useQuickTemplate || selectedContactIds.length > 0 ? payload : undefined);
      showToast(`${ICONS.checkmark} ${t("toast.manualCollectionSent")}`, "success");
      setCollectOpen(false);
      void load();
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : "Erro ao enviar cobrança.";
      showToast(`${ICONS.cross} ${msg}`, "error");
    } finally {
      setCollecting(false);
    }
  };

  const openHistory = async (title: TitleResponse) => {
    try {
      setHistoryOpen(true);
      setHistoryLoading(true);
      setHistoryTitleCode(title.uniqueCode);
      const items = await getTitleHistory(title.id, session.role === "Master" ? selectedTenantId : undefined);
      setHistoryItems(items);
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : "Erro ao carregar histórico.";
      showToast(`${ICONS.cross} ${msg}`, "error");
      setHistoryItems([]);
    } finally {
      setHistoryLoading(false);
    }
  };

  const cols = [
    t("titles.col.code"), t("titles.col.clientCnpj"), t("titles.col.dueDate"),
    t("titles.col.amount"), t("titles.col.status"), t("titles.col.channels"),
    t("titles.col.lastAction"), t("titles.col.actions"),
  ];

  return (
    <div className="animate-fade-up">
      <div className="flex items-center gap-2.5 mb-5 flex-wrap">
        <input
          value={search}
          onChange={e => { setSearch(e.target.value); setPage(1); }}
          placeholder={`${ICONS.search}  ${t("titles.searchPlaceholder")}`}
          className="bg-surface-2 border border-border-subtle-2 rounded-lg px-[13px] py-[9px] text-[13px] text-text-primary outline-none flex-1 min-w-[220px] focus:border-accent"
        />
        <select
          value={statusFilter}
          onChange={e => { setStatusFilter(e.target.value); setPage(1); }}
          className="bg-surface-2 border border-border-subtle-2 rounded-lg px-[13px] py-[9px] text-[13px] text-text-primary outline-none focus:border-accent cursor-pointer"
        >
          <option value="all">{t("common.allStatuses")}</option>
          <option value="Open">{t("titles.filterOpen")}</option>
          <option value="Paid">{t("titles.filterPaid")}</option>
          <option value="Overdue">{t("titles.filterOverdue")}</option>
          <option value="Cancelled">{t("badge.cancelled")}</option>
        </select>
        {canWrite && (
          <Button variant="primary" onClick={() => setModalOpen(true)}>
            {t("titles.newTitle")}
          </Button>
        )}
      </div>

      <div className="text-xs text-text-muted mb-2">{totalCount} títulos encontrados</div>

      <div className="rounded-xl border border-border-subtle overflow-hidden">
        <table className="w-full border-collapse text-[13px]">
          <thead>
            <tr className="bg-surface-2">
              {cols.map(c => (
                <th key={c} className="px-4 py-[11px] text-left text-[10.5px] font-bold tracking-[0.5px] uppercase text-text-muted border-b border-border-subtle whitespace-nowrap">
                  {c}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr><td colSpan={8} className="text-center py-12 text-sm text-text-muted">Carregando...</td></tr>
            ) : titles.length === 0 ? (
              <tr>
                <td colSpan={8} className="text-center px-6 py-12 text-text-muted">
                  <div className="text-[32px] mb-2">{ICONS.search}</div>
                  <div className="text-sm text-text-secondary">{t("titles.noResults")}</div>
                  <div className="text-xs">{t("titles.adjustFilters")}</div>
                </td>
              </tr>
            ) : titles.map(title => (
              <tr key={title.id} className="border-b border-border-subtle hover:bg-surface-2/50 transition-colors">
                <td className="px-4 py-[13px] font-mono text-[11px] text-text-secondary">{title.uniqueCode}</td>
                <td className="px-4 py-[13px]">
                  <div className="font-semibold text-[13px]">{title.clientName}</div>
                  <div className="text-[11px] text-text-muted mt-0.5">{title.clientTaxId}</div>
                </td>
                <td className="px-4 py-[13px] text-[13px]">
                  <div>{formatIsoDateBR(title.dueDate)}</div>
                  {title.isBoletoOverdue && (
                    <div className="text-[11px] font-semibold text-danger mt-0.5">Boleto vencido</div>
                  )}
                </td>
                <td className="px-4 py-[13px] font-extrabold text-sm">{formatBRLFull(title.amount)}</td>
                <td className="px-4 py-[13px]"><Badge status={title.status.toLowerCase()} /></td>
                <td className="px-4 py-[13px]">
                  <ChannelPills channels={toChannelPills(title.channels)} />
                </td>
                <td className="px-4 py-[13px] text-xs text-text-secondary">
                  <div className="font-semibold text-text-primary">{title.lastAction ?? "Sem ação"}</div>
                  <div className="text-text-muted mt-0.5">{title.lastActionAt ? new Date(title.lastActionAt).toLocaleString("pt-BR") : "—"}</div>
                  <button
                    onClick={() => void openHistory(title)}
                    className="mt-1 text-[11px] underline text-accent bg-transparent border-none p-0 cursor-pointer"
                  >
                    Ver histórico
                  </button>
                </td>
                <td className="px-4 py-[13px]">
                  <div className="flex gap-1.5">
                    <Button size="sm" variant="secondary" onClick={() => openViewModal(title)}>
                      {ICONS.eye}
                    </Button>
                    {canWrite && (
                      <Button size="sm" variant="secondary" onClick={() => openCollectModal(title)}>
                        {ICONS.envelope}
                      </Button>
                    )}
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* Pagination */}
      {totalPages > 1 && (
        <div className="flex items-center justify-center gap-2 mt-4">
          <Button size="sm" variant="secondary" onClick={() => setPage(p => Math.max(1, p - 1))} disabled={page === 1}>←</Button>
          <span className="text-sm text-text-secondary">{page} / {totalPages}</span>
          <Button size="sm" variant="secondary" onClick={() => setPage(p => Math.min(totalPages, p + 1))} disabled={page === totalPages}>→</Button>
        </div>
      )}

      {/* Create modal */}
      <Modal open={modalOpen} onClose={() => setModalOpen(false)} title={t("titles.modalTitle")}
        footer={<>
          <Button variant="secondary" onClick={() => setModalOpen(false)}>{t("common.cancel")}</Button>
          <Button variant="primary" onClick={handleCreate}>{saving ? "Salvando..." : t("titles.saveTitle")}</Button>
        </>}
      >
        <div className="grid grid-cols-2 gap-3.5">
          <div className="col-span-2">
            <label className="block text-[11px] font-bold tracking-[0.6px] uppercase text-text-muted mb-[5px]">Cliente *</label>
            <select
              value={fClientId}
              onChange={e => setFClientId(e.target.value)}
              className="bg-surface-2 border border-border-subtle-2 rounded-lg px-[13px] py-[9px] text-[13px] text-text-primary outline-none w-full focus:border-accent"
            >
              <option value="">Selecionar cliente...</option>
              {clients.map(c => (
                <option key={c.id} value={c.id}>{c.legalName} – {c.taxId}</option>
              ))}
            </select>
          </div>
          <FormInput label={`${t("titles.titleCode")} *`} value={fCode} onChange={e => setFCode(e.target.value)} placeholder="TIT-2026-001" />
          <FormInput label={`${t("titles.amount")} *`} type="number" value={fAmount} onChange={e => setFAmount(e.target.value)} placeholder="0.00" />
          <FormInput label={`${t("titles.dueDate")} *`} type="date" value={fDue} onChange={e => setFDue(e.target.value)} />
          <FormInput label={t("titles.issueDate")} type="date" value={fIssue} onChange={e => setFIssue(e.target.value)} />
          <div className="col-span-2">
            <FormInput label={t("titles.ticketLink")} type="url" value={fBoleto} onChange={e => setFBoleto(e.target.value)} placeholder="https://..." />
          </div>
        </div>
      </Modal>

      {/* View modal */}
      {selected && (
        <Modal open={viewOpen} onClose={() => setViewOpen(false)} title={`Detalhes — ${selected.uniqueCode}`}
          footer={<>
            <Button variant="secondary" onClick={() => setViewOpen(false)}>Fechar</Button>
            {canWrite && (
              <Button variant="secondary" onClick={handleUpdateStatus} disabled={statusSaving}>
                {statusSaving ? "Salvando status..." : "Salvar status"}
              </Button>
            )}
            {canWrite && (
              <Button variant="primary" onClick={() => { setViewOpen(false); openCollectModal(selected); }}>
                Reenviar Cobrança
              </Button>
            )}
          </>}
        >
          <div className="grid grid-cols-2 gap-4 bg-surface-2 p-4 rounded-xl border border-border-subtle-2">
            <div>
              <div className="text-[11px] text-text-muted mb-1 uppercase font-bold tracking-wider">Cliente</div>
              <div className="text-sm font-semibold">{selected.clientName}</div>
              <div className="text-xs text-text-secondary mt-0.5">{selected.clientTaxId}</div>
            </div>
            <div>
              <div className="text-[11px] text-text-muted mb-1 uppercase font-bold tracking-wider">Status</div>
              <Badge status={selected.status.toLowerCase()} />
            </div>
            {canWrite && (
              <div className="col-span-2">
                <label className="block text-[11px] text-text-muted mb-1 uppercase font-bold tracking-wider">Alterar status</label>
                <select
                  value={statusDraft}
                  onChange={e => setStatusDraft(e.target.value)}
                  className="bg-surface border border-border-subtle-2 rounded-lg px-[13px] py-[9px] text-[13px] text-text-primary outline-none w-full focus:border-accent"
                >
                  <option value="Open">Em Aberto</option>
                  <option value="Overdue">Em Atraso</option>
                  <option value="Paid">Pago</option>
                  <option value="Cancelled">Cancelado</option>
                </select>
              </div>
            )}
            <div>
              <div className="text-[11px] text-text-muted mb-1 uppercase font-bold tracking-wider">Emissão</div>
              <div className="text-sm font-medium">{selected.issueDate ? formatIsoDateBR(selected.issueDate) : "-"}</div>
            </div>
            <div>
              <div className="text-[11px] text-text-muted mb-1 uppercase font-bold tracking-wider">Vencimento</div>
              <div className="text-sm font-medium">{formatIsoDateBR(selected.dueDate)}</div>
            </div>
            <div>
              <div className="text-[11px] text-text-muted mb-1 uppercase font-bold tracking-wider">Valor</div>
              <div className="text-base font-extrabold text-accent">{formatBRLFull(selected.amount)}</div>
            </div>
            {selected.boletoUrl && (
              <div className="col-span-2">
                <div className="text-[11px] text-text-muted mb-1 uppercase font-bold tracking-wider">Boleto</div>
                <a href={selected.boletoUrl} target="_blank" rel="noreferrer" className="text-sm text-accent underline break-all">
                  {selected.boletoUrl}
                </a>
              </div>
            )}
            <div className="col-span-2">
              <div className="text-[11px] text-text-muted mb-1 uppercase font-bold tracking-wider">Última Ação</div>
              <div className="text-sm text-text-secondary">{selected.lastAction ?? "Nenhuma ação registrada"}</div>
              <button
                onClick={() => void openHistory(selected)}
                className="mt-1 text-[12px] underline text-accent bg-transparent border-none p-0 cursor-pointer"
              >
                Ver histórico completo
              </button>
            </div>
          </div>
        </Modal>
      )}

      <Modal
        open={collectOpen}
        onClose={() => setCollectOpen(false)}
        title={collectTarget ? `Envio manual — ${collectTarget.uniqueCode}` : "Envio manual"}
        footer={<>
          <Button variant="secondary" onClick={() => setCollectOpen(false)} disabled={collecting}>Cancelar</Button>
          <Button variant="primary" onClick={handleCollectConfirm} disabled={collecting}>
            {collecting ? "Enviando..." : "Enviar cobrança"}
          </Button>
        </>}
      >
        <div className="space-y-4">
          {collectTarget && (
            <div className="bg-surface-2 border border-border-subtle rounded-lg p-3">
              <div className="text-[11px] uppercase font-bold tracking-wider text-text-muted">Cliente</div>
              <div className="text-sm font-semibold text-text-primary mt-0.5">{collectTarget.clientName}</div>
              <div className="text-xs text-text-secondary mt-0.5">{collectTarget.uniqueCode} • {formatBRLFull(collectTarget.amount)}</div>
            </div>
          )}

          <div>
            <label className="block text-[11px] font-bold tracking-[0.6px] uppercase text-text-muted mb-[5px]">Destinatários</label>
            <select
              value={collectRecipientMode}
              onChange={e => {
                const mode = e.target.value as "companyDefault" | "primary" | "all" | "custom";
                setCollectRecipientMode(mode);
                if (mode !== "custom")
                  setCollectCustomContactIds([]);
              }}
              className="bg-surface-2 border border-border-subtle-2 rounded-lg px-[13px] py-[9px] text-[13px] text-text-primary outline-none w-full focus:border-accent"
            >
              <option value="companyDefault">Padrão da empresa (principal/todos)</option>
              <option value="primary">Somente contato principal</option>
              <option value="all">Todos os contatos</option>
              <option value="custom">Escolher contatos</option>
            </select>
            <div className="text-[11px] text-text-muted mt-1">
              {collectRecipientMode === "companyDefault"
                ? "Usa a configuração da empresa definida na tela de contatos."
                : collectRecipientMode === "primary"
                  ? "Será usado apenas o contato principal da empresa."
                  : collectRecipientMode === "all"
                    ? "Será enviado para todos os contatos disponíveis."
                    : "Selecione manualmente os contatos abaixo."}
            </div>
          </div>

          {collectRecipientMode === "custom" && (
            <div className="space-y-2 bg-surface-2 border border-border-subtle rounded-lg p-3">
              {collectContactsLoading ? (
                <div className="text-xs text-text-muted">Carregando contatos...</div>
              ) : collectContacts.length === 0 ? (
                <div className="text-xs text-text-muted">Nenhum contato disponível para seleção.</div>
              ) : (
                collectContacts.map(contact => (
                  <label key={contact.id} className="flex items-start gap-2 text-sm text-text-secondary">
                    <input
                      type="checkbox"
                      checked={collectCustomContactIds.includes(contact.id)}
                      onChange={e => setCollectCustomContactIds(prev => e.target.checked
                        ? [...prev, contact.id]
                        : prev.filter(id => id !== contact.id))}
                      className="accent-accent mt-0.5"
                    />
                    <span>
                      <span className="font-semibold text-text-primary">{contact.name}</span>
                      {contact.isPrimary && <span className="ml-2 text-[10px] font-bold px-1.5 py-0.5 rounded-full bg-success/10 text-success">Principal</span>}
                      <span className="block text-xs text-text-muted">{contact.email || "sem e-mail"} • {contact.whatsAppPhone || "sem WhatsApp"}</span>
                    </span>
                  </label>
                ))
              )}
            </div>
          )}

          <label className="flex items-center gap-2 text-sm text-text-secondary">
            <input
              type="checkbox"
              checked={useQuickTemplate}
              onChange={e => setUseQuickTemplate(e.target.checked)}
              className="accent-accent"
            />
            Configurar template rápido para este envio
          </label>

          {useQuickTemplate ? (
            <div className="space-y-3">
              <div>
                <label className="block text-[11px] font-bold tracking-[0.6px] uppercase text-text-muted mb-[5px]">Canal</label>
                <select
                  value={quickChannel}
                  onChange={e => setQuickChannel(e.target.value)}
                  className="bg-surface-2 border border-border-subtle-2 rounded-lg px-[13px] py-[9px] text-[13px] text-text-primary outline-none w-full focus:border-accent"
                >
                  <option value="Email">E-mail</option>
                  <option value="WhatsApp">WhatsApp</option>
                  <option value="Both">Ambos</option>
                </select>
                <div className="text-[11px] text-text-muted mt-1">Use "Ambos" para registrar envio multi-canal no fluxo manual.</div>
              </div>

              <FormInput
                label="Assunto"
                value={quickSubject}
                onChange={e => setQuickSubject(e.target.value)}
                placeholder="Cobrança do título"
              />

              <div>
                <label className="block text-[11px] font-bold tracking-[0.6px] uppercase text-text-muted mb-[5px]">Mensagem *</label>
                <textarea
                  value={quickBody}
                  onChange={e => setQuickBody(e.target.value)}
                  rows={8}
                  className="bg-surface-2 border border-border-subtle-2 rounded-lg px-[13px] py-[9px] text-[13px] text-text-primary outline-none w-full focus:border-accent resize-y"
                  placeholder="Digite a mensagem rápida..."
                />
                <div className="text-[11px] text-text-muted mt-1">
                  Variáveis: {"{{ClienteNome}}, {{TituloCodigo}}, {{Valor}}, {{DataVencimento}}, {{Empresa}}"}
                </div>
              </div>
            </div>
          ) : (
            <div className="text-xs text-text-muted bg-surface-2 border border-border-subtle rounded-lg p-3">
              Sem template rápido, o sistema usa a régua ativa para agendar os disparos automáticos do título.
            </div>
          )}
        </div>
      </Modal>

      <Modal
        open={historyOpen}
        onClose={() => setHistoryOpen(false)}
        title={`Histórico — ${historyTitleCode}`}
        maxWidth={760}
        footer={<Button variant="secondary" onClick={() => setHistoryOpen(false)}>Fechar</Button>}
      >
        {historyLoading ? (
          <div className="text-sm text-text-muted py-4">Carregando histórico...</div>
        ) : historyItems.length === 0 ? (
          <div className="text-sm text-text-muted py-4">Nenhum evento encontrado para este título.</div>
        ) : (
          <div className="space-y-2">
            {historyItems.map(item => (
              <div key={item.id} className="bg-surface-2 border border-border-subtle rounded-lg px-3 py-2.5">
                <div className="text-[11px] text-text-muted">{new Date(item.timestamp).toLocaleString("pt-BR")}</div>
                <div className="text-[13px] font-semibold text-text-primary mt-0.5">{item.action}</div>
                <div className="text-[12px] text-text-secondary mt-1">{item.description}</div>
              </div>
            ))}
          </div>
        )}
      </Modal>
    </div>
  );
};
