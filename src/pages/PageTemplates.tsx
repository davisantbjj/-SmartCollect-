import { useEffect, useState } from "react";
import { CardHeader, Button, Modal, FormInput, FormSelect } from "../components/UI";
import { ICONS } from "../utils/icons";
import { t } from "../i18n";
import { TEMPLATE_VARIABLES } from "../config/templateVariables";

import {
  ApiError, getTemplates, createTemplate, updateTemplate,
  type MessageTemplateResponse, type StoredSession,
} from "../services/api";
import type { ShowToast } from "../types";

interface TemplateForm {
  name: string;
  channel: string;
  type: string;
  subject: string;
  body: string;
  active: boolean;
}

const emptyForm: TemplateForm = { name: "", channel: "Email", type: "Collection", subject: "", body: "", active: true };

export const PageTemplates = ({
  showToast,
  session,
  selectedTenantId,
}: {
  showToast: ShowToast;
  session: StoredSession;
  selectedTenantId?: string;
}) => {
  const [templates, setTemplates] = useState<MessageTemplateResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [selected, setSelected] = useState(0);
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<MessageTemplateResponse | null>(null);
  const [form, setForm] = useState<TemplateForm>(emptyForm);
  const [saving, setSaving] = useState(false);

  const tenantId = session.role === "Master" ? selectedTenantId : undefined;
  const requiresTenantSelection = session.role === "Master" && !tenantId;
  const canEdit =
    session.role === "Admin"
    || session.role === "Worker"
    || (session.role === "Master" && Boolean(tenantId));

  const load = async () => {
    if (requiresTenantSelection) {
      setTemplates([]);
      setLoading(false);
      return;
    }

    try {
      setLoading(true);
      const data = await getTemplates(tenantId);
      setTemplates(data);
    } catch {
      showToast(`${ICONS.cross} Erro ao carregar templates.`, "error");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { void load(); }, [tenantId, requiresTenantSelection]);

  const openNew = () => {
    setEditing(null);
    setForm(emptyForm);
    setModalOpen(true);
  };

  const openEdit = (tpl: MessageTemplateResponse) => {
    setEditing(tpl);
    setForm({
      name: tpl.name,
      channel: tpl.channel,
      type: tpl.type,
      subject: tpl.subject ?? "",
      body: tpl.body,
      active: tpl.active,
    });
    setModalOpen(true);
  };

  const handleSave = async () => {
    if (requiresTenantSelection) {
      showToast(`${ICONS.warning} Selecione uma empresa para gerenciar templates.`, "warn");
      return;
    }

    if (!form.name || !form.body) {
      showToast(`${ICONS.warning} Nome e mensagem são obrigatórios.`, "warn");
      return;
    }
    if ((form.channel === "Email" || form.channel === "Both") && !form.subject) {
      showToast(`${ICONS.warning} Templates de E-mail exigem assunto.`, "warn");
      return;
    }
    try {
      setSaving(true);
      if (editing) {
        await updateTemplate(editing.id, {
          name: form.name, channel: form.channel, type: form.type,
          subject: form.subject || undefined, body: form.body, active: form.active,
        }, tenantId);
      } else {
        await createTemplate({
          name: form.name, channel: form.channel, type: form.type,
          subject: form.subject || undefined, body: form.body,
        }, tenantId);
      }
      showToast(`${ICONS.checkmark} ${t("toast.templateCreated")}`, "success");
      setModalOpen(false);
      void load();
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : "Erro ao salvar template.";
      showToast(`${ICONS.cross} ${msg}`, "error");
    } finally {
      setSaving(false);
    }
  };

  const insertVar = (v: string) => {
    setForm(f => ({ ...f, body: f.body + v }));
  };

  if (loading) return <div className="py-12 text-center text-sm text-text-muted">Carregando templates...</div>;

  return (
    <div className="animate-fade-up">
      {requiresTenantSelection && (
        <div className="rounded-xl border border-border-subtle bg-surface-2/60 px-4 py-3 text-sm text-text-secondary mb-4">
          Selecione uma empresa no topo para visualizar e editar templates.
        </div>
      )}

      <div className="flex justify-end mb-5">
        {canEdit && (
          <Button variant="primary" onClick={openNew} disabled={requiresTenantSelection}>
            {t("templates.createTemplate")}
          </Button>
        )}
      </div>

      <div className="grid grid-cols-3 gap-3.5 mb-7">
        {templates.map((tpl, i) => (
          <div
            key={tpl.id}
            onClick={() => setSelected(i)}
            className={`bg-surface border rounded-[14px] overflow-hidden p-[18px] cursor-pointer relative transition-all duration-200 ${
              selected === i ? "border-accent bg-accent/[0.03]" : "border-border-subtle hover:border-border-subtle-2"
            }`}
          >
            <div className={`inline-flex items-center gap-[5px] text-[11px] font-bold px-[9px] py-[3px] rounded-full mb-3 ${
              tpl.channel === "Email"
                ? "bg-blue-500/[0.12] text-blue-500"
                : tpl.channel === "Both"
                  ? "bg-accent/12 text-accent"
                  : "bg-[#25d366]/[0.12] text-[#25d366]"
            }`}>
              {tpl.channel === "Email"
                ? <>{ICONS.email} {t("templates.channelEmail")}</>
                : tpl.channel === "Both"
                  ? <>{ICONS.link} {t("common.both")}</>
                  : <>{ICONS.chat} {t("templates.channelWhatsapp")}</>}
            </div>
            <div className="font-extrabold text-sm mb-[5px]">{tpl.name}</div>
            <div className="text-xs text-text-secondary mb-1">{tpl.type} · {tpl.active ? "Ativo" : "Inativo"}</div>
            <div className="bg-surface-2 rounded-lg p-3 text-xs text-text-secondary leading-[1.7] border-l-[3px] border-accent mt-2">
              {tpl.body.split(/({{[^}]+}})/g).map((part, j) =>
                part.startsWith("{{")
                  ? <span key={j} className="bg-accent/[0.13] text-accent px-1 py-px rounded-[3px] font-mono text-[11px]">{part}</span>
                  : part
              )}
            </div>
            {canEdit && (
              <div className="mt-3 flex gap-1.5">
                <Button size="sm" variant="secondary" onClick={() => openEdit(tpl)}>
                  {ICONS.pencil} Editar
                </Button>
              </div>
            )}
          </div>
        ))}

        {canEdit && (
          <div
            onClick={openNew}
            className="bg-surface border border-dashed border-border-subtle rounded-[14px] p-[18px] cursor-pointer flex items-center justify-center flex-col gap-2 min-h-40 hover:border-accent transition-colors"
          >
            <span className="text-[28px] text-text-muted">+</span>
            <span className="text-[13px] text-text-secondary">{t("templates.newTemplate")}</span>
          </div>
        )}
      </div>

      {/* Variables reference */}
      <div className="bg-surface border border-border-subtle rounded-[14px] overflow-hidden">
        <CardHeader title={<>{ICONS.abcLetters} {t("templates.availableVars")}</>} subtitle={t("templates.availableVarsSubtitle")} />
        <div className="p-[18px] flex flex-wrap gap-2">
          {TEMPLATE_VARIABLES.map(v => (
            <span
              key={v}
              onClick={() => showToast(`${ICONS.clipboard} ${v} ${t("toast.variableCopied")}`, "info")}
              className="bg-accent/10 text-accent px-3 py-[5px] rounded-full font-mono text-xs cursor-pointer border border-accent/20 hover:bg-accent/[0.16] transition-colors"
            >
              {v}
            </span>
          ))}
        </div>
      </div>

      <Modal open={modalOpen} onClose={() => setModalOpen(false)} title={editing ? "Editar Template" : t("templates.modalTitle")}
        maxWidth={620}
        footer={<>
          <Button variant="secondary" onClick={() => setModalOpen(false)}>{t("common.cancel")}</Button>
          <Button variant="primary" onClick={handleSave}>{saving ? "Salvando..." : t("templates.saveTemplate")}</Button>
        </>}
      >
        <div className="grid grid-cols-2 gap-3.5">
          <FormInput label={t("templates.templateName")} value={form.name} onChange={e => setForm(f => ({ ...f, name: e.target.value }))} placeholder={t("templates.templateNamePlaceholder")} />
          <FormSelect label={t("templates.channel")} value={form.channel} onChange={e => setForm(f => ({ ...f, channel: e.target.value }))}>
            <option value="Email">{t("channel.email")}</option>
            <option value="WhatsApp">WhatsApp</option>
            <option value="Both">{t("common.both")}</option>
          </FormSelect>
          <FormSelect label="Tipo" value={form.type} onChange={e => setForm(f => ({ ...f, type: e.target.value }))}>
            <option value="Collection">Cobrança</option>
            <option value="ThankYou">Agradecimento</option>
          </FormSelect>
          {editing && (
            <div className="flex items-center gap-2 mt-5">
              <input type="checkbox" id="tpl-active" checked={form.active} onChange={e => setForm(f => ({ ...f, active: e.target.checked }))} className="accent-accent" />
              <label htmlFor="tpl-active" className="text-sm text-text-secondary">Ativo</label>
            </div>
          )}
          {(form.channel === "Email" || form.channel === "Both") && (
            <div className="col-span-2">
              <FormInput label={t("templates.emailSubject")} value={form.subject} onChange={e => setForm(f => ({ ...f, subject: e.target.value }))} placeholder={t("templates.emailSubjectPlaceholder")} />
            </div>
          )}
          <div className="col-span-2">
            <label className="block text-[11px] font-bold tracking-[0.6px] uppercase text-text-muted mb-[5px]">{t("templates.message")}</label>
            <textarea
              value={form.body}
              onChange={e => setForm(f => ({ ...f, body: e.target.value }))}
              placeholder={t("templates.messagePlaceholder")}
              rows={5}
              className="bg-surface-2 border border-border-subtle-2 rounded-lg px-[13px] py-[9px] text-[13px] text-text-primary outline-none w-full focus:border-accent resize-y"
            />
          </div>
          <div className="col-span-2">
            <label className="block text-[11px] font-bold tracking-[0.6px] uppercase text-text-muted mb-[5px]">{t("templates.varsClickToInsert")}</label>
            <div className="flex flex-wrap gap-1.5 mt-1.5">
              {TEMPLATE_VARIABLES.slice(0, 6).map(v => (
                <span key={v} onClick={() => insertVar(v)} className="bg-accent/10 text-accent px-2 py-[3px] rounded-full font-mono text-[11px] cursor-pointer hover:bg-accent/20">{v}</span>
              ))}
            </div>
          </div>
        </div>
      </Modal>
    </div>
  );
};
