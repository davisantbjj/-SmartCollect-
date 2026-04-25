import { useEffect, useState } from "react";
import { Badge, Button, FormInput, Modal } from "../components/UI";
import { ICONS } from "../utils/icons";
import {
  ApiError,
  deleteWorker,
  getWorkers,
  registerTenantUser,
  updateWorker,
  type StoredSession,
  type TenantUserRole,
  type WorkerResponse,
} from "../services/api";
import type { ShowToast } from "../types";

export const PageWorkers = ({
  showToast,
  session,
  selectedTenantId,
}: {
  showToast: ShowToast;
  session: StoredSession;
  selectedTenantId?: string;
}) => {
  const [workers, setWorkers] = useState<WorkerResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [createOpen, setCreateOpen] = useState(false);
  const [editOpen, setEditOpen] = useState(false);
  const [selected, setSelected] = useState<WorkerResponse | null>(null);
  const [saving, setSaving] = useState(false);
  const [updatingWorkerId, setUpdatingWorkerId] = useState<string | null>(null);
  const [deletingWorkerId, setDeletingWorkerId] = useState<string | null>(null);

  const [newName, setNewName] = useState("");
  const [newEmail, setNewEmail] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [newRole, setNewRole] = useState<TenantUserRole>("Worker");

  const [editName, setEditName] = useState("");
  const [editEmail, setEditEmail] = useState("");
  const [editPassword, setEditPassword] = useState("");

  const tenantId = session.role === "Master" ? selectedTenantId : undefined;
  const requiresTenantSelection = session.role === "Master" && !tenantId;

  const load = async () => {
    if (requiresTenantSelection) {
      setWorkers([]);
      setLoading(false);
      return;
    }

    try {
      setLoading(true);
      setWorkers(await getWorkers(tenantId));
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : "Erro ao carregar usuarios.";
      showToast(`${ICONS.cross} ${msg}`, "error");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void load();
  }, [tenantId, requiresTenantSelection]);

  const openEdit = (worker: WorkerResponse) => {
    setSelected(worker);
    setEditName(worker.name);
    setEditEmail(worker.email);
    setEditPassword("");
    setEditOpen(true);
  };

  const handleCreate = async () => {
    if (!newName.trim() || !newEmail.trim() || !newPassword) {
      showToast(`${ICONS.warning} Preencha nome, e-mail e senha.`, "warn");
      return;
    }

    if (requiresTenantSelection) {
      showToast(`${ICONS.warning} Selecione uma empresa para cadastrar usuarios.`, "warn");
      return;
    }

    try {
      setSaving(true);
      await registerTenantUser(newName.trim(), newEmail.trim(), newPassword, newRole, tenantId);
      showToast(`${ICONS.checkmark} Usuario cadastrado com sucesso.`, "success");
      setCreateOpen(false);
      setNewName("");
      setNewEmail("");
      setNewPassword("");
      setNewRole("Worker");
      await load();
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : "Erro ao cadastrar usuario.";
      showToast(`${ICONS.cross} ${msg}`, "error");
    } finally {
      setSaving(false);
    }
  };

  const handleUpdate = async () => {
    if (!selected) return;

    if (!editName.trim()) {
      showToast(`${ICONS.warning} Nome do usuario e obrigatorio.`, "warn");
      return;
    }

    if (!editEmail.trim()) {
      showToast(`${ICONS.warning} E-mail do usuario e obrigatorio.`, "warn");
      return;
    }

    if (requiresTenantSelection) {
      showToast(`${ICONS.warning} Selecione uma empresa para atualizar usuarios.`, "warn");
      return;
    }

    try {
      setSaving(true);
      await updateWorker(selected.id, {
        name: editName.trim(),
        email: editEmail.trim(),
        active: selected.active,
        password: editPassword.trim() || undefined,
      }, tenantId);
      showToast(`${ICONS.checkmark} Usuario atualizado com sucesso.`, "success");
      setEditOpen(false);
      await load();
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : "Erro ao atualizar usuario.";
      showToast(`${ICONS.cross} ${msg}`, "error");
    } finally {
      setSaving(false);
    }
  };

  const handleDelete = async (worker: WorkerResponse) => {
    if (requiresTenantSelection) {
      showToast(`${ICONS.warning} Selecione uma empresa para excluir usuarios.`, "warn");
      return;
    }

    const confirmed = window.confirm(`Tem certeza que deseja excluir definitivamente o usuario "${worker.name}"?`);
    if (!confirmed) return;

    try {
      setDeletingWorkerId(worker.id);
      await deleteWorker(worker.id, tenantId);
      showToast(`${ICONS.checkmark} Usuario excluido com sucesso.`, "success");
      await load();
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : "Erro ao excluir usuario.";
      showToast(`${ICONS.cross} ${msg}`, "error");
    } finally {
      setDeletingWorkerId(null);
    }
  };

  const handleToggleAccess = async (worker: WorkerResponse) => {
    if (requiresTenantSelection) {
      showToast(`${ICONS.warning} Selecione uma empresa para atualizar usuarios.`, "warn");
      return;
    }

    const nextActive = !worker.active;
    const confirmed = window.confirm(
      `Deseja ${nextActive ? "reativar" : "inativar"} o acesso de "${worker.name}"?`
    );
    if (!confirmed) return;

    try {
      setUpdatingWorkerId(worker.id);
      await updateWorker(worker.id, {
        name: worker.name,
        email: worker.email,
        active: nextActive,
      }, tenantId);
      showToast(
        `${ICONS.checkmark} Acesso do usuario ${nextActive ? "reativado" : "inativado"} com sucesso.`,
        "success"
      );
      await load();
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : "Erro ao atualizar acesso do usuario.";
      showToast(`${ICONS.cross} ${msg}`, "error");
    } finally {
      setUpdatingWorkerId(null);
    }
  };

  return (
    <div className="animate-fade-up">
      <div className="flex justify-end mb-4">
        <Button
          variant="primary"
          onClick={() => setCreateOpen(true)}
          disabled={requiresTenantSelection}
        >
          {ICONS.plus} Novo Usuario
        </Button>
      </div>

      {requiresTenantSelection && (
        <div className="rounded-xl border border-border-subtle bg-surface-2/60 px-4 py-3 text-sm text-text-secondary mb-4">
          Selecione uma empresa no topo para gerenciar usuarios.
        </div>
      )}

      <div className="rounded-xl border border-border-subtle overflow-hidden">
        <table className="w-full border-collapse text-[13px]">
          <thead>
            <tr className="bg-surface-2">
              <th className="px-4 py-[11px] text-left text-[10.5px] font-bold tracking-[0.5px] uppercase text-text-muted border-b border-border-subtle">Nome</th>
              <th className="px-4 py-[11px] text-left text-[10.5px] font-bold tracking-[0.5px] uppercase text-text-muted border-b border-border-subtle">E-mail</th>
              <th className="px-4 py-[11px] text-left text-[10.5px] font-bold tracking-[0.5px] uppercase text-text-muted border-b border-border-subtle">Perfil</th>
              <th className="px-4 py-[11px] text-left text-[10.5px] font-bold tracking-[0.5px] uppercase text-text-muted border-b border-border-subtle">Status</th>
              <th className="px-4 py-[11px] text-left text-[10.5px] font-bold tracking-[0.5px] uppercase text-text-muted border-b border-border-subtle">Ultimo Login</th>
              <th className="px-4 py-[11px] text-left text-[10.5px] font-bold tracking-[0.5px] uppercase text-text-muted border-b border-border-subtle">Acoes</th>
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr><td colSpan={6} className="text-center py-12 text-sm text-text-muted">Carregando usuarios...</td></tr>
            ) : workers.length === 0 ? (
              <tr><td colSpan={6} className="text-center py-12 text-sm text-text-muted">Nenhum usuario cadastrado.</td></tr>
            ) : workers.map(worker => (
              <tr key={worker.id} className="border-b border-border-subtle hover:bg-surface-2/50 transition-colors">
                <td className="px-4 py-[13px] font-semibold">{worker.name}</td>
                <td className="px-4 py-[13px] text-text-secondary">{worker.email}</td>
                <td className="px-4 py-[13px]">
                  <span className={`inline-flex rounded-full px-2 py-0.5 text-xs font-semibold ${worker.role === "Admin" ? "bg-accent/12 text-accent" : "bg-surface-2 text-text-secondary"}`}>
                    {worker.role === "Admin" ? "Admin" : "Worker"}
                  </span>
                </td>
                <td className="px-4 py-[13px]">
                  <Badge status={worker.active ? "complete" : "cancelled"} />
                </td>
                <td className="px-4 py-[13px] text-text-secondary text-xs">
                  {worker.lastLogin ? new Date(worker.lastLogin).toLocaleString("pt-BR") : "Nunca acessou"}
                </td>
                <td className="px-4 py-[13px]">
                  <div className="flex gap-1.5">
                    <Button size="sm" variant="secondary" onClick={() => openEdit(worker)}>
                      {ICONS.pencil} Editar
                    </Button>
                    <button
                      type="button"
                      role="switch"
                      aria-checked={worker.active}
                      disabled={updatingWorkerId === worker.id}
                      onClick={() => void handleToggleAccess(worker)}
                      className={`inline-flex items-center gap-2 rounded-full border px-2 py-1 text-xs font-semibold transition-colors ${worker.active ? "border-success/30 bg-success/12 text-success" : "border-border-subtle-2 bg-surface-2 text-text-muted"} ${updatingWorkerId === worker.id ? "opacity-60 cursor-not-allowed" : "cursor-pointer"}`}
                    >
                      <span
                        className={`h-4 w-7 rounded-full p-[2px] transition-colors ${worker.active ? "bg-success/75" : "bg-text-muted/40"}`}
                      >
                        <span className={`block h-3 w-3 rounded-full bg-white transition-transform ${worker.active ? "translate-x-3" : "translate-x-0"}`} />
                      </span>
                      <span>{worker.active ? "Ativo" : "Inativo"}</span>
                    </button>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <Modal
        open={createOpen}
        onClose={() => setCreateOpen(false)}
        title="Novo Usuario"
        footer={<>
          <Button variant="secondary" onClick={() => setCreateOpen(false)}>Cancelar</Button>
          <Button variant="primary" onClick={handleCreate}>{saving ? "Salvando..." : "Cadastrar"}</Button>
        </>}
      >
        <div className="grid gap-3">
          <FormInput label="Nome" value={newName} onChange={e => setNewName(e.target.value)} />
          <FormInput label="E-mail" type="email" value={newEmail} onChange={e => setNewEmail(e.target.value)} />
          <FormInput label="Senha" type="password" value={newPassword} onChange={e => setNewPassword(e.target.value)} />
          <div>
            <label className="block text-xs font-bold uppercase text-text-muted tracking-wider mb-1.5">Perfil</label>
            <select
              value={newRole}
              onChange={e => setNewRole(e.target.value as TenantUserRole)}
              className="w-full rounded-lg border border-border-subtle-2 bg-surface px-3.5 py-2.5 text-[13px] text-text-primary outline-none transition-[border-color] focus:border-accent focus:ring-2 focus:ring-accent/10"
            >
              <option value="Worker">Worker</option>
              <option value="Admin">Admin</option>
            </select>
          </div>
        </div>
      </Modal>

      <Modal
        open={editOpen}
        onClose={() => setEditOpen(false)}
        title="Editar Usuario"
        footer={<>
          {selected && !selected.active && (
            <Button
              variant="danger"
              disabled={deletingWorkerId === selected.id}
              onClick={() => void handleDelete(selected)}
            >
              {deletingWorkerId === selected.id ? "Excluindo..." : `${ICONS.cross} Excluir`}
            </Button>
          )}
          <Button variant="secondary" onClick={() => setEditOpen(false)}>Cancelar</Button>
          <Button variant="primary" onClick={handleUpdate}>{saving ? "Salvando..." : "Salvar Alteracoes"}</Button>
        </>}
      >
        <div className="grid gap-3">
          <FormInput label="Nome" value={editName} onChange={e => setEditName(e.target.value)} />
          <FormInput label="E-mail" type="email" value={editEmail} onChange={e => setEditEmail(e.target.value)} />
          <FormInput
            label="Nova senha (opcional)"
            type="password"
            value={editPassword}
            onChange={e => setEditPassword(e.target.value)}
            placeholder="Deixe em branco para manter"
          />
        </div>
      </Modal>
    </div>
  );
};
