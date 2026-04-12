import { useEffect, useState, useMemo } from "react";
import { t } from "../i18n";
import { ICONS } from "../utils/icons";
import { Badge, Button, Modal, FormInput, FormSelect } from "../components/UI";
import {
  ApiError, getClients, getContactsByClient, createContact, updateContact, createClient,
  type ClientResponse, type ContactResponse, type UpsertContactRequest,
} from "../services/api";
import type { ShowToast } from "../types";

const formatCnpj = (v: string) => {
  const d = v.replace(/\D/g, "").substring(0, 14);
  if (d.length <= 2) return d;
  if (d.length <= 5) return `${d.slice(0,2)}.${d.slice(2)}`;
  if (d.length <= 8) return `${d.slice(0,2)}.${d.slice(2,5)}.${d.slice(5)}`;
  if (d.length <= 12) return `${d.slice(0,2)}.${d.slice(2,5)}.${d.slice(5,8)}/${d.slice(8)}`;
  return `${d.slice(0,2)}.${d.slice(2,5)}.${d.slice(5,8)}/${d.slice(8,12)}-${d.slice(12,14)}`;
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
    if (local.length <= 6) return `+55 (${local.slice(0,2)}) ${local.slice(2)}`;
    if (local.length <= 10) return `+55 (${local.slice(0,2)}) ${local.slice(2,6)}-${local.slice(6)}`;
    return `+55 (${local.slice(0,2)}) ${local.slice(2,7)}-${local.slice(7,11)}`;
  }

  if (d.length <= 2) return `(${d}`;
  if (d.length <= 6) return `(${d.slice(0,2)}) ${d.slice(2)}`;
  if (d.length <= 10) return `(${d.slice(0,2)}) ${d.slice(2,6)}-${d.slice(6)}`;
  if (d.length <= 11) return `(${d.slice(0,2)}) ${d.slice(2,7)}-${d.slice(7,11)}`;

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
  clientId: "", name: "", department: "Finance",
  email: "", whatsAppPhone: "", isPrimary: true,
};

