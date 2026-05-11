import { useEffect, useMemo, useState } from "react";
import { t } from "../i18n";
import { ICONS } from "../utils/icons";
import { Badge, Button, Modal, FormInput, FormSelect } from "../components/UI";
import {
  ApiError,
  createClient,
  createContact,
  deleteContact,
  getClients,
  getContacts,
  updateClientDispatchPreference,
  updateContact,
  type ClientResponse,
  type ContactResponse,
  type StoredSession,
  type UpdateClientDispatchPreferenceRequest,
  type UpsertContactRequest,
} from "../services/api";
import type { ShowToast } from "../types";

const formatCnpj = (v: string) => {
  const d = v.replace(/\D/g, "").substring(0, 14);
  if (d.length <= 2) return d;
  if (d.length <= 5) return `${d.slice(0, 2)}.${d.slice(2)}`;
  if (d.length <= 8) return `${d.slice(0, 2)}.${d.slice(2, 5)}.${d.slice(5)}`;
  if (d.length <= 12) return `${d.slice(0, 2)}.${d.slice(2, 5)}.${d.slice(5, 8)}/${d.slice(8)}`;
  return `${d.slice(0, 2)}.${d.slice(2, 5)}.${d.slice(5, 8)}/${d.slice(8, 12)}-${d.slice(12, 14)}`;
};

const formatPhone = (v: string) => {
  const raw = v.trim();
  let d = raw.replace(/\D/g, "").substring(0, 13);
  if (d.length === 0) return "";

  if (d.startsWith("00")) d = d.slice(2);

  const explicitInternational = raw.startsWith("+");
  if (explicitInternational && !d.startsWith("55")) {
    return `+${d}`;
  }

  if (d.startsWith("55")) {
    const local = d.slice(2);
    if (local.length === 0) return "+55";
    if (local.length <= 2) return `+55 (${local}`;
    if (local.length <= 6) return `+55 (${local.slice(0, 2)}) ${local.slice(2)}`;
    if (local.length <= 10) return `+55 (${local.slice(0, 2)}) ${local.slice(2, 6)}-${local.slice(6)}`;
    return `+55 (${local.slice(0, 2)}) ${local.slice(2, 7)}-${local.slice(7, 11)}`;
  }

  if (d.length <= 2) return `(${d}`;
  if (d.length <= 6) return `(${d.slice(0, 2)}) ${d.slice(2)}`;
  if (d.length <= 10) return `(${d.slice(0, 2)}) ${d.slice(2, 6)}-${d.slice(6)}`;
  if (d.length <= 11) return `(${d.slice(0, 2)}) ${d.slice(2, 7)}-${d.slice(7, 11)}`;

  return `+${d}`;
};

const normalizePhone = (v: string): string | undefined => {
  const raw = v.trim();
  const explicitInternational = raw.startsWith("+");

  let d = raw.replace(/\D/g, "");
  if (d.startsWith("00")) d = d.slice(2);
  if (d.length < 10 || d.length > 13) return undefined;

  if (!explicitInternational && (d.length === 10 || d.length === 11)) {
    d = `55${d}`;
  }

  return `+${d}`;
};

interface FormState {
  clientId: string;
  name: string;
  department: string;
  email: string;
  whatsAppPhone: string;
  isPrimary: boolean;
}

const defaultForm: FormState = {
  clientId: "",
  name: "",
  department: "Finance",
  email: "",
  whatsAppPhone: "",
  isPrimary: true,
};

interface CompanyRow {
  client: ClientResponse;
  contacts: ContactResponse[];
  primaryContact: ContactResponse | null;
  status: "pending" | "partial" | "complete";
}

type DispatchMode = "Primary" | "All" | "Selected";

const normalizeDispatchMode = (client: ClientResponse): DispatchMode => {
  if (client.dispatchMode === "All" || client.dispatchMode === "Selected" || client.dispatchMode === "Primary")
    return client.dispatchMode;

  return client.sendToAllContacts ? "All" : "Primary";
};

