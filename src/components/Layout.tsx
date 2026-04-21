import { useEffect, useMemo, useRef, useState } from "react";
import { t } from "../i18n";
import { ICONS, type IconKey } from "../utils/icons";
import { colors } from "../utils/colors";
import { Button, FormInput, Modal } from "./UI";
import type { NavItem as NavItemType, ShowToast, ToastLogEntry } from "../types";
import { ApiError, getTemplates, getTitles, updateMyProfile, type StoredSession, type TenantResponse } from "../services/api";

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5013";

interface NotificationItem {
  id: string;
  kind: "api" | "pending" | "activity";
  title: string;
  subtitle?: string;
  dismissible?: boolean;
}

function formatToastKind(kind: ToastLogEntry["type"]) {
  if (kind === "error") return "ERRO";
  if (kind === "warn") return "ALERTA";
  if (kind === "success") return "SUCESSO";
  return "INFO";
}

function toastKindClass(kind: ToastLogEntry["type"]) {
  if (kind === "error") return "bg-danger/12 text-danger";
  if (kind === "warn") return "bg-warn/12 text-warn";
  if (kind === "success") return "bg-success/12 text-success";
  return "bg-accent/12 text-accent";
}

function shortText(value: string, max = 88) {
  if (value.length <= max) return value;
  return `${value.slice(0, max - 1)}...`;
}

// ── NavItem ───────────────────────────────────────────────────────────────
const NavItem = ({
  item,
  active,
  onClick,
}: {
  item: NavItemType;
  active: boolean;
  onClick: () => void;
}) => (
  <div
    onClick={onClick}
    className={`flex items-center gap-2.5 px-3 py-[9px] rounded-[9px] cursor-pointer transition-all duration-[170ms] text-[13.5px]
      ${active
        ? "bg-accent/12 border border-accent/30 text-accent font-semibold"
        : "border border-transparent text-text-secondary hover:bg-surface-2 hover:text-text-primary font-normal"
      }`}
  >
    <span className="text-base w-[18px] text-center">{ICONS[item.icon as IconKey]}</span>
    <span className="flex-1">{item.label}</span>
    {item.badge && (
      <span
        className={`text-[10px] font-extrabold px-1.5 py-px rounded-full min-w-[18px] text-center ${
          item.badgeType === "warn" ? "bg-warn text-black" : "bg-danger text-white"
        }`}
      >
        {item.badge}
      </span>
    )}
  </div>
);

// ── Role badge color ──────────────────────────────────────────────────────
function roleBadge(role: string) {
  if (role === "Master") return "bg-accent/10 text-accent";
  if (role === "Admin")  return "bg-success/10 text-success";
  return "bg-surface-3 text-text-muted";
}

const PAGE_META: Record<string, [string, string]> = {
  dashboard: [t("pageMeta.dashboard.title"), t("pageMeta.dashboard.subtitle")],
  analytics: [t("pageMeta.analytics.title"), t("pageMeta.analytics.subtitle")],
  titles: [t("pageMeta.titles.title"), t("pageMeta.titles.subtitle")],
  import: [t("pageMeta.import.title"), t("pageMeta.import.subtitle")],
  contacts: [t("pageMeta.contacts.title"), t("pageMeta.contacts.subtitle")],
  sequence: [t("pageMeta.sequence.title"), t("pageMeta.sequence.subtitle")],
  templates: [t("pageMeta.templates.title"), t("pageMeta.templates.subtitle")],
  integration: [t("pageMeta.integration.title"), t("pageMeta.integration.subtitle")],
  workers: [t("pageMeta.workers.title"), t("pageMeta.workers.subtitle")],
  tenants: [t("pageMeta.tenants.title"), t("pageMeta.tenants.subtitle")],
};

