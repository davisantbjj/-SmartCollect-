import { useEffect, useState } from "react";
import { Button, FormInput, Modal } from "../components/UI";
import { ICONS } from "../utils/icons";
import { formatCnpj, onlyDigits } from "../utils/formatters";
import { t } from "../i18n";
import {
  ApiError,
  createTenant,
  getTenantById,
  getTenants,
  updateTenant,
  updateTenantAccess,
  type TenantResponse,
} from "../services/api";
import type { ShowToast } from "../types";

interface TenantForm {
  companyName: string;
  taxId: string;
  emailDomain: string;
  adminName: string;
  adminEmail: string;
  adminPassword: string;
}

const defaultForm: TenantForm = {
  companyName: "",
  taxId: "",
  emailDomain: "",
  adminName: "",
  adminEmail: "",
  adminPassword: "",
};

export const PageTenants = ({ showToast }: { showToast: ShowToast }) => {
  const [tenants, setTenants] = useState<TenantResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [updatingTenantId, setUpdatingTenantId] = useState<string | null>(null);
  const [open, setOpen] = useState(false);
  const [editOpen, setEditOpen] = useState(false);
  const [editLoading, setEditLoading] = useState(false);
  const [editSaving, setEditSaving] = useState(false);
  const [editingTenantId, setEditingTenantId] = useState<string | null>(null);
  const [createAdminLogin, setCreateAdminLogin] = useState(true);
  const [currentAdmin, setCurrentAdmin] = useState<{ name: string; email: string } | null>(null);
  const [form, setForm] = useState<TenantForm>(defaultForm);
  const [editForm, setEditForm] = useState<TenantForm>(defaultForm);

  const load = async () => {
    try {
      setLoading(true);
      setTenants(await getTenants());
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : t("tenants.errors.load");
      showToast(`${msg}`, "error");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void load();
  }, []);

  const handleCreate = async () => {
    if (!form.companyName || !form.taxId || !form.emailDomain) {
      showToast(`${t("tenants.validation.companyRequired")}`, "warn");
      return;
    }

    if (createAdminLogin && (!form.adminName || !form.adminEmail || !form.adminPassword)) {
      showToast(`${t("tenants.validation.adminRequired")}`, "warn");
      return;
    }

    try {
      setSaving(true);
      await createTenant({
        companyName: form.companyName.trim(),
        taxId: onlyDigits(form.taxId),
        emailDomain: form.emailDomain.trim(),
        adminName: createAdminLogin ? form.adminName.trim() : undefined,
        adminEmail: createAdminLogin ? form.adminEmail.trim() : undefined,
        adminPassword: createAdminLogin ? form.adminPassword : undefined,
      });
      showToast(`${t("tenants.messages.created")}`, "success");
      setOpen(false);
      setForm(defaultForm);
      setCreateAdminLogin(true);
      await load();
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : t("tenants.errors.create");
      showToast(`${msg}`, "error");
    } finally {
      setSaving(false);
    }
  };

  const handleToggleAccess = async (tenant: TenantResponse) => {
    const nextActive = !tenant.active;
    const actionText = nextActive ? t("tenants.actions.reactivate") : t("tenants.actions.remove");
    const confirmed = window.confirm(
      `${t("tenants.confirmTogglePrefix")} ${actionText} ${t("tenants.confirmToggleSuffix")} "${tenant.companyName}"?`
    );
    if (!confirmed) return;

    try {
      setUpdatingTenantId(tenant.id);
      await updateTenantAccess(tenant.id, { active: nextActive });
      showToast(
        `${nextActive ? t("tenants.messages.accessReactivated") : t("tenants.messages.accessRemoved")}`,
        "success"
      );
      await load();
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : t("tenants.errors.accessUpdate");
      showToast(`${msg}`, "error");
    } finally {
      setUpdatingTenantId(null);
    }
  };

  const openEdit = async (tenantId: string) => {
    try {
      setEditingTenantId(tenantId);
      setEditOpen(true);
      setEditLoading(true);

      const tenant = await getTenantById(tenantId);
      const adminName = tenant.adminName ?? t("tenants.adminNotInformed");
      const adminEmail = tenant.adminEmail ?? t("tenants.adminNotInformed");

      setEditForm({
        companyName: tenant.companyName,
        taxId: formatCnpj(tenant.taxId),
        emailDomain: tenant.emailDomain,
        adminName: tenant.adminName ?? "",
        adminEmail: tenant.adminEmail ?? "",
        adminPassword: "",
      });
      setCurrentAdmin({ name: adminName, email: adminEmail });
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : t("tenants.errors.loadDetails");
      showToast(`${msg}`, "error");
      setEditOpen(false);
      setEditingTenantId(null);
      setCurrentAdmin(null);
    } finally {
      setEditLoading(false);
    }
  };

  const handleUpdate = async () => {
    if (!editingTenantId) return;

    if (!editForm.companyName || !editForm.taxId || !editForm.emailDomain) {
      showToast(`${t("tenants.validation.companyRequired")}`, "warn");
      return;
    }

    try {
      setEditSaving(true);
      await updateTenant(editingTenantId, {
        companyName: editForm.companyName.trim(),
        taxId: onlyDigits(editForm.taxId),
        emailDomain: editForm.emailDomain.trim(),
      });

      showToast(`${t("tenants.messages.updated")}`, "success");
      setEditOpen(false);
      setEditingTenantId(null);
      setCurrentAdmin(null);
      await load();
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : t("tenants.errors.update");
      showToast(`${msg}`, "error");
    } finally {
      setEditSaving(false);
    }
  };

  return (
    <div className="animate-fade-up">
      <div className="flex justify-end mb-4">
        <Button variant="primary" onClick={() => setOpen(true)}>
          {ICONS.plus} {t("tenants.newCompany")}
        </Button>
      </div>

      <div className="rounded-xl border border-border-subtle overflow-hidden">
        <table className="w-full border-collapse text-[13px]">
          <thead>
            <tr className="bg-surface-2">
              <th className="px-4 py-[11px] text-left text-[10.5px] font-bold tracking-[0.5px] uppercase text-text-muted border-b border-border-subtle">{t("tenants.table.company")}</th>
              <th className="px-4 py-[11px] text-left text-[10.5px] font-bold tracking-[0.5px] uppercase text-text-muted border-b border-border-subtle">{t("tenants.table.cnpj")}</th>
              <th className="px-4 py-[11px] text-left text-[10.5px] font-bold tracking-[0.5px] uppercase text-text-muted border-b border-border-subtle">{t("tenants.table.plan")}</th>
              <th className="px-4 py-[11px] text-left text-[10.5px] font-bold tracking-[0.5px] uppercase text-text-muted border-b border-border-subtle">{t("tenants.table.users")}</th>
              <th className="px-4 py-[11px] text-left text-[10.5px] font-bold tracking-[0.5px] uppercase text-text-muted border-b border-border-subtle">{t("tenants.table.titles")}</th>
              <th className="px-4 py-[11px] text-left text-[10.5px] font-bold tracking-[0.5px] uppercase text-text-muted border-b border-border-subtle">{t("tenants.table.status")}</th>
              <th className="px-4 py-[11px] text-left text-[10.5px] font-bold tracking-[0.5px] uppercase text-text-muted border-b border-border-subtle">{t("tenants.table.edit")}</th>
              <th className="px-4 py-[11px] text-left text-[10.5px] font-bold tracking-[0.5px] uppercase text-text-muted border-b border-border-subtle">{t("tenants.table.active")}</th>
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr><td colSpan={8} className="text-center py-12 text-sm text-text-muted">{t("tenants.loading")}</td></tr>
            ) : tenants.length === 0 ? (
              <tr><td colSpan={8} className="text-center py-12 text-sm text-text-muted">{t("tenants.empty")}</td></tr>
            ) : tenants.map(tenant => (
              <tr key={tenant.id} className="border-b border-border-subtle hover:bg-surface-2/50 transition-colors">
                <td className="px-4 py-[13px] font-semibold">{tenant.companyName}</td>
                <td className="px-4 py-[13px] text-text-secondary">{formatCnpj(tenant.taxId)}</td>
                <td className="px-4 py-[13px] text-text-secondary">{tenant.plan}</td>
                <td className="px-4 py-[13px] text-text-secondary">{tenant.userCount}</td>
                <td className="px-4 py-[13px] text-text-secondary">{tenant.titleCount}</td>
                <td className="px-4 py-[13px]">
                  <span className={`text-[11px] font-bold px-2 py-1 rounded-full ${tenant.active ? "bg-success/10 text-success" : "bg-text-muted/12 text-text-muted"}`}>
                    {tenant.active ? t("tenants.statusActive") : t("tenants.statusInactive")}
                  </span>
                </td>
                <td className="px-4 py-[13px]">
                  <Button size="sm" variant="secondary" onClick={() => void openEdit(tenant.id)}>
                    {ICONS.pencil} {t("common.edit")}
                  </Button>
                </td>
                <td className="px-4 py-[13px]">
                  <button
                    type="button"
                    role="switch"
                    aria-checked={tenant.active}
                    disabled={updatingTenantId === tenant.id}
                    onClick={() => void handleToggleAccess(tenant)}
                    className={`inline-flex items-center gap-2 rounded-full border px-2 py-1 text-xs font-semibold transition-colors ${tenant.active ? "border-success/30 bg-success/12 text-success" : "border-border-subtle-2 bg-surface-2 text-text-muted"} ${updatingTenantId === tenant.id ? "opacity-60 cursor-not-allowed" : "cursor-pointer"}`}
                  >
                    <span
                      className={`h-4 w-7 rounded-full p-[2px] transition-colors ${tenant.active ? "bg-success/75" : "bg-text-muted/40"}`}
                    >
                      <span className={`block h-3 w-3 rounded-full bg-white transition-transform ${tenant.active ? "translate-x-3" : "translate-x-0"}`} />
                    </span>
                    <span>{tenant.active ? t("common.active") : t("common.inactive")}</span>
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <Modal
        open={open}
        onClose={() => setOpen(false)}
        title={t("tenants.modalCreateTitle")}
        maxWidth={680}
        footer={<>
          <Button variant="secondary" onClick={() => setOpen(false)}>{t("common.cancel")}</Button>
          <Button variant="primary" onClick={handleCreate}>{saving ? t("common.saving") : t("tenants.createAction")}</Button>
        </>}
      >
        <div className="grid grid-cols-2 gap-3">
          <FormInput label={t("tenants.labels.companyName")} value={form.companyName} onChange={e => setForm(f => ({ ...f, companyName: e.target.value }))} />
          <FormInput label={t("tenants.labels.taxId")} value={form.taxId} onChange={e => setForm(f => ({ ...f, taxId: formatCnpj(e.target.value) }))} placeholder={t("common.cnpjPlaceholder")} />
          <FormInput label={t("tenants.labels.emailDomain")} value={form.emailDomain} onChange={e => setForm(f => ({ ...f, emailDomain: e.target.value }))} placeholder={t("tenants.placeholders.emailDomain")} />
          <div />
          <div className="col-span-2 flex items-center gap-2 mt-1">
            <input
              type="checkbox"
              id="create-admin"
              checked={createAdminLogin}
              onChange={e => setCreateAdminLogin(e.target.checked)}
              className="accent-accent"
            />
            <label htmlFor="create-admin" className="text-sm text-text-secondary">
              {t("tenants.labels.createAdminNow")}
            </label>
          </div>
          {createAdminLogin && (
            <>
              <FormInput label={t("tenants.labels.adminName")} value={form.adminName} onChange={e => setForm(f => ({ ...f, adminName: e.target.value }))} />
              <FormInput label={t("tenants.labels.adminEmail")} type="email" value={form.adminEmail} onChange={e => setForm(f => ({ ...f, adminEmail: e.target.value }))} />
              <div className="col-span-2">
                <FormInput
                  label={t("tenants.labels.adminPassword")}
                  type="password"
                  value={form.adminPassword}
                  onChange={e => setForm(f => ({ ...f, adminPassword: e.target.value }))} 
                />
              </div>
            </>
          )}
        </div>
      </Modal>

      <Modal
        open={editOpen}
        onClose={() => {
          setEditOpen(false);
          setEditingTenantId(null);
          setCurrentAdmin(null);
        }}
        title={t("tenants.modalEditTitle")}
        maxWidth={680}
        footer={<>
          <Button variant="secondary" onClick={() => {
            setEditOpen(false);
            setEditingTenantId(null);
            setCurrentAdmin(null);
          }}>{t("common.cancel")}</Button>
          <Button variant="primary" onClick={handleUpdate}>{editSaving ? t("common.saving") : t("tenants.saveChanges")}</Button>
        </>}
      >
        {editLoading ? (
          <div className="text-sm text-text-muted py-4">{t("tenants.loadingDetails")}</div>
        ) : (
          <div className="grid grid-cols-2 gap-3">
            <FormInput label={t("tenants.labels.companyName")} value={editForm.companyName} onChange={e => setEditForm(f => ({ ...f, companyName: e.target.value }))} />
            <FormInput label={t("tenants.labels.taxId")} value={editForm.taxId} onChange={e => setEditForm(f => ({ ...f, taxId: formatCnpj(e.target.value) }))} placeholder={t("common.cnpjPlaceholder")} />
            <FormInput label={t("tenants.labels.emailDomain")} value={editForm.emailDomain} onChange={e => setEditForm(f => ({ ...f, emailDomain: e.target.value }))} placeholder={t("tenants.placeholders.emailDomain")} />
            <div />

            {currentAdmin && (
              <div className="col-span-2 rounded-lg border border-border-subtle-2 bg-surface-2 px-3 py-2 text-sm">
                <div className="text-[11px] font-bold tracking-[0.6px] uppercase text-text-muted mb-1">{t("tenants.labels.currentAdmin")}</div>
                <div className="text-text-primary font-semibold">{currentAdmin.name}</div>
                <div className="text-text-secondary text-xs mt-0.5">{currentAdmin.email}</div>
              </div>
            )}

          </div>
        )}
      </Modal>
    </div>
  );
};
