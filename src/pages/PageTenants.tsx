import { useEffect, useState } from "react";
import { Button, FormInput, Modal } from "../components/UI";
import { ICONS } from "../utils/icons";
import { formatCnpj, onlyDigits } from "../utils/formatters";
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
  const [editAdminLogin, setEditAdminLogin] = useState(false);
  const [currentAdmin, setCurrentAdmin] = useState<{ name: string; email: string } | null>(null);
  const [form, setForm] = useState<TenantForm>(defaultForm);
  const [editForm, setEditForm] = useState<TenantForm>(defaultForm);

  const load = async () => {
    try {
      setLoading(true);
      setTenants(await getTenants());
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : "Erro ao carregar empresas.";
      showToast(`${ICONS.cross} ${msg}`, "error");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void load();
  }, []);

  const handleCreate = async () => {
    if (!form.companyName || !form.taxId || !form.emailDomain || !form.adminName || !form.adminEmail || !form.adminPassword) {
      showToast(`${ICONS.warning} Preencha todos os campos obrigatórios.`, "warn");
      return;
    }

    try {
      setSaving(true);
      await createTenant({
        companyName: form.companyName.trim(),
        taxId: onlyDigits(form.taxId),
        emailDomain: form.emailDomain.trim(),
        adminName: form.adminName.trim(),
        adminEmail: form.adminEmail.trim(),
        adminPassword: form.adminPassword,
      });
      showToast(`${ICONS.checkmark} Empresa cadastrada com sucesso.`, "success");
      setOpen(false);
      setForm(defaultForm);
      await load();
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : "Erro ao criar empresa.";
      showToast(`${ICONS.cross} ${msg}`, "error");
    } finally {
      setSaving(false);
    }
  };

  const handleToggleAccess = async (tenant: TenantResponse) => {
    const nextActive = !tenant.active;
    const actionText = nextActive ? "reativar" : "remover";
    const confirmed = window.confirm(
      `Deseja ${actionText} o acesso da empresa \"${tenant.companyName}\"?`
    );
    if (!confirmed) return;

    try {
      setUpdatingTenantId(tenant.id);
      await updateTenantAccess(tenant.id, { active: nextActive });
      showToast(
        `${ICONS.checkmark} Acesso da empresa ${nextActive ? "reativado" : "removido"} com sucesso.`,
        "success"
      );
      await load();
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : "Erro ao atualizar acesso da empresa.";
      showToast(`${ICONS.cross} ${msg}`, "error");
    } finally {
      setUpdatingTenantId(null);
    }
  };

  const openEdit = async (tenantId: string) => {
    try {
      setEditingTenantId(tenantId);
      setEditOpen(true);
      setEditLoading(true);
      setEditAdminLogin(false);

      const tenant = await getTenantById(tenantId);
      const adminName = tenant.adminName ?? "Não informado";
      const adminEmail = tenant.adminEmail ?? "Não informado";

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
      const msg = err instanceof ApiError ? err.message : "Erro ao carregar dados da empresa.";
      showToast(`${ICONS.cross} ${msg}`, "error");
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
      showToast(`${ICONS.warning} Preencha os dados obrigatórios da empresa.`, "warn");
      return;
    }

    if (editAdminLogin && (!editForm.adminName || !editForm.adminEmail)) {
      showToast(`${ICONS.warning} Preencha nome e e-mail do administrador.`, "warn");
      return;
    }

    try {
      setEditSaving(true);
      await updateTenant(editingTenantId, {
        companyName: editForm.companyName.trim(),
        taxId: onlyDigits(editForm.taxId),
        emailDomain: editForm.emailDomain.trim(),
        editAdminLogin,
        adminName: editAdminLogin ? editForm.adminName.trim() : undefined,
        adminEmail: editAdminLogin ? editForm.adminEmail.trim() : undefined,
        adminPassword: editAdminLogin && editForm.adminPassword.trim() ? editForm.adminPassword : undefined,
      });

      showToast(`${ICONS.checkmark} Empresa atualizada com sucesso.`, "success");
      setEditOpen(false);
      setEditingTenantId(null);
      setCurrentAdmin(null);
      await load();
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : "Erro ao atualizar empresa.";
      showToast(`${ICONS.cross} ${msg}`, "error");
    } finally {
      setEditSaving(false);
    }
  };

  return (
    <div className="animate-fade-up">
      <div className="flex justify-end mb-4">
        <Button variant="primary" onClick={() => setOpen(true)}>
          {ICONS.plus} Nova Empresa
        </Button>
      </div>

      <div className="rounded-xl border border-border-subtle overflow-hidden">
        <table className="w-full border-collapse text-[13px]">
          <thead>
            <tr className="bg-surface-2">
              <th className="px-4 py-[11px] text-left text-[10.5px] font-bold tracking-[0.5px] uppercase text-text-muted border-b border-border-subtle">Empresa</th>
              <th className="px-4 py-[11px] text-left text-[10.5px] font-bold tracking-[0.5px] uppercase text-text-muted border-b border-border-subtle">CNPJ</th>
              <th className="px-4 py-[11px] text-left text-[10.5px] font-bold tracking-[0.5px] uppercase text-text-muted border-b border-border-subtle">Plano</th>
              <th className="px-4 py-[11px] text-left text-[10.5px] font-bold tracking-[0.5px] uppercase text-text-muted border-b border-border-subtle">Usuários</th>
              <th className="px-4 py-[11px] text-left text-[10.5px] font-bold tracking-[0.5px] uppercase text-text-muted border-b border-border-subtle">Títulos</th>
              <th className="px-4 py-[11px] text-left text-[10.5px] font-bold tracking-[0.5px] uppercase text-text-muted border-b border-border-subtle">Status</th>
              <th className="px-4 py-[11px] text-left text-[10.5px] font-bold tracking-[0.5px] uppercase text-text-muted border-b border-border-subtle">Editar</th>
              <th className="px-4 py-[11px] text-left text-[10.5px] font-bold tracking-[0.5px] uppercase text-text-muted border-b border-border-subtle">Ativo</th>
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr><td colSpan={8} className="text-center py-12 text-sm text-text-muted">Carregando empresas...</td></tr>
            ) : tenants.length === 0 ? (
              <tr><td colSpan={8} className="text-center py-12 text-sm text-text-muted">Nenhuma empresa cadastrada.</td></tr>
            ) : tenants.map(tenant => (
              <tr key={tenant.id} className="border-b border-border-subtle hover:bg-surface-2/50 transition-colors">
                <td className="px-4 py-[13px] font-semibold">{tenant.companyName}</td>
                <td className="px-4 py-[13px] text-text-secondary">{formatCnpj(tenant.taxId)}</td>
                <td className="px-4 py-[13px] text-text-secondary">{tenant.plan}</td>
                <td className="px-4 py-[13px] text-text-secondary">{tenant.userCount}</td>
                <td className="px-4 py-[13px] text-text-secondary">{tenant.titleCount}</td>
                <td className="px-4 py-[13px]">
                  <span className={`text-[11px] font-bold px-2 py-1 rounded-full ${tenant.active ? "bg-success/10 text-success" : "bg-text-muted/12 text-text-muted"}`}>
                    {tenant.active ? "Ativa" : "Inativa"}
                  </span>
                </td>
                <td className="px-4 py-[13px]">
                  <Button size="sm" variant="secondary" onClick={() => void openEdit(tenant.id)}>
                    {ICONS.pencil} Editar
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
                    <span>{tenant.active ? "Ativo" : "Inativo"}</span>
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
        title="Cadastrar Empresa"
        maxWidth={680}
        footer={<>
          <Button variant="secondary" onClick={() => setOpen(false)}>Cancelar</Button>
          <Button variant="primary" onClick={handleCreate}>{saving ? "Salvando..." : "Cadastrar Empresa"}</Button>
        </>}
      >
        <div className="grid grid-cols-2 gap-3">
          <FormInput label="Razão Social" value={form.companyName} onChange={e => setForm(f => ({ ...f, companyName: e.target.value }))} />
          <FormInput label="CNPJ" value={form.taxId} onChange={e => setForm(f => ({ ...f, taxId: formatCnpj(e.target.value) }))} placeholder="00.000.000/0001-00" />
          <FormInput label="Domínio de E-mail" value={form.emailDomain} onChange={e => setForm(f => ({ ...f, emailDomain: e.target.value }))} placeholder="empresa.com.br" />
          <div />
          <FormInput label="Nome do Admin" value={form.adminName} onChange={e => setForm(f => ({ ...f, adminName: e.target.value }))} />
          <FormInput label="E-mail do Admin" type="email" value={form.adminEmail} onChange={e => setForm(f => ({ ...f, adminEmail: e.target.value }))} />
          <div className="col-span-2">
            <FormInput label="Senha Inicial do Admin" type="password" value={form.adminPassword} onChange={e => setForm(f => ({ ...f, adminPassword: e.target.value }))} />
          </div>
        </div>
      </Modal>

      <Modal
        open={editOpen}
        onClose={() => {
          setEditOpen(false);
          setEditingTenantId(null);
          setCurrentAdmin(null);
          setEditAdminLogin(false);
        }}
        title="Editar Empresa"
        maxWidth={680}
        footer={<>
          <Button variant="secondary" onClick={() => {
            setEditOpen(false);
            setEditingTenantId(null);
            setCurrentAdmin(null);
            setEditAdminLogin(false);
          }}>Cancelar</Button>
          <Button variant="primary" onClick={handleUpdate}>{editSaving ? "Salvando..." : "Salvar Alterações"}</Button>
        </>}
      >
        {editLoading ? (
          <div className="text-sm text-text-muted py-4">Carregando dados da empresa...</div>
        ) : (
          <div className="grid grid-cols-2 gap-3">
            <FormInput label="Razão Social" value={editForm.companyName} onChange={e => setEditForm(f => ({ ...f, companyName: e.target.value }))} />
            <FormInput label="CNPJ" value={editForm.taxId} onChange={e => setEditForm(f => ({ ...f, taxId: formatCnpj(e.target.value) }))} placeholder="00.000.000/0001-00" />
            <FormInput label="Domínio de E-mail" value={editForm.emailDomain} onChange={e => setEditForm(f => ({ ...f, emailDomain: e.target.value }))} placeholder="empresa.com.br" />
            <div />

            {currentAdmin && (
              <div className="col-span-2 rounded-lg border border-border-subtle-2 bg-surface-2 px-3 py-2 text-sm">
                <div className="text-[11px] font-bold tracking-[0.6px] uppercase text-text-muted mb-1">Admin atual</div>
                <div className="text-text-primary font-semibold">{currentAdmin.name}</div>
                <div className="text-text-secondary text-xs mt-0.5">{currentAdmin.email}</div>
              </div>
            )}

            <div className="col-span-2 flex items-center gap-2 mt-1">
              <input
                id="edit-admin-login"
                type="checkbox"
                checked={editAdminLogin}
                onChange={e => setEditAdminLogin(e.target.checked)}
                className="accent-accent"
              />
              <label htmlFor="edit-admin-login" className="text-sm text-text-secondary">Editar dados de login do Admin</label>
            </div>

            {editAdminLogin && (
              <>
                <FormInput label="Nome do Admin" value={editForm.adminName} onChange={e => setEditForm(f => ({ ...f, adminName: e.target.value }))} />
                <FormInput label="E-mail do Admin" type="email" value={editForm.adminEmail} onChange={e => setEditForm(f => ({ ...f, adminEmail: e.target.value }))} />
                <div className="col-span-2">
                  <FormInput
                    label="Nova senha do Admin (opcional)"
                    type="password"
                    value={editForm.adminPassword}
                    onChange={e => setEditForm(f => ({ ...f, adminPassword: e.target.value }))}
                    placeholder="Deixe em branco para manter"
                  />
                </div>
              </>
            )}
          </div>
        )}
      </Modal>
    </div>
  );
};