function navByRole(role: string) {
  if (role === "Master") {
    return [
      {
        section: t("nav.overview"),
        items: [
          { id: "dashboard", icon: "dashboard", label: t("nav.dashboard") },
          { id: "analytics", icon: "analytics", label: t("nav.analytics") },
        ],
      },
      {
        section: t("nav.configuration"),
        items: [
          { id: "tenants", icon: "users", label: t("nav.tenants") },
        ],
      },
    ];
  }

  if (role === "Admin") {
    return [
      {
        section: t("nav.overview"),
        items: [
          { id: "dashboard", icon: "dashboard", label: t("nav.dashboard") },
          { id: "analytics", icon: "analytics", label: t("nav.analytics") },
        ],
      },
      {
        section: t("nav.collection"),
        items: [
          { id: "titles", icon: "document", label: t("nav.titles") },
          { id: "import", icon: "upload", label: t("nav.importData") },
          { id: "contacts", icon: "users", label: t("nav.contactsCRM") },
          { id: "workers", icon: "users", label: t("nav.workers") },
        ],
      },
      {
        section: t("nav.configuration"),
        items: [
          { id: "sequence", icon: "settings", label: t("nav.collectionSequence") },
          { id: "templates", icon: "mail", label: t("nav.templates") },
          { id: "integration", icon: "link", label: t("nav.integrationSMTP") },
        ],
      },
    ];
  }

  return [
    {
      section: t("nav.overview"),
      items: [
        { id: "dashboard", icon: "dashboard", label: t("nav.dashboard") },
        { id: "analytics", icon: "analytics", label: t("nav.analytics") },
      ],
    },
    {
      section: t("nav.collection"),
      items: [
        { id: "titles", icon: "document", label: t("nav.titles") },
        { id: "import", icon: "upload", label: t("nav.importData") },
        { id: "contacts", icon: "users", label: t("nav.contactsCRM") },
      ],
    },
    {
      section: t("nav.configuration"),
      items: [
        { id: "sequence", icon: "settings", label: t("nav.collectionSequence") },
        { id: "templates", icon: "mail", label: t("nav.templates") },
      ],
    },
  ];
}

