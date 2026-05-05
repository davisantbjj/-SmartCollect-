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
  const [quickTemplateType, setQuickTemplateType] = useState<"Collection" | "ThankYou">("Collection");
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

  const tenantId = session.role === "Master" ? selectedTenantId : undefined;
  const requiresTenantSelection = session.role === "Master" && !tenantId;
  const canWrite = session.role === "Admin"
    || session.role === "Worker"
    || (session.role === "Master" && Boolean(tenantId));

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
      const msg = err instanceof ApiError ? err.message : t("titles.errors.load");
      showToast(`${ICONS.cross} ${msg}`, "error");
    } finally {
      setLoading(false);
    }
  }, [session.role, selectedTenantId, statusFilter, search, page, showToast]);

  useEffect(() => { void load(); }, [load]);

  useEffect(() => {
    if (requiresTenantSelection) {
      setClients([]);
      return;
    }

    getClients(tenantId).then(setClients).catch(() => {});
  }, [tenantId, requiresTenantSelection]);

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
      showToast(`${ICONS.warning} ${t("titles.validation.requiredFields")}`, "warn");
      return;
    }
    try {
      setSaving(true);
      if (requiresTenantSelection) {
        showToast(`${ICONS.warning} ${t("titles.validation.selectTenantToCreate")}`, "warn");
        return;
      }

      await createTitle({
        clientId: fClientId,
        uniqueCode: fCode,
        amount: parseFloat(fAmount.replace(",", ".")),
        dueDate: fDue,
        issueDate: fIssue || fDue,
        boletoUrl: fBoleto || undefined,
      }, tenantId);
      showToast(`${ICONS.checkmark} ${t("toast.titleCreated")}`, "success");
      setModalOpen(false);
      setFClientId(""); setFCode(""); setFAmount(""); setFDue(""); setFIssue(""); setFBoleto("");
      void load();
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : t("titles.errors.save");
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
      showToast(`${ICONS.warning} ${t("titles.validation.selectStatus")}`, "warn");
      return;
    }

    if (statusDraft === selected.status) {
      showToast(`${ICONS.info} ${t("titles.validation.statusAlreadySet")}`, "info");
      return;
    }

    try {
      setStatusSaving(true);
      const updated = await updateTitleStatus(selected.id, { status: statusDraft }, tenantId);
      setSelected(updated);
      setStatusDraft(updated.status);
      showToast(`${ICONS.checkmark} ${t("titles.messages.statusUpdated")}`, "success");
      void load();
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : t("titles.errors.statusUpdate");
      showToast(`${ICONS.cross} ${msg}`, "error");
    } finally {
      setStatusSaving(false);
    }
  };

  const buildDefaultQuickBody = () => t("titles.quickTemplates.collectionBody");

  const buildDefaultThankYouBody = () => t("titles.quickTemplates.thankYouBody");

  const applyQuickTemplateDefaults = (type: "Collection" | "ThankYou", title: TitleResponse) => {
    if (type === "ThankYou") {
      setQuickTemplateType("ThankYou");
      setQuickChannel("Email");
      setQuickSubject(`${t("titles.quickTemplates.thankYouSubject")} — ${title.uniqueCode}`);
      setQuickBody(buildDefaultThankYouBody());
      return;
    }

    setQuickTemplateType("Collection");
    setQuickSubject(`${t("titles.quickTemplates.collectionSubject")} ${title.uniqueCode}`);
    setQuickBody(buildDefaultQuickBody());
  };

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
    applyQuickTemplateDefaults("Collection", title);
    setCollectRecipientMode("companyDefault");
    setCollectCustomContactIds([]);
    setCollectContacts([]);
    setCollectContactsLoading(true);
    setCollectOpen(true);
    void getContactsByClient(title.clientId, tenantId)
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
      showToast(`${ICONS.warning} ${t("titles.validation.quickTemplateMessage")}`, "warn");
      return;
    }

    const selectedContactIds = resolveManualContactIds();
    if (collectRecipientMode !== "companyDefault" && selectedContactIds.length === 0) {
      showToast(`${ICONS.warning} ${t("titles.validation.selectContact")}`, "warn");
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

      await sendCollection(
        collectTarget.id,
        useQuickTemplate || selectedContactIds.length > 0 ? payload : undefined,
        tenantId
      );
      showToast(`${ICONS.checkmark} ${t("toast.manualCollectionSent")}`, "success");
      setCollectOpen(false);
      void load();
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : t("titles.errors.sendCollection");
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
      const msg = err instanceof ApiError ? err.message : t("titles.errors.historyLoad");
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

      <div className="text-xs text-text-muted mb-2">{totalCount} {t("titles.foundCount")}</div>

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
              <tr><td colSpan={8} className="text-center py-12 text-sm text-text-muted">{t("common.loading")}</td></tr>
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
                    <div className="text-[11px] font-semibold text-danger mt-0.5">{t("titles.boletoOverdue")}</div>
                  )}
                </td>
                <td className="px-4 py-[13px] font-extrabold text-sm">{formatBRLFull(title.amount)}</td>
                <td className="px-4 py-[13px]"><Badge status={title.status.toLowerCase()} /></td>
                <td className="px-4 py-[13px]">
                  <ChannelPills channels={toChannelPills(title.channels)} />
                </td>
                <td className="px-4 py-[13px] text-xs text-text-secondary">
                  <div className="font-semibold text-text-primary">{title.lastAction ?? t("titles.noAction")}</div>
                  <div className="text-text-muted mt-0.5">{title.lastActionAt ? new Date(title.lastActionAt).toLocaleString("pt-BR") : "—"}</div>
                  <button
                    onClick={() => void openHistory(title)}
                    className="mt-1 text-[11px] underline text-accent bg-transparent border-none p-0 cursor-pointer"
                  >
                    {t("titles.viewHistory")}
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
          <Button variant="primary" onClick={handleCreate}>{saving ? t("common.saving") : t("titles.saveTitle")}</Button>
        </>}
      >
        <div className="grid grid-cols-2 gap-3.5">
          <div className="col-span-2">
            <label className="block text-[11px] font-bold tracking-[0.6px] uppercase text-text-muted mb-[5px]">{t("titles.selectClientLabel")}</label>
            <select
              value={fClientId}
              onChange={e => setFClientId(e.target.value)}
              className="bg-surface-2 border border-border-subtle-2 rounded-lg px-[13px] py-[9px] text-[13px] text-text-primary outline-none w-full focus:border-accent"
            >
              <option value="">{t("titles.selectClientPlaceholder")}</option>
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
        <Modal open={viewOpen} onClose={() => setViewOpen(false)} title={`${t("titles.detailsTitle")} — ${selected.uniqueCode}`}
          footer={<>
            <Button variant="secondary" onClick={() => setViewOpen(false)}>{t("common.close")}</Button>
            {canWrite && (
              <Button variant="secondary" onClick={handleUpdateStatus} disabled={statusSaving}>
                {statusSaving ? t("titles.savingStatus") : t("titles.saveStatus")}
              </Button>
            )}
            {canWrite && (
              <Button variant="primary" onClick={() => { setViewOpen(false); openCollectModal(selected); }}>
                {t("titles.resendCollection")}
              </Button>
            )}
          </>}
        >
          <div className="grid grid-cols-2 gap-4 bg-surface-2 p-4 rounded-xl border border-border-subtle-2">
            <div>
              <div className="text-[11px] text-text-muted mb-1 uppercase font-bold tracking-wider">{t("titles.labels.client")}</div>
              <div className="text-sm font-semibold">{selected.clientName}</div>
              <div className="text-xs text-text-secondary mt-0.5">{selected.clientTaxId}</div>
            </div>
            <div>
              <div className="text-[11px] text-text-muted mb-1 uppercase font-bold tracking-wider">{t("titles.labels.status")}</div>
              <Badge status={selected.status.toLowerCase()} />
            </div>
            {canWrite && (
              <div className="col-span-2">
                <label className="block text-[11px] text-text-muted mb-1 uppercase font-bold tracking-wider">{t("titles.labels.changeStatus")}</label>
                <select
                  value={statusDraft}
                  onChange={e => setStatusDraft(e.target.value)}
                  className="bg-surface border border-border-subtle-2 rounded-lg px-[13px] py-[9px] text-[13px] text-text-primary outline-none w-full focus:border-accent"
                >
                  <option value="Open">{t("badge.open")}</option>
                  <option value="Overdue">{t("badge.overdue")}</option>
                  <option value="Paid">{t("badge.paid")}</option>
                  <option value="Cancelled">{t("badge.cancelled")}</option>
                </select>
              </div>
            )}
            <div>
              <div className="text-[11px] text-text-muted mb-1 uppercase font-bold tracking-wider">{t("titles.labels.issueDate")}</div>
              <div className="text-sm font-medium">{selected.issueDate ? formatIsoDateBR(selected.issueDate) : "-"}</div>
            </div>
            <div>
              <div className="text-[11px] text-text-muted mb-1 uppercase font-bold tracking-wider">{t("titles.labels.dueDate")}</div>
              <div className="text-sm font-medium">{formatIsoDateBR(selected.dueDate)}</div>
            </div>
            <div>
              <div className="text-[11px] text-text-muted mb-1 uppercase font-bold tracking-wider">{t("titles.labels.amount")}</div>
              <div className="text-base font-extrabold text-accent">{formatBRLFull(selected.amount)}</div>
            </div>
            {selected.boletoUrl && (
              <div className="col-span-2">
                <div className="text-[11px] text-text-muted mb-1 uppercase font-bold tracking-wider">{t("titles.labels.boleto")}</div>
                <a href={selected.boletoUrl} target="_blank" rel="noreferrer" className="text-sm text-accent underline break-all">
                  {selected.boletoUrl}
                </a>
              </div>
            )}
            <div className="col-span-2">
              <div className="text-[11px] text-text-muted mb-1 uppercase font-bold tracking-wider">{t("titles.labels.lastAction")}</div>
              <div className="text-sm text-text-secondary">{selected.lastAction ?? t("titles.noActionRecorded")}</div>
              <button
                onClick={() => void openHistory(selected)}
                className="mt-1 text-[12px] underline text-accent bg-transparent border-none p-0 cursor-pointer"
              >
                {t("titles.viewHistoryFull")}
              </button>
            </div>
          </div>
        </Modal>
      )}

      <Modal
        open={collectOpen}
        onClose={() => setCollectOpen(false)}
        title={collectTarget ? `${t("titles.manualSendTitle")} — ${collectTarget.uniqueCode}` : t("titles.manualSendTitle")}
        footer={<>
          <Button variant="secondary" onClick={() => setCollectOpen(false)} disabled={collecting}>{t("common.cancel")}</Button>
          <Button variant="primary" onClick={handleCollectConfirm} disabled={collecting}>
            {collecting ? t("titles.manualSendSending") : t("titles.manualSendSend")}
          </Button>
        </>}
      >
        <div className="space-y-4">
          {collectTarget && (
            <div className="bg-surface-2 border border-border-subtle rounded-lg p-3">
              <div className="text-[11px] uppercase font-bold tracking-wider text-text-muted">{t("titles.labels.client")}</div>
              <div className="text-sm font-semibold text-text-primary mt-0.5">{collectTarget.clientName}</div>
              <div className="text-xs text-text-secondary mt-0.5">{collectTarget.uniqueCode} • {formatBRLFull(collectTarget.amount)}</div>
            </div>
          )}

          <div>
            <label className="block text-[11px] font-bold tracking-[0.6px] uppercase text-text-muted mb-[5px]">{t("titles.recipientsLabel")}</label>
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
              <option value="companyDefault">{t("titles.recipientOptionCompanyDefault")}</option>
              <option value="primary">{t("titles.recipientOptionPrimary")}</option>
              <option value="all">{t("titles.recipientOptionAll")}</option>
              <option value="custom">{t("titles.recipientOptionCustom")}</option>
            </select>
            <div className="text-[11px] text-text-muted mt-1">
              {collectRecipientMode === "companyDefault"
                ? t("titles.recipientHelpCompanyDefault")
                : collectRecipientMode === "primary"
                  ? t("titles.recipientHelpPrimary")
                  : collectRecipientMode === "all"
                    ? t("titles.recipientHelpAll")
                    : t("titles.recipientHelpCustom")}
            </div>
          </div>

          {collectRecipientMode === "custom" && (
            <div className="space-y-2 bg-surface-2 border border-border-subtle rounded-lg p-3">
              {collectContactsLoading ? (
                <div className="text-xs text-text-muted">{t("titles.contactsLoading")}</div>
              ) : collectContacts.length === 0 ? (
                <div className="text-xs text-text-muted">{t("titles.contactsEmpty")}</div>
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
                      {contact.isPrimary && <span className="ml-2 text-[10px] font-bold px-1.5 py-0.5 rounded-full bg-success/10 text-success">{t("titles.contactPrimary")}</span>}
                      <span className="block text-xs text-text-muted">{contact.email || t("titles.contactNoEmail")} • {contact.whatsAppPhone || t("titles.contactNoWhatsapp")}</span>
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
            {t("titles.quickTemplateToggle")}
          </label>

          {useQuickTemplate ? (
            <div className="space-y-3">
              <div>
                <label className="block text-[11px] font-bold tracking-[0.6px] uppercase text-text-muted mb-[5px]">{t("titles.quickTemplateTypeLabel")}</label>
                <select
                  value={quickTemplateType}
                  onChange={e => {
                    if (!collectTarget) return;
                    applyQuickTemplateDefaults(e.target.value as "Collection" | "ThankYou", collectTarget);
                  }}
                  className="bg-surface-2 border border-border-subtle-2 rounded-lg px-[13px] py-[9px] text-[13px] text-text-primary outline-none w-full focus:border-accent"
                >
                  <option value="Collection">{t("titles.quickTemplateTypeCollection")}</option>
                  <option value="ThankYou">{t("titles.quickTemplateTypeThankYou")}</option>
                </select>
              </div>
              <div>
                <label className="block text-[11px] font-bold tracking-[0.6px] uppercase text-text-muted mb-[5px]">{t("titles.quickTemplateChannelLabel")}</label>
                <select
                  value={quickChannel}
                  onChange={e => setQuickChannel(e.target.value)}
                  disabled={quickTemplateType === "ThankYou"}
                  className="bg-surface-2 border border-border-subtle-2 rounded-lg px-[13px] py-[9px] text-[13px] text-text-primary outline-none w-full focus:border-accent"
                >
                  <option value="Email">{t("channel.email")}</option>
                  <option value="WhatsApp">{t("channel.whatsapp")}</option>
                  <option value="Both">{t("common.both")}</option>
                </select>
                <div className="text-[11px] text-text-muted mt-1">
                  {quickTemplateType === "ThankYou"
                    ? t("titles.quickTemplateChannelHelperThankYou")
                    : t("titles.quickTemplateChannelHelperCollection")}
                </div>
              </div>

              <FormInput
                label={t("titles.quickSubjectLabel")}
                value={quickSubject}
                onChange={e => setQuickSubject(e.target.value)}
                placeholder={t("titles.quickSubjectPlaceholder")}
              />

              <div>
                <label className="block text-[11px] font-bold tracking-[0.6px] uppercase text-text-muted mb-[5px]">{t("titles.quickMessageLabel")}</label>
                <textarea
                  value={quickBody}
                  onChange={e => setQuickBody(e.target.value)}
                  rows={8}
                  className="bg-surface-2 border border-border-subtle-2 rounded-lg px-[13px] py-[9px] text-[13px] text-text-primary outline-none w-full focus:border-accent resize-y"
                  placeholder={t("titles.quickMessagePlaceholder")}
                />
                <div className="text-[11px] text-text-muted mt-1">
                  {t("titles.quickVariablesLabel")}: {t("titles.quickVariablesList")}
                </div>
              </div>
            </div>
          ) : (
            <div className="text-xs text-text-muted bg-surface-2 border border-border-subtle rounded-lg p-3">
              {t("titles.quickTemplateNone")}
            </div>
          )}
        </div>
      </Modal>

      <Modal
        open={historyOpen}
        onClose={() => setHistoryOpen(false)}
        title={`${t("titles.historyTitle")} — ${historyTitleCode}`}
        maxWidth={760}
        footer={<Button variant="secondary" onClick={() => setHistoryOpen(false)}>{t("common.close")}</Button>}
      >
        {historyLoading ? (
          <div className="text-sm text-text-muted py-4">{t("titles.historyLoading")}</div>
        ) : historyItems.length === 0 ? (
          <div className="text-sm text-text-muted py-4">{t("titles.historyEmpty")}</div>
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
