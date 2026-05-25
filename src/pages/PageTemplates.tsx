import { useEffect, useRef, useState } from "react";
import { CardHeader, Button, Modal, FormInput, FormSelect } from "../components/UI";
import { ICONS } from "../utils/icons";
import { t } from "../i18n";
import { TEMPLATE_VARIABLES } from "../config/templateVariables";

import {
  ApiError, getTemplates, createTemplate, updateTemplate, deleteTemplate,
  getEmailLayoutConfig, saveEmailLayoutConfig,
  type MessageTemplateResponse, type StoredSession,
  type EmailLayoutConfigResponse,
} from "../services/api";
import type { ShowToast } from "../types";

interface TemplateForm {
  name: string;
  type: string;
  subject: string;
  body: string;
  active: boolean;
}

const emptyForm: TemplateForm = { name: "", type: "Collection", subject: "", body: "", active: true };

const emptyLayout: EmailLayoutConfigResponse = {
  enabled: false,
  logoUrl: "",
  heroUrl: "",
  footerMessage: "",
  instagramUrl: "",
  linkedInUrl: "",
  whatsAppUrl: "",
  telegramUrl: "",
};

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
  const [deletingId, setDeletingId] = useState<string | null>(null);
  const [layoutOpen, setLayoutOpen] = useState(false);
  const [layout, setLayout] = useState<EmailLayoutConfigResponse>(emptyLayout);
  const [layoutLoading, setLayoutLoading] = useState(false);
  const [layoutSaving, setLayoutSaving] = useState(false);
  const [logoFileName, setLogoFileName] = useState("");
  const [heroFileName, setHeroFileName] = useState("");
  const logoInputRef = useRef<HTMLInputElement | null>(null);
  const heroInputRef = useRef<HTMLInputElement | null>(null);

  const tenantId = session.role === "Master" ? selectedTenantId : undefined;
  const requiresTenantSelection = session.role === "Master" && !tenantId;
  const canEdit =
    session.role === "Admin"
    || session.role === "Worker"
    || (session.role === "Master" && Boolean(tenantId));

  const resolveTemplateTypeLabel = (type: string) => {
    if (type === "ThankYou") return t("templates.typeThankYou");
    if (type === "Reminder") return t("templates.typeReminder");
    if (type === "Collection") return t("templates.typeCollection");
    return type;
  };

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
      showToast(`${t("templates.errors.load")}`, "error");
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
      type: tpl.type,
      subject: tpl.subject ?? "",
      body: tpl.body,
      active: tpl.active,
    });
    setModalOpen(true);
  };

  const openLayout = async () => {
    if (requiresTenantSelection) {
      showToast(`${t("templates.validation.selectTenant")}`, "warn");
      return;
    }

    try {
      setLayoutLoading(true);
      const data = await getEmailLayoutConfig(tenantId);
      setLayout({ ...emptyLayout, ...data });
      setLogoFileName(extractFileName(data.logoUrl));
      setHeroFileName(extractFileName(data.heroUrl));
      setLayoutOpen(true);
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : t("templates.layout.errors.load");
      showToast(`${msg}`, "error");
    } finally {
      setLayoutLoading(false);
    }
  };

  const handleSave = async () => {
    if (requiresTenantSelection) {
      showToast(`${t("templates.validation.selectTenant")}`, "warn");
      return;
    }

    if (!form.name || !form.body) {
      showToast(`${t("templates.validation.requiredFields")}`, "warn");
      return;
    }
    try {
      setSaving(true);
      const payloadChannel = editing?.channel ?? "Email";
      if (editing) {
        await updateTemplate(editing.id, {
          name: form.name, channel: payloadChannel, type: form.type,
          subject: form.subject || undefined, body: form.body, active: form.active,
        }, tenantId);
      } else {
        await createTemplate({
          name: form.name, channel: payloadChannel, type: form.type,
          subject: form.subject || undefined, body: form.body,
        }, tenantId);
      }
      showToast(`${t("toast.templateCreated")}`, "success");
      setModalOpen(false);
      void load();
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : t("templates.errors.save");
      showToast(`${msg}`, "error");
    } finally {
      setSaving(false);
    }
  };

  const handleDelete = async (tpl: MessageTemplateResponse) => {
    if (requiresTenantSelection) {
      showToast(`${t("templates.validation.selectTenant")}`, "warn");
      return;
    }

    const confirmed = window.confirm(`${t("templates.confirmDelete")} "${tpl.name}"?`);
    if (!confirmed)
      return;

    try {
      setDeletingId(tpl.id);
      await deleteTemplate(tpl.id, tenantId);
      showToast(`${t("templates.messages.deleted")}`, "success");
      await load();
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : t("templates.errors.delete");
      showToast(`${msg}`, "error");
    } finally {
      setDeletingId(null);
    }
  };

  const insertVar = (v: string) => {
    setForm(f => ({ ...f, body: f.body + v }));
  };

  const sanitizePreviewHtml = (html: string) =>
    html.replace(/<script[\s\S]*?>[\s\S]*?<\/script>/gi, "");

  const extractFileName = (value?: string | null) => {
    if (!value) return "";
    if (value.startsWith("data:")) return "";
    try {
      const url = new URL(value);
      const last = url.pathname.split("/").filter(Boolean).pop();
      return last ?? "";
    } catch {
      const last = value.split("/").filter(Boolean).pop();
      return last ?? "";
    }
  };

  const handleImageUpload = (file: File | null, target: "logo" | "hero") => {
    if (!file) {
      if (target === "logo") {
        setLayout(l => ({ ...l, logoUrl: "" }));
        setLogoFileName("");
      } else {
        setLayout(l => ({ ...l, heroUrl: "" }));
        setHeroFileName("");
      }
      return;
    }

    optimizeLayoutImage(file, target).then(result => {
      if (target === "logo") {
        setLayout(l => ({ ...l, logoUrl: result }));
        setLogoFileName(file.name);
      } else {
        setLayout(l => ({ ...l, heroUrl: result }));
        setHeroFileName(file.name);
      }
    });
  };

  const optimizeLayoutImage = (file: File, target: "logo" | "hero") =>
    new Promise<string>(resolve => {
      if (!file.type.startsWith("image/") || file.type === "image/svg+xml") {
        const reader = new FileReader();
        reader.onload = () => resolve(typeof reader.result === "string" ? reader.result : "");
        reader.readAsDataURL(file);
        return;
      }

      const reader = new FileReader();
      reader.onload = () => {
        const source = typeof reader.result === "string" ? reader.result : "";
        const image = new Image();
        image.onload = () => {
          const maxWidth = target === "logo" ? 320 : 720;
          const maxHeight = target === "logo" ? 180 : 360;
          const scale = Math.min(1, maxWidth / image.width, maxHeight / image.height);
          const canvas = document.createElement("canvas");
          canvas.width = Math.max(1, Math.round(image.width * scale));
          canvas.height = Math.max(1, Math.round(image.height * scale));
          const context = canvas.getContext("2d");

          if (!context) {
            resolve(source);
            return;
          }

          context.drawImage(image, 0, 0, canvas.width, canvas.height);
          resolve(canvas.toDataURL(file.type === "image/png" ? "image/png" : "image/jpeg", 0.82));
        };
        image.onerror = () => resolve(source);
        image.src = source;
      };
      reader.readAsDataURL(file);
    });

  const buildLayoutPreview = (data: EmailLayoutConfigResponse) => {
    const safe = (value?: string | null) => (value ?? "").trim();
    const logo = safe(data.logoUrl)
      ? `<div style="text-align:center;margin-bottom:16px;"><img src="${safe(data.logoUrl)}" alt="Logo" style="max-width:180px;height:auto;" /></div>`
      : "";
    const hero = safe(data.heroUrl)
      ? `<div style="text-align:center;margin:16px 0;"><img src="${safe(data.heroUrl)}" alt="Imagem" style="max-width:100%;height:auto;border-radius:10px;" /></div>`
      : "";
    const footer = safe(data.footerMessage)
      ? `<div style="margin-top:18px;font-size:12px;color:#9CA3AF;line-height:1.5;">${safe(data.footerMessage)}</div>`
      : "";

    const icon = (url?: string | null, label?: string, path?: string) =>
      safe(url)
        ? `<a href="${safe(url)}" style="display:inline-block;margin:0 6px;text-decoration:none;">
            <svg width="26" height="26" viewBox="0 0 24 24" fill="#061C4B" xmlns="http://www.w3.org/2000/svg" aria-label="${label}">
              <path d="${path}" />
            </svg>
          </a>`
        : "";

    const social = [
      icon(data.instagramUrl, "Instagram", "M12 7a5 5 0 1 0 0 10 5 5 0 0 0 0-10zm0-5.5c1.6 0 3.2.03 4.8.1 1.2.05 2.1.24 2.9.56.85.34 1.56.8 2.26 1.5.7.7 1.16 1.41 1.5 2.26.32.8.51 1.7.56 2.9.07 1.6.1 3.2.1 4.8s-.03 3.2-.1 4.8c-.05 1.2-.24 2.1-.56 2.9-.34.85-.8 1.56-1.5 2.26-.7.7-1.41 1.16-2.26 1.5-.8.32-1.7.51-2.9.56-1.6.07-3.2.1-4.8.1s-3.2-.03-4.8-.1c-1.2-.05-2.1-.24-2.9-.56-.85-.34-1.56-.8-2.26-1.5-.7-.7-1.16-1.41-1.5-2.26-.32-.8-.51-1.7-.56-2.9C1.03 15.2 1 13.6 1 12s.03-3.2.1-4.8c.05-1.2.24-2.1.56-2.9.34-.85.8-1.56 1.5-2.26.7-.7 1.41-1.16 2.26-1.5.8-.32 1.7-.51 2.9-.56C8.8 1.53 10.4 1.5 12 1.5zm0 7a3.5 3.5 0 1 1 0 7 3.5 3.5 0 0 1 0-7zm6.1-2.9a1.3 1.3 0 1 1-2.6 0 1.3 1.3 0 0 1 2.6 0z"),
      icon(data.linkedInUrl, "LinkedIn", "M4.98 3.5a2.5 2.5 0 1 1 0 5 2.5 2.5 0 0 1 0-5zM3 9h4v12H3V9zm7 0h3.8v1.64h.05c.53-1 1.85-2.06 3.8-2.06 4.07 0 4.82 2.68 4.82 6.16V21h-4v-5.2c0-1.24-.02-2.84-1.73-2.84-1.73 0-2 1.35-2 2.75V21h-4V9z"),
      icon(data.whatsAppUrl, "WhatsApp", "M12 2a10 10 0 0 0-8.7 14.9L2 22l5.3-1.3A10 10 0 1 0 12 2zm5.8 14.2c-.24.68-1.4 1.3-1.93 1.38-.5.08-1.13.12-1.82-.11-.42-.14-.95-.31-1.64-.61-2.88-1.25-4.76-4.19-4.9-4.38-.13-.18-1.17-1.56-1.17-2.98 0-1.42.74-2.12 1-2.42.25-.3.56-.37.74-.37h.54c.18 0 .42-.07.65.5.24.57.8 1.96.87 2.1.07.14.12.3.02.48-.1.18-.15.3-.3.46-.15.16-.32.36-.45.48-.15.15-.3.32-.13.62.18.3.8 1.32 1.7 2.14 1.17 1.06 2.14 1.4 2.44 1.56.3.15.48.13.66-.08.18-.21.76-.88.96-1.18.2-.3.4-.24.66-.14.26.1 1.66.78 1.95.92.3.14.5.22.57.34.07.12.07.7-.17 1.38z"),
      icon(data.telegramUrl, "Telegram", "M21.9 4.6 3.7 11.5c-1.25.48-1.23 1.17-.22 1.48l4.7 1.46 1.8 5.5c.22.6.12.85.76.85.5 0 .72-.23 1-.5l2.42-2.35 5.02 3.7c.92.5 1.58.25 1.8-.85l3.26-15.3c.3-1.35-.52-1.96-1.3-1.69zm-3.46 3.45-7.9 7.16-.3 3.08-1.83-5.83 10.03-4.41z"),
    ].filter(Boolean).join(" ");

    const socialRow = social
      ? `<div style="margin-top:16px;text-align:center;">${social}</div>`
      : "";

    return `
      <div style="background:#1F2937;border:1px solid rgba(255,255,255,0.15);border-radius:14px;padding:22px;color:#D1D5DB;font-family:'Plus Jakarta Sans',Arial,sans-serif;">
        <div style="font-size:18px;font-weight:800;color:#F9FAFB;">${session?.role ? "SmartCollect" : "SmartCollect"}</div>
        <div style="font-size:12px;color:#9CA3AF;">Prévia do layout</div>
        ${logo}
        ${hero}
        <div style="font-size:14px;line-height:1.6;">
          Olá {{NomeCliente}}, aqui vai a mensagem do template.
        </div>
        ${footer}
        ${socialRow}
      </div>
    `;
  };

  if (loading) return <div className="py-12 text-center text-sm text-text-muted">{t("templates.loading")}</div>;

  return (
    <div className="animate-fade-up">
      {requiresTenantSelection && (
        <div className="rounded-xl border border-border-subtle bg-surface-2/60 px-4 py-3 text-sm text-text-secondary mb-4">
          {t("templates.validation.selectTenant")}
        </div>
      )}

      <div className="flex justify-end gap-2 mb-5">
        {canEdit && (
          <>
            <Button variant="secondary" onClick={() => void openLayout()} disabled={requiresTenantSelection || layoutLoading}>
              {layoutLoading ? t("common.loading") : t("templates.layout.open")}
            </Button>
            <Button variant="primary" onClick={openNew} disabled={requiresTenantSelection}>
              {t("templates.createTemplate")}
            </Button>
          </>
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
            <div className="text-xs text-text-secondary mb-1">{resolveTemplateTypeLabel(tpl.type)} · {tpl.active ? t("common.active") : t("common.inactive")}</div>
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
                  {ICONS.pencil} {t("common.edit")}
                </Button>
                <Button
                  size="sm"
                  variant="danger"
                  onClick={(e) => {
                    e.stopPropagation();
                    void handleDelete(tpl);
                  }}
                  disabled={deletingId === tpl.id}
                >
                  {deletingId === tpl.id ? t("templates.actionDeleting") : t("common.delete")}
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
              onClick={() => showToast(`${v} ${t("toast.variableCopied")}`, "info")}
              className="bg-accent/10 text-accent px-3 py-[5px] rounded-full font-mono text-xs cursor-pointer border border-accent/20 hover:bg-accent/[0.16] transition-colors"
            >
              {v}
            </span>
          ))}
        </div>
      </div>

      <Modal open={modalOpen} onClose={() => setModalOpen(false)} title={editing ? t("templates.editTitle") : t("templates.modalTitle")}
        maxWidth={620}
        footer={<>
          <Button variant="secondary" onClick={() => setModalOpen(false)}>{t("common.cancel")}</Button>
          <Button variant="primary" onClick={handleSave}>{saving ? t("common.saving") : t("templates.saveTemplate")}</Button>
        </>}
      >
        <div className="grid grid-cols-2 gap-3.5">
          <FormInput label={t("templates.templateName")} value={form.name} onChange={e => setForm(f => ({ ...f, name: e.target.value }))} placeholder={t("templates.templateNamePlaceholder")} />
          <FormSelect label={t("templates.typeLabel")} value={form.type} onChange={e => setForm(f => ({ ...f, type: e.target.value }))}>
            <option value="Collection">{t("templates.typeCollection")}</option>
            <option value="Reminder">{t("templates.typeReminder")}</option>
            <option value="ThankYou">{t("templates.typeThankYou")}</option>
          </FormSelect>
          {editing && (
            <div className="flex items-center gap-2 mt-5">
              <input type="checkbox" id="tpl-active" checked={form.active} onChange={e => setForm(f => ({ ...f, active: e.target.checked }))} className="accent-accent" />
              <label htmlFor="tpl-active" className="text-sm text-text-secondary">{t("common.active")}</label>
            </div>
          )}
          <div className="col-span-2">
            <FormInput label={t("templates.emailSubject")} value={form.subject} onChange={e => setForm(f => ({ ...f, subject: e.target.value }))} placeholder={t("templates.emailSubjectPlaceholder")} />
          </div>
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
              {TEMPLATE_VARIABLES.map(v => (
                <span key={v} onClick={() => insertVar(v)} className="bg-accent/10 text-accent px-2 py-[3px] rounded-full font-mono text-[11px] cursor-pointer hover:bg-accent/20">{v}</span>
              ))}
            </div>
          </div>
        </div>
      </Modal>

      <Modal open={layoutOpen} onClose={() => setLayoutOpen(false)} title={t("templates.layout.title")}
        maxWidth={740}
        footer={<>
          <Button variant="secondary" onClick={() => setLayoutOpen(false)}>{t("common.cancel")}</Button>
          <Button
            variant="primary"
            onClick={async () => {
              if (requiresTenantSelection) {
                showToast(`${t("templates.validation.selectTenant")}`, "warn");
                return;
              }
              try {
                setLayoutSaving(true);
                await saveEmailLayoutConfig({
                  enabled: layout.enabled,
                  logoUrl: layout.logoUrl || undefined,
                  heroUrl: layout.heroUrl || undefined,
                  footerMessage: layout.footerMessage || undefined,
                  instagramUrl: layout.instagramUrl || undefined,
                  linkedInUrl: layout.linkedInUrl || undefined,
                  whatsAppUrl: layout.whatsAppUrl || undefined,
                  telegramUrl: layout.telegramUrl || undefined,
                }, tenantId);
                showToast(`${t("templates.layout.messages.saved")}`, "success");
                setLayoutOpen(false);
              } catch (err) {
                const msg = err instanceof ApiError ? err.message : t("templates.layout.errors.save");
                showToast(`${msg}`, "error");
              } finally {
                setLayoutSaving(false);
              }
            }}
          >
            {layoutSaving ? t("common.saving") : t("templates.layout.save")}
          </Button>
        </>}
      >
        <div className="grid grid-cols-2 gap-3.5">
          <div className="col-span-2 flex items-center justify-between gap-3 rounded-xl border border-border-subtle bg-surface-2 px-4 py-3">
            <div>
              <div className="text-sm font-semibold text-text-primary">{t("templates.layout.enabled")}</div>
              <div className="text-xs text-text-muted mt-0.5">{t("templates.layout.enabledHint")}</div>
            </div>
            <button
              type="button"
              role="switch"
              aria-checked={layout.enabled}
              onClick={() => setLayout(l => ({ ...l, enabled: !l.enabled }))}
              className={`inline-flex items-center gap-2 rounded-full border px-2 py-1 text-xs font-semibold transition-colors ${layout.enabled ? "border-success/30 bg-success/12 text-success" : "border-border-subtle-2 bg-surface text-text-muted"}`}
            >
              <span className={`h-4 w-7 rounded-full p-[2px] transition-colors ${layout.enabled ? "bg-success/75" : "bg-text-muted/40"}`}>
                <span className={`block h-3 w-3 rounded-full bg-white transition-transform ${layout.enabled ? "translate-x-3" : "translate-x-0"}`} />
              </span>
              <span>{layout.enabled ? t("common.active") : t("common.inactive")}</span>
            </button>
          </div>
          <div className="col-span-2 grid grid-cols-2 gap-3.5">
            <div>
              <label className="block text-[11px] font-bold tracking-[0.6px] uppercase text-text-muted mb-[5px]">{t("templates.layout.logoUpload")}</label>
              <div className="flex items-center gap-3">
                <button
                  type="button"
                  onClick={() => logoInputRef.current?.click()}
                  className="px-3 py-2 rounded-lg border border-border-subtle bg-surface-2 text-xs text-text-secondary hover:border-border-subtle-2"
                >
                  {t("templates.layout.upload")}
                </button>
                <button
                  type="button"
                  onClick={() => handleImageUpload(null, "logo")}
                  className="text-xs text-text-muted hover:text-text-primary"
                >
                  {t("templates.layout.remove")}
                </button>
              </div>
              {layout.logoUrl && (
                <div className="mt-2 flex items-center gap-2">
                  <img
                    src={layout.logoUrl}
                    alt="Logo"
                    className="h-8 w-8 rounded-md border border-border-subtle object-contain bg-surface"
                  />
                  <span className="text-[11px] text-text-muted">
                    Selecionado: {logoFileName || "imagem atual"}
                  </span>
                </div>
              )}
              <input
                ref={logoInputRef}
                type="file"
                accept="image/*"
                onChange={e => handleImageUpload(e.target.files?.[0] ?? null, "logo")}
                className="hidden"
              />
            </div>
            <div>
              <label className="block text-[11px] font-bold tracking-[0.6px] uppercase text-text-muted mb-[5px]">{t("templates.layout.heroUpload")}</label>
              <div className="flex items-center gap-3">
                <button
                  type="button"
                  onClick={() => heroInputRef.current?.click()}
                  className="px-3 py-2 rounded-lg border border-border-subtle bg-surface-2 text-xs text-text-secondary hover:border-border-subtle-2"
                >
                  {t("templates.layout.upload")}
                </button>
                <button
                  type="button"
                  onClick={() => handleImageUpload(null, "hero")}
                  className="text-xs text-text-muted hover:text-text-primary"
                >
                  {t("templates.layout.remove")}
                </button>
              </div>
              {layout.heroUrl && (
                <div className="mt-2 flex items-center gap-2">
                  <img
                    src={layout.heroUrl}
                    alt="Imagem principal"
                    className="h-8 w-8 rounded-md border border-border-subtle object-cover bg-surface"
                  />
                  <span className="text-[11px] text-text-muted">
                    Selecionado: {heroFileName || "imagem atual"}
                  </span>
                </div>
              )}
              <input
                ref={heroInputRef}
                type="file"
                accept="image/*"
                onChange={e => handleImageUpload(e.target.files?.[0] ?? null, "hero")}
                className="hidden"
              />
            </div>
          </div>
          <div className="col-span-2">
            <label className="block text-[11px] font-bold tracking-[0.6px] uppercase text-text-muted mb-[5px]">{t("templates.layout.footerMessage")}</label>
            <textarea
              value={layout.footerMessage ?? ""}
              onChange={e => setLayout(l => ({ ...l, footerMessage: e.target.value }))}
              rows={3}
              placeholder={t("templates.layout.footerPlaceholder")}
              className="bg-surface-2 border border-border-subtle-2 rounded-lg px-[13px] py-[9px] text-[13px] text-text-primary outline-none w-full focus:border-accent resize-y"
            />
          </div>
          <FormInput
            label={t("templates.layout.instagram")}
            value={layout.instagramUrl ?? ""}
            onChange={e => setLayout(l => ({ ...l, instagramUrl: e.target.value }))}
            placeholder="https://instagram.com/..."
          />
          <FormInput
            label={t("templates.layout.linkedin")}
            value={layout.linkedInUrl ?? ""}
            onChange={e => setLayout(l => ({ ...l, linkedInUrl: e.target.value }))}
            placeholder="https://linkedin.com/..."
          />
          <FormInput
            label={t("templates.layout.whatsapp")}
            value={layout.whatsAppUrl ?? ""}
            onChange={e => setLayout(l => ({ ...l, whatsAppUrl: e.target.value }))}
            placeholder="https://wa.me/..."
          />
          <FormInput
            label={t("templates.layout.telegram")}
            value={layout.telegramUrl ?? ""}
            onChange={e => setLayout(l => ({ ...l, telegramUrl: e.target.value }))}
            placeholder="https://t.me/..."
          />
          <div className="col-span-2">
            <label className="block text-[11px] font-bold tracking-[0.6px] uppercase text-text-muted mb-[5px]">{t("templates.layout.preview")}</label>
            <div className="bg-surface-2 border border-border-subtle-2 rounded-lg p-3 text-[13px] text-text-secondary min-h-[160px]">
              <div dangerouslySetInnerHTML={{ __html: sanitizePreviewHtml(buildLayoutPreview(layout)) }} />
            </div>
          </div>
        </div>
      </Modal>
    </div>
  );
};