// ── Sidebar ───────────────────────────────────────────────────────────────
export const Sidebar = ({
  active,
  onNav,
  isDark,
  toggleTheme,
  session,
  showToast,
  onSessionUpdate,
  onLogout,
}: {
  active: string;
  onNav: (id: string) => void;
  isDark: boolean;
  toggleTheme: () => void;
  session: StoredSession;
  showToast: ShowToast;
  onSessionUpdate: (session: StoredSession) => void;
  onLogout: () => void;
}) => {
  const nav = navByRole(session.role);
  const [profileOpen, setProfileOpen] = useState(false);
  const [profileSaving, setProfileSaving] = useState(false);
  const [profileName, setProfileName] = useState(session.userName);
  const [profileEmail, setProfileEmail] = useState(session.email);
  const [profilePhotoUrl, setProfilePhotoUrl] = useState(session.photoUrl ?? "");
  const [removePhoto, setRemovePhoto] = useState(false);
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const profilePhotoInputRef = useRef<HTMLInputElement | null>(null);

  const initials = session.userName
    .split(" ")
    .map(w => w[0])
    .slice(0, 2)
    .join("")
    .toUpperCase();

  useEffect(() => {
    setProfileName(session.userName);
    setProfileEmail(session.email);
    setProfilePhotoUrl(session.photoUrl ?? "");
  }, [session.userName, session.email, session.photoUrl]);

  const handlePhotoUpload = (file: File | null) => {
    if (!file) return;
    if (file.size > 2 * 1024 * 1024) {
      showToast(`${ICONS.warning} A foto deve ter no máximo 2MB.`, "warn");
      return;
    }

    const reader = new FileReader();
    reader.onload = () => {
      const result = typeof reader.result === "string" ? reader.result : "";
      setProfilePhotoUrl(result);
      setRemovePhoto(false);
    };
    reader.readAsDataURL(file);
  };

  const handleSaveProfile = async () => {
    if (!profileName.trim() || !profileEmail.trim()) {
      showToast(`${ICONS.warning} Nome e e-mail são obrigatórios.`, "warn");
      return;
    }

    if (newPassword && newPassword.trim().length < 6) {
      showToast(`${ICONS.warning} A nova senha deve ter pelo menos 6 caracteres.`, "warn");
      return;
    }

    try {
      setProfileSaving(true);
      const updated = await updateMyProfile({
        name: profileName.trim(),
        email: profileEmail.trim(),
        currentPassword: currentPassword || undefined,
        newPassword: newPassword || undefined,
        photoUrl: removePhoto ? null : (profilePhotoUrl || undefined),
        removePhoto,
      });

      onSessionUpdate(updated);
      setProfileOpen(false);
      setCurrentPassword("");
      setNewPassword("");
      setRemovePhoto(false);
      showToast(`${ICONS.checkmark} Perfil atualizado com sucesso.`, "success");
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : "Erro ao atualizar perfil.";
      showToast(`${ICONS.cross} ${msg}`, "error");
    } finally {
      setProfileSaving(false);
    }
  };

  return (
    <aside className="w-[248px] bg-surface border-r border-border-subtle flex flex-col fixed top-0 left-0 bottom-0 z-[100]">
      {/* Logo */}
      <div className="px-[18px] pt-[22px] pb-[18px] border-b border-border-subtle flex items-center justify-center">
        <img
          src="/atos-logo.png"
          alt="Atos Capital"
          className="h-12 w-auto object-contain"
          style={isDark
            ? {
                filter: "brightness(0) saturate(100%) invert(21%) sepia(95%) saturate(2638%) hue-rotate(348deg) brightness(96%) contrast(95%)",
              }
            : undefined}
          onError={e => {
            const el = e.currentTarget as HTMLImageElement;
            el.style.display = "none";
            el.parentElement!.insertAdjacentHTML(
              "beforeend",
              `<span class="font-extrabold text-lg text-[#7A1414] tracking-tight">SmartCollect</span>`
            );
          }}
        />
      </div>

      {/* Nav */}
      <nav className="flex-1 px-2.5 py-3.5 overflow-y-auto flex flex-col gap-0.5">
        {nav.map(group => (
          <div key={group.section}>
            <div className="text-[10px] font-bold tracking-[1px] uppercase text-text-muted px-2.5 pt-3 pb-[5px]">
              {group.section}
            </div>
            {group.items.map(item => (
              <NavItem
                key={item.id}
                item={item}
                active={active === item.id}
                onClick={() => onNav(item.id)}
              />
            ))}
          </div>
        ))}
      </nav>

      {/* User area */}
      <div className="px-2.5 py-3 border-t border-border-subtle">
        <div className="flex items-center gap-2.5 px-[9px] py-2 rounded-[9px] mb-2">
          <button
            onClick={() => setProfileOpen(true)}
            className="w-[36px] h-[36px] bg-gradient-to-br from-accent to-[#B91C1C] rounded-full flex items-center justify-center font-extrabold text-[13px] shrink-0 text-white overflow-hidden border border-transparent hover:border-white/40 transition-colors cursor-pointer"
            title="Atualizar perfil"
          >
            {session.photoUrl ? (
              <img src={session.photoUrl} alt="Foto de perfil" className="w-full h-full object-cover" />
            ) : (
              initials
            )}
          </button>
          <div className="flex-1 min-w-0">
            <div className="flex items-center justify-between gap-2">
              <div className="text-[13px] font-semibold whitespace-nowrap overflow-hidden text-ellipsis">
                {session.userName}
              </div>
              <button
                onClick={toggleTheme}
                className="w-7 h-7 rounded-md bg-surface-2 border border-border-subtle flex items-center justify-center text-[13px] text-text-muted hover:text-text-primary hover:border-border-subtle-2 transition-colors cursor-pointer shrink-0"
                title={isDark ? "Modo Claro" : "Modo Escuro"}
              >
                {isDark ? "☀" : "☾"}
              </button>
            </div>
            <div className="text-[11px] text-text-muted whitespace-nowrap overflow-hidden text-ellipsis mt-0.5">
              {session.email}
            </div>
            <div className="flex items-center gap-1.5 mt-1">
              <span className={`text-[10px] font-bold px-1.5 py-px rounded-full ${roleBadge(session.role)}`}>
                {session.role}
              </span>
            </div>
          </div>
        </div>

        <button
          onClick={onLogout}
          className="mx-auto block min-w-[88px] px-4 py-1.5 rounded-md bg-danger text-white text-xs font-semibold hover:bg-[#B91C1C] transition-colors cursor-pointer"
          title="Sair"
        >
          Sair
        </button>
      </div>

      <Modal
        open={profileOpen}
        onClose={() => {
          setProfileOpen(false);
          setCurrentPassword("");
          setNewPassword("");
          setRemovePhoto(false);
        }}
        title="Atualizar perfil"
        footer={
          <>
            <Button variant="secondary" onClick={() => setProfileOpen(false)}>Cancelar</Button>
            <Button variant="primary" onClick={() => void handleSaveProfile()}>
              {profileSaving ? "Salvando..." : "Salvar alterações"}
            </Button>
          </>
        }
      >
        <div className="space-y-3">
          <div className="flex items-center gap-3">
            <button
              type="button"
              onClick={() => profilePhotoInputRef.current?.click()}
              className="w-14 h-14 rounded-full overflow-hidden bg-surface-2 border border-border-subtle flex items-center justify-center text-xs text-text-muted cursor-pointer hover:border-border-subtle-2 transition-colors"
              title="Clique para adicionar nova foto"
            >
              {profilePhotoUrl && !removePhoto ? (
                <img src={profilePhotoUrl} alt="Foto" className="w-full h-full object-cover" />
              ) : (
                "Sem foto"
              )}
            </button>
            <div className="flex-1">
              <input
                ref={profilePhotoInputRef}
                type="file"
                accept="image/*"
                onChange={e => handlePhotoUpload(e.target.files?.[0] ?? null)}
                className="hidden"
              />
              <div className="text-xs text-text-muted">Clique na foto para adicionar uma nova imagem</div>
              <button
                type="button"
                onClick={() => {
                  if (window.confirm("Deseja remover a foto atual?")) {
                    setRemovePhoto(true);
                    setProfilePhotoUrl("");
                  }
                }}
                className="mt-1 text-xs text-text-secondary hover:text-danger underline bg-transparent border-none p-0 cursor-pointer"
              >
                Remover foto atual
              </button>
            </div>
          </div>

          <FormInput
            label="Nome"
            value={profileName}
            onChange={e => setProfileName(e.target.value)}
            placeholder="Seu nome"
          />
          <FormInput
            label="E-mail"
            type="email"
            value={profileEmail}
            onChange={e => setProfileEmail(e.target.value)}
            placeholder="seu@email.com"
          />
          <FormInput
            label="Senha atual"
            type="password"
            value={currentPassword}
            onChange={e => setCurrentPassword(e.target.value)}
            placeholder="Informe para trocar a senha"
          />
          <FormInput
            label="Nova senha"
            type="password"
            value={newPassword}
            onChange={e => setNewPassword(e.target.value)}
            placeholder="Mínimo de 6 caracteres"
          />
        </div>
      </Modal>
    </aside>
  );
};