const getDepartmentLabel = (department: string) => {
  const normalized = department.trim().toLowerCase();
  if (normalized === "finance") return t("contactsPage.departments.finance");
  if (normalized === "commercial") return t("contactsPage.departments.commercial");
  if (normalized === "management") return t("contactsPage.departments.management");
  if (normalized === "other") return t("contactsPage.departments.other");
  if (normalized === "purchasing") return t("contactsPage.departments.purchasing");
  if (normalized === "partner") return t("contactsPage.departments.partner");
  return department;
};

export const PageContacts = ({
  showToast,
  session,
  selectedTenantId,
  onSubtitleChange,
}: {
  showToast: ShowToast;
  session: StoredSession;
  selectedTenantId?: string;
  onSubtitleChange?: (subtitle: string) => void;
}) => {
  const [clients, setClients] = useState<ClientResponse[]>([]);
  const [contacts, setContacts] = useState<ContactResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState<"all" | "pending">("all");
  const [page, setPage] = useState(1);
  const pageSize = 20;
  const [companyDetailsClientId, setCompanyDetailsClientId] = useState<string | null>(null);
  const [updatingDispatchClientId, setUpdatingDispatchClientId] = useState<string | null>(null);
  const [settingPrimaryContactId, setSettingPrimaryContactId] = useState<string | null>(null);
  const [deletingContactId, setDeletingContactId] = useState<string | null>(null);
  const [dispatchModeDraft, setDispatchModeDraft] = useState<DispatchMode>("Primary");
  const [dispatchSelectedContactIdsDraft, setDispatchSelectedContactIdsDraft] = useState<string[]>([]);

  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<ContactResponse | null>(null);
  const [form, setForm] = useState<FormState>(defaultForm);
  const [saving, setSaving] = useState(false);

  const [newClientName, setNewClientName] = useState("");
  const [newClientCnpj, setNewClientCnpj] = useState("");

  const tenantId = session.role === "Master" ? selectedTenantId : undefined;
  const requiresTenantSelection = session.role === "Master" && !tenantId;

  const loadAll = async () => {
    if (requiresTenantSelection) {
      setClients([]);
      setContacts([]);
      setLoading(false);
      return;
    }

    try {
      setLoading(true);
      const companyList = await getClients(tenantId);
      setClients(companyList);

      const contactList = await getContacts(tenantId);
      setContacts(contactList);
    } catch {
      showToast(`${ICONS.cross} ${t("contactsPage.errors.load")}`, "error");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void loadAll();
  }, [tenantId, requiresTenantSelection]);

  const isPendingStatus = (status: CompanyRow["status"]) => status !== "complete";

  const baseCompanyRows = useMemo<CompanyRow[]>(() => {
    const s = search.trim().toLowerCase();

    const hasEmail = (contact?: ContactResponse | null) => !!contact?.email?.trim();
    const hasWhatsApp = (contact?: ContactResponse | null) => !!contact?.whatsAppPhone?.trim();

    return clients
      .map(client => {
        const companyContacts = contacts
          .filter(c => c.clientId === client.id)
          .sort((a, b) => Number(b.isPrimary) - Number(a.isPrimary) || a.name.localeCompare(b.name, "pt-BR"));

        const primaryContact = companyContacts.find(c => c.isPrimary) ?? companyContacts[0] ?? null;
        const dispatchMode = normalizeDispatchMode(client);
        const selectedContacts = companyContacts.filter(c => (client.selectedContactIds ?? []).includes(c.id));
        const contactsForStatus = dispatchMode === "All"
          ? companyContacts
          : dispatchMode === "Selected"
            ? selectedContacts
            : primaryContact ? [primaryContact] : [];

        let status: "pending" | "partial" | "complete" = "pending";
        const anyEmail = contactsForStatus.some(c => hasEmail(c));
        const anyWhatsApp = contactsForStatus.some(c => hasWhatsApp(c));
        status = anyEmail && anyWhatsApp ? "complete" : anyEmail || anyWhatsApp ? "partial" : "pending";

        return { client, contacts: companyContacts, primaryContact, status };
      })
      .filter(row => {
        if (!s) return true;

        return (
          row.client.legalName.toLowerCase().includes(s)
          || row.client.taxId.includes(s)
          || (row.primaryContact?.name ?? "").toLowerCase().includes(s)
          || (row.primaryContact?.email ?? "").toLowerCase().includes(s)
          || (row.primaryContact?.whatsAppPhone ?? "").toLowerCase().includes(s)
        );
      })
      .sort((a, b) => a.client.legalName.localeCompare(b.client.legalName, "pt-BR"));
  }, [clients, contacts, search]);

  const pendingCompanyCount = useMemo(
    () => baseCompanyRows.filter(row => isPendingStatus(row.status)).length,
    [baseCompanyRows]
  );

  useEffect(() => {
    if (!onSubtitleChange) return;
    const nextSubtitle = requiresTenantSelection
      ? t("contactsPage.validation.selectTenantView")
      : loading
        ? t("common.loading")
        : pendingCompanyCount === 0
          ? t("contactsPage.summary.noPending")
          : `${pendingCompanyCount} ${t("contactsPage.summary.pendingCompanies")}`;

    onSubtitleChange(nextSubtitle);

    return () => onSubtitleChange("");
  }, [onSubtitleChange, pendingCompanyCount, requiresTenantSelection, loading]);

  const companyRows = useMemo(
    () => (statusFilter === "pending" ? baseCompanyRows.filter(row => isPendingStatus(row.status)) : baseCompanyRows),
    [baseCompanyRows, statusFilter]
  );

  useEffect(() => {
    setPage(1);
  }, [search, statusFilter, baseCompanyRows.length]);

  const totalPages = Math.max(1, Math.ceil(companyRows.length / pageSize));
  const pagedRows = useMemo(
    () => companyRows.slice((page - 1) * pageSize, page * pageSize),
    [companyRows, page]
  );

  const selectedCompany = useMemo(
    () => clients.find(c => c.id === companyDetailsClientId) ?? null,
    [clients, companyDetailsClientId]
  );

  const selectedCompanyContacts = useMemo(() => {
    if (!companyDetailsClientId) return [];

    return contacts
      .filter(c => c.clientId === companyDetailsClientId)
      .sort((a, b) => Number(b.isPrimary) - Number(a.isPrimary) || a.name.localeCompare(b.name, "pt-BR"));
  }, [contacts, companyDetailsClientId]);

  useEffect(() => {
    if (!selectedCompany) return;

    setDispatchModeDraft(normalizeDispatchMode(selectedCompany));
    setDispatchSelectedContactIdsDraft(selectedCompany.selectedContactIds ?? []);
  }, [selectedCompany]);

  const openNew = () => {
    setEditing(null);
    setForm(defaultForm);
    setNewClientName("");
    setNewClientCnpj("");
    setModalOpen(true);
  };

  const openNewForClient = (clientId: string) => {
    setCompanyDetailsClientId(null);
    setEditing(null);
    setForm({ ...defaultForm, clientId, isPrimary: false });
    setNewClientName("");
    setNewClientCnpj("");
    setModalOpen(true);
  };

  const openEdit = (contact: ContactResponse) => {
    setEditing(contact);
    setForm({
      clientId: contact.clientId,
      name: contact.name,
      department: contact.department,
      email: contact.email ?? "",
      whatsAppPhone: contact.whatsAppPhone ?? "",
      isPrimary: contact.isPrimary,
    });
    setModalOpen(true);
  };

  const openEditFromCompany = (contact: ContactResponse) => {
    setCompanyDetailsClientId(null);
    openEdit(contact);
  };

  const handleSetDispatchPreference = async (clientId: string, mode: DispatchMode, selectedContactIds: string[]) => {
    if (requiresTenantSelection) {
      showToast(`${ICONS.warning} ${t("contactsPage.validation.selectTenantDispatch")}`, "warn");
      return;
    }

    if (mode === "Selected" && selectedContactIds.length === 0) {
      showToast(`${ICONS.warning} ${t("contactsPage.validation.customRequiresContact")}`, "warn");
      return;
    }

    const payload: UpdateClientDispatchPreferenceRequest = {
      dispatchMode: mode,
      sendToAllContacts: mode === "All",
      selectedContactIds: mode === "Selected" ? selectedContactIds : [],
    };

    try {
      setUpdatingDispatchClientId(clientId);
      const updated = await updateClientDispatchPreference(clientId, payload, tenantId);
      setClients(prev => prev.map(c => c.id === updated.id ? updated : c));
      showToast(
        `${ICONS.checkmark} ${t("contactsPage.messages.dispatchUpdated")} ${updated.dispatchMode === "All" ? t("contactsPage.dispatchModes.all") : updated.dispatchMode === "Selected" ? t("contactsPage.dispatchModes.selected") : t("contactsPage.dispatchModes.primary")}.`,
        "success"
      );
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : t("contactsPage.errors.updateDispatch");
      showToast(`${ICONS.cross} ${msg}`, "error");
    } finally {
      setUpdatingDispatchClientId(null);
    }
  };

  const handleSetPrimaryContact = async (contact: ContactResponse) => {
    if (requiresTenantSelection) {
      showToast(`${ICONS.warning} ${t("contactsPage.validation.selectTenantContacts")}`, "warn");
      return;
    }

    try {
      setSettingPrimaryContactId(contact.id);

      const payload: UpsertContactRequest = {
        name: contact.name,
        email: contact.email?.trim() ?? "",
        whatsAppPhone: contact.whatsAppPhone ?? undefined,
        department: contact.department,
        isPrimary: true,
      };

      await updateContact(contact.clientId, contact.id, payload, tenantId);
      showToast(`${ICONS.checkmark} ${t("contactsPage.messages.primaryUpdated")}`, "success");
      await loadAll();
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : t("contactsPage.errors.setPrimary");
      showToast(`${ICONS.cross} ${msg}`, "error");
    } finally {
      setSettingPrimaryContactId(null);
    }
  };

  const handleDeleteContact = async (contact: ContactResponse) => {
    if (requiresTenantSelection) {
      showToast(`${ICONS.warning} ${t("contactsPage.validation.selectTenantContacts")}`, "warn");
      return;
    }

    const confirmed = window.confirm(`${t("contactsPage.confirm.deleteContact")} "${contact.name}"?`);
    if (!confirmed)
      return;

    try {
      setDeletingContactId(contact.id);
      await deleteContact(contact.clientId, contact.id, tenantId);
      showToast(`${ICONS.checkmark} ${t("contactsPage.messages.deleted")}`, "success");
      await loadAll();
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : t("contactsPage.errors.delete");
      showToast(`${ICONS.cross} ${msg}`, "error");
    } finally {
      setDeletingContactId(null);
    }
  };

  const handleSave = async () => {
    if (requiresTenantSelection) {
      showToast(`${ICONS.warning} ${t("contactsPage.validation.selectTenantManage")}`, "warn");
      return;
    }

    const hasEmail = form.email.trim().length > 0;
    const normalizedPhone = normalizePhone(form.whatsAppPhone);

    if (!form.name || (!hasEmail && !normalizedPhone)) {
      showToast(`${ICONS.warning} ${t("contactsPage.validation.requireContactChannel")}`, "warn");
      return;
    }

    try {
      setSaving(true);
      const payload: UpsertContactRequest = {
        name: form.name,
        email: form.email.trim(),
        whatsAppPhone: normalizedPhone,
        department: form.department,
        isPrimary: form.isPrimary,
      };

      let clientId = form.clientId;

      if (!clientId) {
        if (!newClientName || !newClientCnpj) {
          showToast(`${ICONS.warning} ${t("contactsPage.validation.selectOrCreateClient")}`, "warn");
          setSaving(false);
          return;
        }

        const created = await createClient({
          legalName: newClientName,
          taxId: newClientCnpj.replace(/\D/g, ""),
        }, tenantId);
        clientId = created.id;
      }

      if (editing) {
        await updateContact(clientId, editing.id, payload, tenantId);
      } else {
        await createContact(clientId, payload, tenantId);
      }

      showToast(`${ICONS.checkmark} ${t("toast.contactSaved")}`, "success");
      setModalOpen(false);
      await loadAll();
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : t("contactsPage.errors.save");
      showToast(`${ICONS.cross} ${msg}`, "error");
    } finally {
      setSaving(false);
    }
  };

  const headers = [
    t("contacts.col.companyCnpj"),
    t("contacts.col.contact"),
    t("contacts.col.whatsapp"),
    t("contacts.col.titles"),
    t("contacts.col.status"),
    t("contacts.col.actions"),
  ];

  return (
    <div className="animate-fade-up">
      <div className="flex items-center gap-2.5 mb-5">
        <input
          value={search}
          onChange={e => setSearch(e.target.value)}
          placeholder={`${ICONS.search} ${t("contacts.searchPlaceholder")}`}
          className="bg-surface-2 border border-border-subtle-2 rounded-lg px-[13px] py-[9px] text-[13px] text-text-primary outline-none flex-1 focus:border-accent"
          disabled={requiresTenantSelection}
        />
        <div className="min-w-[190px]">
          <FormSelect
            value={statusFilter}
            onChange={e => setStatusFilter(e.target.value as "all" | "pending")}
            disabled={requiresTenantSelection}
            aria-label={t("contactsPage.filters.statusLabel")}
          >
            <option value="all">{t("contactsPage.filters.statusAll")}</option>
            <option value="pending">{t("contactsPage.filters.statusPending")}</option>
          </FormSelect>
        </div>
        <Button
          variant="primary"
          onClick={openNew}
          disabled={requiresTenantSelection}
          title={requiresTenantSelection ? t("contactsPage.validation.selectTenantManage") : undefined}
        >
          {t("contacts.newContact")}
        </Button>
      </div>

      {requiresTenantSelection && (
        <div className="rounded-xl border border-border-subtle bg-surface-2/60 px-4 py-3 text-sm text-text-secondary mb-4">
          {t("contactsPage.validation.selectTenantView")}
        </div>
      )}

      <div className="text-xs text-text-muted mb-2">
        {companyRows.length} {t("contactsPage.table.found")}
      </div>

      <div className="rounded-xl border border-border-subtle overflow-hidden">
        <table className="w-full border-collapse text-[13px]">
          <thead>
            <tr className="bg-surface-2">
              {headers.map(h => (
                <th key={h} className="px-4 py-[11px] text-left text-[10.5px] font-bold tracking-[0.5px] uppercase text-text-muted border-b border-border-subtle whitespace-nowrap">{h}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr><td colSpan={6} className="text-center py-12 text-sm text-text-muted">{t("common.loading")}</td></tr>
            ) : companyRows.length === 0 ? (
              <tr><td colSpan={6} className="text-center py-8 text-sm text-text-muted">{t("contactsPage.table.empty")}</td></tr>
            ) : pagedRows.map(({ client, contacts: companyContacts, primaryContact, status }) => (
              <tr key={client.id} className="border-b border-border-subtle hover:bg-surface-2/50 transition-colors">
                <td className="px-4 py-[13px]">
                  <div className="font-semibold">{client.legalName}</div>
                  <div className="text-[11px] text-text-muted mt-0.5">{client.taxId}</div>
                </td>
                <td className="px-4 py-[13px] text-xs text-text-secondary">
                  {primaryContact ? (
                    <>
                      <div className="font-semibold text-text-primary flex items-center gap-1.5">
                        {primaryContact.name}
                        <span className="text-[10px] font-bold px-1.5 py-0.5 rounded-full bg-success/10 text-success">{t("contactsPage.labels.primary")}</span>
                      </div>
                      <div className="mt-0.5">{primaryContact.email || "—"}</div>
                    </>
                  ) : (
                    <span className="text-text-muted">{t("contactsPage.table.noPrimary")}</span>
                  )}
                </td>
                <td className="px-4 py-[13px] text-xs text-text-secondary">{primaryContact?.whatsAppPhone || "—"}</td>
                <td className="px-4 py-[13px] text-xs text-text-secondary">{client.titleCount} {client.titleCount === 1 ? t("contacts.titleSingular") : t("contacts.titlePlural")}</td>
                <td className="px-4 py-[13px]"><Badge status={status} /></td>
                <td className="px-4 py-[13px]">
                  <div className="flex items-center gap-1.5">
                    <Button size="sm" variant="secondary" onClick={() => setCompanyDetailsClientId(client.id)}>
                      {ICONS.eye} {t("contactsPage.actions.viewContacts")} ({companyContacts.length})
                    </Button>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {totalPages > 1 && (
        <div className="flex items-center justify-center gap-2 mt-4">
          <Button size="sm" variant="secondary" onClick={() => setPage(p => Math.max(1, p - 1))} disabled={page === 1}>←</Button>
          <span className="text-sm text-text-secondary">{page} / {totalPages}</span>
          <Button size="sm" variant="secondary" onClick={() => setPage(p => Math.min(totalPages, p + 1))} disabled={page === totalPages}>→</Button>
        </div>
      )}

      <Modal
        open={!!companyDetailsClientId}
        onClose={() => setCompanyDetailsClientId(null)}
        title={selectedCompany ? `${t("contactsPage.modal.companyContactsPrefix")} ${selectedCompany.legalName}` : t("contactsPage.modal.companyContactsTitle")}
        maxWidth={860}
        footer={<>
          <Button variant="secondary" onClick={() => setCompanyDetailsClientId(null)}>{t("common.cancel")}</Button>
          {selectedCompany && (
            <Button variant="primary" onClick={() => openNewForClient(selectedCompany.id)}>
              {ICONS.plus} {t("contactsPage.actions.addContact")}
            </Button>
          )}
        </>}
      >
        <div className="flex flex-col gap-3">
          {selectedCompany && (
            <div className="rounded-xl border border-border-subtle bg-surface-2 px-4 py-3">
              <div className="flex items-center justify-between gap-3 flex-wrap">
                <div>
                  <div className="text-[10px] font-bold uppercase tracking-[0.6px] text-text-muted">{t("contactsPage.dispatch.title")}</div>
                  <div className="text-sm font-semibold text-text-primary mt-0.5">{t("contactsPage.dispatch.subtitle")}</div>
                  <div className="text-xs text-text-muted mt-1">
                    {t("contactsPage.dispatch.currentMode")} {dispatchModeDraft === "All" ? t("contactsPage.dispatchModes.all") : dispatchModeDraft === "Selected" ? t("contactsPage.dispatchModes.selected") : t("contactsPage.dispatchModes.primary")}
                  </div>
                </div>

                <div className="min-w-[230px]">
                  <select
                    value={dispatchModeDraft}
                    onChange={e => setDispatchModeDraft(e.target.value as DispatchMode)}
                    disabled={updatingDispatchClientId === selectedCompany.id}
                    className="w-full bg-surface border border-border-subtle-2 rounded-lg px-3 py-2 text-sm text-text-primary outline-none focus:border-accent cursor-pointer"
                  >
                    <option value="Primary">{t("contactsPage.dispatchModes.primary")}</option>
                    <option value="All">{t("contactsPage.dispatchModes.all")}</option>
                    <option value="Selected">{t("contactsPage.dispatchModes.selected")}</option>
                  </select>
                </div>
              </div>

              {dispatchModeDraft === "Selected" && (
                <div className="mt-3 rounded-lg border border-border-subtle bg-surface px-3 py-2.5">
                  <div className="text-[11px] font-bold uppercase tracking-[0.6px] text-text-muted mb-2">{t("contactsPage.dispatch.selectContacts")}</div>
                  <div className="flex flex-col gap-1.5">
                    {selectedCompanyContacts.length === 0 ? (
                      <div className="text-xs text-text-muted">{t("contactsPage.dispatch.noContacts")}</div>
                    ) : (
                      selectedCompanyContacts.map(contact => (
                        <label key={contact.id} className="flex items-center gap-2 text-xs text-text-secondary">
                          <input
                            type="checkbox"
                            checked={dispatchSelectedContactIdsDraft.includes(contact.id)}
                            onChange={e => setDispatchSelectedContactIdsDraft(prev => e.target.checked
                              ? [...prev, contact.id]
                              : prev.filter(id => id !== contact.id))}
                            className="accent-accent"
                          />
                          <span className="font-semibold text-text-primary">{contact.name}</span>
                          {contact.isPrimary && <span className="text-[10px] px-1.5 py-0.5 rounded-full bg-success/10 text-success font-bold">{t("contactsPage.labels.primary")}</span>}
                        </label>
                      ))
                    )}
                  </div>
                </div>
              )}

              <div className="mt-3 flex justify-end">
                <Button
                  size="sm"
                  variant="primary"
                  onClick={() => void handleSetDispatchPreference(selectedCompany.id, dispatchModeDraft, dispatchSelectedContactIdsDraft)}
                  disabled={updatingDispatchClientId === selectedCompany.id}
                >
                  {updatingDispatchClientId === selectedCompany.id ? t("contactsPage.actions.saving") : t("contactsPage.actions.saveDispatch")}
                </Button>
              </div>
            </div>
          )}

          {selectedCompanyContacts.length === 0 ? (
            <div className="rounded-xl border border-border-subtle bg-surface-2 px-4 py-6 text-center text-sm text-text-muted">
              {t("contactsPage.company.noContacts")}
            </div>
          ) : (
            <>
              <div className="text-xs text-text-muted bg-surface-2 border border-border-subtle rounded-xl px-4 py-2.5">
                {t("contactsPage.company.primaryHelp")}
              </div>
              <div className="flex flex-col gap-2">
                {selectedCompanyContacts.map(contact => (
                  <div key={contact.id} className="bg-surface-2 border border-border-subtle rounded-xl px-4 py-3 flex items-start justify-between gap-3">
                    <div className="min-w-0 flex-1">
                      <div className="font-semibold text-[15px] flex items-center gap-2 flex-wrap">
                        <span className="text-text-primary">{contact.name}</span>
                        {contact.isPrimary && (
                          <span className="text-[10px] font-bold px-1.5 py-0.5 rounded-full bg-success/10 text-success">{t("contactsPage.labels.primary")}</span>
                        )}
                        <span className="text-[10px] font-bold uppercase tracking-wide px-1.5 py-0.5 rounded-full bg-surface border border-border-subtle text-text-muted">
                          {getDepartmentLabel(contact.department)}
                        </span>
                      </div>
                      <div className="text-xs text-text-secondary mt-1 break-all">{contact.email || t("contactsPage.labels.noEmail")}</div>
                      <div className="text-xs text-text-secondary mt-0.5">{contact.whatsAppPhone || t("contactsPage.labels.noWhatsApp")}</div>
                    </div>

                    <div className="flex items-center gap-1.5 shrink-0">
                      {!contact.isPrimary && (
                        <Button
                          size="sm"
                          variant="secondary"
                          onClick={() => void handleSetPrimaryContact(contact)}
                          disabled={settingPrimaryContactId === contact.id}
                        >
                          {settingPrimaryContactId === contact.id ? t("contactsPage.actions.saving") : t("contactsPage.actions.makePrimary")}
                        </Button>
                      )}
                      <Button size="sm" variant="secondary" onClick={() => openEditFromCompany(contact)}>
                        {ICONS.pencil} {t("contacts.edit")}
                      </Button>
                      <Button
                        size="sm"
                        variant="danger"
                        onClick={() => void handleDeleteContact(contact)}
                        disabled={deletingContactId === contact.id}
                      >
                        {deletingContactId === contact.id ? t("contactsPage.actions.deleting") : t("contactsPage.actions.delete")}
                      </Button>
                    </div>
                  </div>
                ))}
              </div>
            </>
          )}
        </div>
      </Modal>

      <Modal
        open={modalOpen}
        onClose={() => setModalOpen(false)}
        title={editing ? t("contactsPage.modal.editTitle") : t("contacts.modalTitle")}
        footer={<>
          <Button variant="secondary" onClick={() => setModalOpen(false)}>{t("common.cancel")}</Button>
          <Button variant="primary" onClick={handleSave}>{saving ? t("contactsPage.actions.saving") : t("contacts.saveContact")}</Button>
        </>}
      >
        <div className="grid grid-cols-2 gap-3.5">
          <div className="col-span-2">
            <label className="block text-[11px] font-bold tracking-[0.6px] uppercase text-text-muted mb-[5px]">{t("contactsPage.labels.company")}</label>
            <select
              value={form.clientId}
              onChange={e => setForm(f => ({ ...f, clientId: e.target.value }))}
              className="bg-surface-2 border border-border-subtle-2 rounded-lg px-[13px] py-[9px] text-[13px] text-text-primary outline-none w-full focus:border-accent"
            >
              <option value="">{t("contactsPage.labels.newCompany")}</option>
              {clients.map(c => (
                <option key={c.id} value={c.id}>{c.legalName} — {c.taxId}</option>
              ))}
            </select>
          </div>

          {!form.clientId && (
            <>
              <FormInput label={t("contactsPage.labels.companyNameRequired")} value={newClientName} onChange={e => setNewClientName(e.target.value)} placeholder={t("contactsPage.placeholders.companyName")} />
              <FormInput label={t("contactsPage.labels.cnpjRequired")} value={newClientCnpj} onChange={e => setNewClientCnpj(formatCnpj(e.target.value))} placeholder={t("common.cnpjPlaceholder")} />
            </>
          )}

          <FormSelect
            label={t("contacts.contactType")}
            value={form.department}
            onChange={e => setForm(f => ({ ...f, department: e.target.value }))}
          >
            <option value="Finance">{t("contacts.typeFinance")}</option>
            <option value="Purchasing">{t("contacts.typePurchasing")}</option>
            <option value="Partner">{t("contacts.typePartner")}</option>
            <option value="Other">{t("contacts.typeOther")}</option>
          </FormSelect>

          <div className="flex items-center gap-2 mt-5">
            <input
              type="checkbox"
              id="primary"
              checked={form.isPrimary}
              onChange={e => setForm(f => ({ ...f, isPrimary: e.target.checked }))}
              className="accent-accent"
            />
            <label htmlFor="primary" className="text-sm text-text-secondary cursor-pointer">{t("contactsPage.labels.primaryContact")}</label>
          </div>

          <div className="col-span-2">
            <FormInput label={`${t("contacts.contactName")} *`} value={form.name} onChange={e => setForm(f => ({ ...f, name: e.target.value }))} placeholder={t("contacts.fullName")} />
          </div>
          <div className="col-span-2">
            <FormInput label={t("contacts.col.email")} type="email" value={form.email} onChange={e => setForm(f => ({ ...f, email: e.target.value }))} placeholder={t("contactsPage.placeholders.contactEmail")} />
          </div>
          <div className="col-span-2">
            <FormInput label={t("contacts.mobileWhatsapp")} value={form.whatsAppPhone} onChange={e => setForm(f => ({ ...f, whatsAppPhone: formatPhone(e.target.value) }))} placeholder={t("contactsPage.placeholders.whatsAppPhone")} />
          </div>
        </div>
      </Modal>
    </div>
  );
};