export const PageContacts = ({ showToast }: { showToast: ShowToast }) => {
  const [clients, setClients] = useState<ClientResponse[]>([]);
  const [contacts, setContacts] = useState<ContactResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState("");
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<ContactResponse | null>(null);
  const [form, setForm] = useState<FormState>(defaultForm);
  const [saving, setSaving] = useState(false);
  // new client inline
  const [newClientName, setNewClientName] = useState("");
  const [newClientCnpj, setNewClientCnpj] = useState("");

  const loadAll = async () => {
    try {
      setLoading(true);
      const cls = await getClients();
      setClients(cls);
      // Batch all contact loads (avoid N+1 in UI)
      const batches = await Promise.all(cls.map(c => getContactsByClient(c.id).catch(() => [])));
      setContacts(batches.flat());
    } catch {
      showToast(`${ICONS.cross} Erro ao carregar contatos.`, "error");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { void loadAll(); }, []);

  const filtered = useMemo(() => {
    if (!search) return contacts;
    const s = search.toLowerCase();
    return contacts.filter(c =>
      c.companyName.toLowerCase().includes(s) ||
      c.companyTaxId.includes(s) ||
      c.name.toLowerCase().includes(s)
    );
  }, [contacts, search]);

  const openNew = () => {
    setEditing(null);
    setForm(defaultForm);
    setNewClientName(""); setNewClientCnpj("");
    setModalOpen(true);
  };

  const openEdit = (c: ContactResponse) => {
    setEditing(c);
    setForm({
      clientId: c.clientId,
      name: c.name,
      department: c.department,
      email: c.email ?? "",
      whatsAppPhone: c.whatsAppPhone ?? "",
      isPrimary: c.isPrimary,
    });
    setModalOpen(true);
  };

  const handleSave = async () => {
    const hasEmail = form.email.trim().length > 0;
    const normalizedPhone = normalizePhone(form.whatsAppPhone);

    if (!form.name || (!hasEmail && !normalizedPhone)) {
      showToast(`${ICONS.warning} Nome e pelo menos um canal de contato são obrigatórios.`, "warn");
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

      // If no client selected, create one inline
      if (!clientId) {
        if (!newClientName || !newClientCnpj) {
          showToast(`${ICONS.warning} Informe o cliente ou crie um novo.`, "warn");
          setSaving(false);
          return;
        }
        const created = await createClient({ legalName: newClientName, taxId: newClientCnpj.replace(/\D/g,"") });
        clientId = created.id;
      }

      if (editing) {
        await updateContact(clientId, editing.id, payload);
      } else {
        await createContact(clientId, payload);
      }

      showToast(`${ICONS.checkmark} ${t("toast.contactSaved")}`, "success");
      setModalOpen(false);
      void loadAll();
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : "Erro ao salvar contato.";
      showToast(`${ICONS.cross} ${msg}`, "error");
    } finally {
      setSaving(false);
    }
  };

  const headers = [
    t("contacts.col.companyCnpj"), t("contacts.col.contact"), t("contacts.col.type"),
    t("contacts.col.email"), t("contacts.col.whatsapp"), t("contacts.col.titles"),
    t("contacts.col.status"), t("contacts.col.actions"),
  ];

  return (
    <div className="animate-fade-up">
      <div className="flex items-center gap-2.5 mb-5">
        <input
          value={search}
          onChange={e => setSearch(e.target.value)}
          placeholder={`${ICONS.search} ${t("contacts.searchPlaceholder")}`}
          className="bg-surface-2 border border-border-subtle-2 rounded-lg px-[13px] py-[9px] text-[13px] text-text-primary outline-none flex-1 focus:border-accent"
        />
        <Button variant="primary" onClick={openNew}>{t("contacts.newContact")}</Button>
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
              <tr><td colSpan={8} className="text-center py-12 text-sm text-text-muted">Carregando...</td></tr>
            ) : filtered.length === 0 ? (
              <tr><td colSpan={8} className="text-center py-8 text-sm text-text-muted">Nenhum contato encontrado.</td></tr>
            ) : filtered.map((c, i) => (
              <tr key={i} className="border-b border-border-subtle hover:bg-surface-2/50 transition-colors">
                <td className="px-4 py-[13px]">
                  <div className="font-semibold">{c.companyName}</div>
                  <div className="text-[11px] text-text-muted mt-0.5">{c.companyTaxId}</div>
                </td>
                <td className={`px-4 py-[13px] ${!c.name ? "text-text-muted" : ""}`}>{c.name || "—"}</td>
                <td className="px-4 py-[13px]">
                  {c.department !== "—" ? (
                    <span className="bg-surface-2 border border-border-subtle text-[11px] px-2 py-0.5 rounded-full text-text-secondary">{c.department}</span>
                  ) : <span className="text-text-muted">—</span>}
                </td>
                <td className={`px-4 py-[13px] text-xs ${!c.email ? "text-text-muted" : "text-text-secondary"}`}>{c.email || "—"}</td>
                <td className={`px-4 py-[13px] text-xs ${!c.whatsAppPhone ? "text-text-muted" : "text-text-secondary"}`}>{c.whatsAppPhone || "—"}</td>
                <td className="px-4 py-[13px] text-xs text-text-secondary">{c.titleCount} {c.titleCount === 1 ? t("contacts.titleSingular") : t("contacts.titlePlural")}</td>
                <td className="px-4 py-[13px]"><Badge status={c.status} /></td>
                <td className="px-4 py-[13px]">
                  <Button size="sm" variant={c.status === "pending" ? "primary" : "secondary"} onClick={() => openEdit(c)}>
                    {c.status === "pending" ? t("contacts.addData") : `${ICONS.pencil} ${t("contacts.edit")}`}
                  </Button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <Modal open={modalOpen} onClose={() => setModalOpen(false)} title={editing ? "Editar Contato" : t("contacts.modalTitle")}
        footer={<>
          <Button variant="secondary" onClick={() => setModalOpen(false)}>{t("common.cancel")}</Button>
          <Button variant="primary" onClick={handleSave}>{saving ? "Salvando..." : t("contacts.saveContact")}</Button>
        </>}
      >
        <div className="grid grid-cols-2 gap-3.5">
          {/* Client selector */}
          <div className="col-span-2">
            <label className="block text-[11px] font-bold tracking-[0.6px] uppercase text-text-muted mb-[5px]">Empresa</label>
            <select
              value={form.clientId}
              onChange={e => setForm(f => ({ ...f, clientId: e.target.value }))}
              className="bg-surface-2 border border-border-subtle-2 rounded-lg px-[13px] py-[9px] text-[13px] text-text-primary outline-none w-full focus:border-accent"
            >
              <option value="">Nova empresa...</option>
              {clients.map(c => (
                <option key={c.id} value={c.id}>{c.legalName} — {c.taxId}</option>
              ))}
            </select>
          </div>

          {/* Inline new client fields */}
          {!form.clientId && (
            <>
              <FormInput label="Razão Social *" value={newClientName} onChange={e => setNewClientName(e.target.value)} placeholder="Nome da empresa" />
              <FormInput label="CNPJ *" value={newClientCnpj} onChange={e => setNewClientCnpj(formatCnpj(e.target.value))} placeholder="00.000.000/0001-00" />
            </>
          )}

          <FormSelect label={t("contacts.contactType")}
            value={form.department}
            onChange={e => setForm(f => ({ ...f, department: e.target.value }))}
          >
            <option value="Finance">{t("contacts.typeFinance")}</option>
            <option value="Purchasing">{t("contacts.typePurchasing")}</option>
            <option value="Partner">{t("contacts.typePartner")}</option>
          </FormSelect>

          <div className="flex items-center gap-2 mt-5">
            <input type="checkbox" id="primary" checked={form.isPrimary} onChange={e => setForm(f => ({ ...f, isPrimary: e.target.checked }))} className="accent-accent" />
            <label htmlFor="primary" className="text-sm text-text-secondary cursor-pointer">Contato principal</label>
          </div>

          <div className="col-span-2">
            <FormInput label={`${t("contacts.contactName")} *`} value={form.name} onChange={e => setForm(f => ({ ...f, name: e.target.value }))} placeholder={t("contacts.fullName")} />
          </div>
          <div className="col-span-2">
            <FormInput label={t("contacts.col.email")} type="email" value={form.email} onChange={e => setForm(f => ({ ...f, email: e.target.value }))} placeholder="contato@empresa.com.br" />
          </div>
          <div className="col-span-2">
            <FormInput label={t("contacts.mobileWhatsapp")} value={form.whatsAppPhone} onChange={e => setForm(f => ({ ...f, whatsAppPhone: formatPhone(e.target.value) }))} placeholder="+55 11 99999-9999" />
          </div>
        </div>
      </Modal>
    </div>
  );
};