// ── Topbar ────────────────────────────────────────────────────────────────
export const Topbar = ({
  page,
  onImport,
  showToast,
  session,
  tenants,
  selectedTenantId,
  onSelectTenant,
  toastLogs,
  unreadToastCount,
  onMarkToastLogsRead,
  onClearToastLogs,
}: {
  page: string;
  onImport: () => void;
  showToast: ShowToast;
  session: StoredSession;
  tenants: TenantResponse[];
  selectedTenantId?: string;
  onSelectTenant?: (tenantId: string) => void;
  toastLogs: ToastLogEntry[];
  unreadToastCount: number;
  onMarkToastLogsRead: () => void;
  onClearToastLogs: () => void;
}) => {
  const [title, subtitle] = PAGE_META[page] || ["SmartCollect", ""];
  const showTenantSelector = session.role === "Master";
  const isMaster = session.role === "Master";

  const [isApiConnected, setIsApiConnected] = useState(false);
  const [notificationsOpen, setNotificationsOpen] = useState(false);
  const [notificationsLoading, setNotificationsLoading] = useState(true);
  const [notificationsInitialized, setNotificationsInitialized] = useState(false);
  const [notifications, setNotifications] = useState<NotificationItem[]>([]);
  const [dismissedNotificationIds, setDismissedNotificationIds] = useState<string[]>([]);
  const [expandedToastLogIds, setExpandedToastLogIds] = useState<string[]>([]);
  const [titlesSubtitle, setTitlesSubtitle] = useState("");
  const [templatesSubtitle, setTemplatesSubtitle] = useState("");

  const notifRef = useRef<HTMLDivElement | null>(null);

  const visibleNotifications = useMemo(
    () => notifications.filter(item => !dismissedNotificationIds.includes(item.id)),
    [notifications, dismissedNotificationIds]
  );

  const dismissNotification = (id: string) => {
    setDismissedNotificationIds(prev => (prev.includes(id) ? prev : [...prev, id]));
  };

  const toggleToastLog = (id: string) => {
    setExpandedToastLogIds(previous => (
      previous.includes(id)
        ? previous.filter(itemId => itemId !== id)
        : [...previous, id]
    ));
  };

  const clearAllNotifications = () => {
    setDismissedNotificationIds(prev => {
      const all = new Set(prev);
      notifications.forEach(item => all.add(item.id));
      return [...all];
    });

    onClearToastLogs();
    setExpandedToastLogIds([]);
  };

  const hasBellNotifications = unreadToastCount > 0 || (!notificationsLoading && visibleNotifications.length > 0);

  useEffect(() => {
    if (isMaster) return;

    let cancelled = false;

    const fetchWithTimeout = async (url: string, init: RequestInit, timeoutMs = 10000) => {
      const controller = new AbortController();
      const timeout = setTimeout(() => controller.abort(), timeoutMs);

      try {
        return await fetch(url, { ...init, signal: controller.signal });
      } finally {
        clearTimeout(timeout);
      }
    };

    const loadStatusAndNotifications = async () => {
      try {
        if (!notificationsInitialized)
          setNotificationsLoading(true);

        const healthResp = await fetchWithTimeout(`${API_BASE_URL}/api/sync/health`, {
          headers: {
            Accept: "application/json",
            Authorization: `Bearer ${session.token}`,
          },
        });

        if (!healthResp.ok) throw new Error(`health_${healthResp.status}`);

        const healthJson = await healthResp.json() as { connected?: boolean; checkedAt?: string };
        const connected = Boolean(healthJson.connected);
        const checkedAt = healthJson.checkedAt
          ? new Date(healthJson.checkedAt).toLocaleString("pt-BR")
          : new Date().toLocaleString("pt-BR");

        const nextNotifications: NotificationItem[] = [
          {
            id: "api-status",
            kind: "api",
            title: connected ? "API externa conectada" : "API externa indisponivel",
            subtitle: connected
              ? `Ultima verificacao: ${checkedAt}`
              : `Falha na verificacao da API externa. Ultima tentativa: ${checkedAt}`,
            dismissible: true,
          },
        ];

        if (!connected) {
          if (!cancelled) {
            setIsApiConnected(false);
            nextNotifications.push({
              id: "external-api-offline",
              kind: "api",
              title: "Integracao pausada",
              subtitle: "Sincronizacao e importacao via integracao podem falhar.",
              dismissible: true,
            });
            setNotifications(nextNotifications);
          }
          return;
        }

        if (cancelled) return;

        setIsApiConnected(true);

        const [pendingResp, activityResp] = await Promise.all([
          fetchWithTimeout(`${API_BASE_URL}/api/titles?status=PendingData&page=1&pageSize=1`, {
            headers: {
              Accept: "application/json",
              Authorization: `Bearer ${session.token}`,
            },
          }),
          fetchWithTimeout(`${API_BASE_URL}/api/dashboard/activity-log`, {
            headers: {
              Accept: "application/json",
              Authorization: `Bearer ${session.token}`,
            },
          }),
        ]);

        if (pendingResp.ok) {
          const pendingJson = await pendingResp.json() as { totalCount?: number };
          const totalPending = pendingJson.totalCount ?? 0;
          if (totalPending > 0) {
            nextNotifications.push({
              id: "pending-data",
              kind: "pending",
              title: `${totalPending} titulos com pendencia de dados`,
              subtitle: "Atualize contatos para liberar envios automáticos",
              dismissible: true,
            });
          }
        }

        if (activityResp.ok) {
          const activityJson = await activityResp.json() as { items?: Array<{ id: string; status: string; summary: string; timestamp: string }> };
          (activityJson.items ?? []).slice(0, 3).forEach(item => {
            nextNotifications.push({
              id: `activity-${item.id}`,
              kind: "activity",
              title: shortText(item.summary, 80),
              subtitle: `${item.status} • ${new Date(item.timestamp).toLocaleString("pt-BR")}`,
              dismissible: true,
            });
          });
        }

        if (!cancelled) setNotifications(nextNotifications);
      } catch {
        if (!cancelled) {
          setIsApiConnected(false);
          setNotifications([
            {
              id: "external-api-offline",
              kind: "api",
              title: "API externa indisponivel",
              subtitle: "Nao foi possivel validar conexao com a API de integracao.",
              dismissible: true,
            },
          ]);
        }
      } finally {
        if (!cancelled)
        {
          setNotificationsLoading(false);
          setNotificationsInitialized(true);
        }
      }
    };

    void loadStatusAndNotifications();
    const timer = setInterval(loadStatusAndNotifications, 30000);

    return () => {
      cancelled = true;
      clearInterval(timer);
    };
  }, [isMaster, session.token, notificationsInitialized]);

  useEffect(() => {
    if (!notificationsOpen) return;

    const onClickOutside = (event: MouseEvent) => {
      if (!notifRef.current) return;
      if (!notifRef.current.contains(event.target as Node)) {
        setNotificationsOpen(false);
      }
    };

    document.addEventListener("mousedown", onClickOutside);
    return () => document.removeEventListener("mousedown", onClickOutside);
  }, [notificationsOpen]);

  useEffect(() => {
    let cancelled = false;

    if (page !== "titles") setTitlesSubtitle("");
    if (page !== "templates") setTemplatesSubtitle("");

    if (page === "titles") {
      const loadTitleCount = async () => {
        if (isMaster && !selectedTenantId) {
          if (!cancelled) setTitlesSubtitle("Selecione uma empresa");
          return;
        }

        try {
          const response = await getTitles({
            tenantId: isMaster ? selectedTenantId || undefined : undefined,
            page: 1,
            pageSize: 1,
          });
          if (!cancelled) {
            setTitlesSubtitle(`${response.totalCount} titulos`);
          }
        } catch {
          if (!cancelled) setTitlesSubtitle("");
        }
      };

      void loadTitleCount();
    }

    if (page === "templates") {
      const loadTemplateCount = async () => {
        try {
          const response = await getTemplates();
          const activeCount = response.filter(tpl => tpl.active).length;
          if (!cancelled) {
            setTemplatesSubtitle(`${activeCount} templates ativos`);
          }
        } catch {
          if (!cancelled) setTemplatesSubtitle("");
        }
      };

      void loadTemplateCount();
    }

    return () => {
      cancelled = true;
    };
  }, [isMaster, page, selectedTenantId, session.token]);

  const displayTitle = page === "titles" ? t("nav.titles") : page === "templates" ? t("nav.templates") : title;
  const displaySubtitle = page === "titles"
    ? titlesSubtitle
    : page === "templates"
      ? templatesSubtitle
      : subtitle;

  return (
    <div className="h-[60px] bg-surface border-b border-border-subtle flex items-center justify-between px-7 sticky top-0 z-50">
      <div>
        <div className="font-extrabold text-[17px] tracking-[-0.3px] text-text-primary">{displayTitle}</div>
        {displaySubtitle && <div className="text-xs text-text-muted mt-px">{displaySubtitle}</div>}
      </div>

      <div className="flex items-center gap-2.5">
        {showTenantSelector && (
          <select
            value={selectedTenantId ?? ""}
            onChange={e => onSelectTenant?.(e.target.value)}
            className="bg-surface-2 border border-border-subtle-2 rounded-lg px-[10px] py-[8px] text-[12px] text-text-primary outline-none min-w-[220px]"
            title="Selecionar empresa"
          >
            <option value="">Todas as empresas (visao global)</option>
            {tenants.map(tenant => (
              <option key={tenant.id} value={tenant.id}>
                {tenant.companyName}
              </option>
            ))}
          </select>
        )}

        {!isMaster && (
          <>
            <div className="flex items-center gap-1.5 text-xs text-text-muted">
              <span className={`${isApiConnected ? "text-success" : "text-danger"} animate-pulse-dot`}>{ICONS.dot}</span>
              {isApiConnected ? "API externa conectada" : "API externa indisponivel"}
            </div>

            <div className="relative" ref={notifRef}>
              <button
                onClick={() => {
                  setNotificationsOpen(previous => {
                    const next = !previous;
                    if (next) onMarkToastLogsRead();
                    return next;
                  });
                }}
                className="w-9 h-9 bg-surface-2 border border-border-subtle rounded-[9px] flex items-center justify-center cursor-pointer text-[15px] relative"
                title="Notificações"
              >
                {ICONS.bell}
                {hasBellNotifications && (
                  <span className="absolute top-[7px] right-[7px] w-[7px] h-[7px] bg-danger rounded-full border-2 border-surface" />
                )}
              </button>

              {notificationsOpen && (
                <div className="absolute right-0 mt-2 w-[360px] bg-surface border border-border-subtle rounded-xl shadow-[0_16px_40px_rgba(0,0,0,0.35)] z-50 overflow-hidden">
                  <div className="px-4 py-3 border-b border-border-subtle flex items-center justify-between gap-3">
                    <span className="text-sm font-semibold">Notificacoes</span>
                    <button
                      onClick={clearAllNotifications}
                      className="text-xs text-text-muted hover:text-text-primary transition-colors"
                    >
                      Limpar tudo
                    </button>
                  </div>
                  <div className="max-h-[320px] overflow-y-auto">
                    {toastLogs.length > 0 && (
                      <>
                        <div className="px-4 py-2 border-b border-border-subtle text-[11px] font-bold tracking-[0.6px] uppercase text-text-muted">
                          Alertas de envio
                        </div>
                        {toastLogs.map(item => {
                          const expanded = expandedToastLogIds.includes(item.id);
                          return (
                            <div key={item.id} className="px-4 py-3 border-b border-border-subtle last:border-b-0">
                              <div className="flex items-start justify-between gap-2">
                                <div className="min-w-0 flex-1">
                                  <div className="flex items-center gap-2 mb-1.5">
                                    <span className={`text-[10px] font-bold px-1.5 py-px rounded-full ${toastKindClass(item.type)}`}>
                                      {formatToastKind(item.type)}
                                    </span>
                                    {!item.read && <span className="text-[10px] text-danger font-bold">nova</span>}
                                  </div>
                                  <div className="text-sm text-text-primary">{item.summary}</div>
                                  <div className="text-[11px] text-text-muted mt-1">
                                    {new Date(item.createdAt).toLocaleString("pt-BR")}
                                  </div>
                                  {expanded && (
                                    <div className="text-xs text-text-secondary mt-2 bg-surface-2 border border-border-subtle rounded-md px-2.5 py-2 whitespace-normal break-words">
                                      {item.fullMessage}
                                    </div>
                                  )}
                                </div>
                                <button
                                  onClick={() => toggleToastLog(item.id)}
                                  className="text-[11px] text-accent hover:text-[#B91C1C] transition-colors whitespace-nowrap"
                                >
                                  {expanded ? "Ocultar" : "Ver completo"}
                                </button>
                              </div>
                            </div>
                          );
                        })}
                      </>
                    )}

                    <div className="px-4 py-2 border-b border-border-subtle text-[11px] font-bold tracking-[0.6px] uppercase text-text-muted">
                      Status do sistema
                    </div>

                    {notificationsLoading ? (
                      <div className="px-4 py-4 text-sm text-text-muted">Carregando notificacoes...</div>
                    ) : visibleNotifications.length === 0 ? (
                      toastLogs.length === 0
                        ? <div className="px-4 py-4 text-sm text-text-muted">Nenhuma notificacao no momento.</div>
                        : null
                    ) : (
                      visibleNotifications.map(item => (
                        <div key={item.id} className="px-4 py-3 border-b border-border-subtle last:border-b-0">
                          <div className="flex items-start justify-between gap-2">
                            <div className="min-w-0">
                              <div className="flex items-center gap-2 mb-1">
                                <span className={`text-[10px] font-bold px-1.5 py-px rounded-full ${
                                  item.kind === "api"
                                    ? "bg-accent/10 text-accent"
                                    : item.kind === "pending"
                                    ? "bg-warn/10 text-warn"
                                    : "bg-success/10 text-success"
                                }`}>
                                  {item.kind === "api" ? "API" : item.kind === "pending" ? "PENDENCIA" : "ATIVIDADE"}
                                </span>
                              </div>
                              <div className="text-sm text-text-primary">{item.title}</div>
                              {item.subtitle && <div className="text-xs text-text-muted mt-1">{item.subtitle}</div>}
                            </div>
                            {item.dismissible && (
                              <button
                                onClick={() => dismissNotification(item.id)}
                                className="text-text-muted hover:text-danger text-sm leading-none px-1"
                                title="Excluir notificacao"
                              >
                                {ICONS.close}
                              </button>
                            )}
                          </div>
                        </div>
                      ))
                    )}
                  </div>
                </div>
              )}
            </div>
          </>
        )}

        {/* Only show Import button for Admin/Worker on operational pages */}
        {!isMaster && page !== "workers" && (
          <Button variant="primary" onClick={onImport}>
            {ICONS.upload} {t("common.import")}
          </Button>
        )}
      </div>
    </div>
  );
};
