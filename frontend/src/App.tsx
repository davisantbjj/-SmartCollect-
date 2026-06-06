import { Suspense, lazy, useCallback, useEffect, useMemo, useState } from "react";
import { t } from "./i18n";
import { useToast } from "./hooks/useToast";
import { Sidebar, Topbar } from "./components/Layout";
import { LoadingState, Toast } from "./components/UI";
import type { PageId } from "./types";
import {
  ApiError,
  getTenants,
  getSession,
  login,
  setSession,
  type StoredSession,
  type TenantResponse,
} from "./services/api";

// Lazy load pages for performance
const PageDashboard  = lazy(() => import("./pages/PageDashboard").then(m => ({ default: m.PageDashboard })));
const PageAnalytics  = lazy(() => import("./pages/PageAnalytics").then(m => ({ default: m.PageAnalytics })));
const PageTitles     = lazy(() => import("./pages/PageTitles").then(m => ({ default: m.PageTitles })));
const PageImport     = lazy(() => import("./pages/PageImport").then(m => ({ default: m.PageImport })));
const PageContacts   = lazy(() => import("./pages/PageContacts").then(m => ({ default: m.PageContacts })));
const PageSequence   = lazy(() => import("./pages/PageSequence").then(m => ({ default: m.PageSequence })));
const PageTemplates  = lazy(() => import("./pages/PageTemplates").then(m => ({ default: m.PageTemplates })));
const PageIntegration= lazy(() => import("./pages/PageIntegration").then(m => ({ default: m.PageIntegration })));
const PageWorkers    = lazy(() => import("./pages/PageWorkers").then(m => ({ default: m.PageWorkers })));
const PageTenants    = lazy(() => import("./pages/PageTenants").then(m => ({ default: m.PageTenants })));

const LOGIN_RECENT_EMAILS_KEY = "smartcollect.login.recentEmails";
const LOGIN_RECENT_EMAILS_MAX = 6;
const LAST_PAGE_KEY = "smartcollect.lastPage";
const MASTER_SELECTED_TENANT_KEY = "smartcollect.master.selectedTenantId";
const ALL_PAGE_IDS: PageId[] = [
  "dashboard",
  "analytics",
  "titles",
  "import",
  "contacts",
  "sequence",
  "templates",
  "integration",
  "workers",
  "tenants",
];

function formatLoginRetryAfter(totalSeconds: number): string {
  const safeSeconds = Math.max(0, Math.ceil(totalSeconds));
  const minutes = Math.floor(safeSeconds / 60);
  const seconds = safeSeconds % 60;

  if (minutes <= 0) return `${seconds}s`;
  if (seconds <= 0) return `${minutes}min`;
  return `${minutes}min ${seconds}s`;
}

function isPageId(value: string): value is PageId {
  return ALL_PAGE_IDS.includes(value as PageId);
}

function getInitialPage(): PageId {
  try {
    const raw = localStorage.getItem(LAST_PAGE_KEY);
    if (raw && isPageId(raw))
      return raw;
  } catch {
    // Ignore storage read issues and keep default page.
  }

  return "dashboard";
}

export default function SmartCollect() {
  const [page, setPage] = useState<PageId>(() => getInitialPage());
  const [session, setSessionState] = useState<StoredSession | null>(() => getSession());
  const [tenants, setTenants] = useState<TenantResponse[]>([]);
  const [tenantsLoaded, setTenantsLoaded] = useState(false);
  const [selectedTenantId, setSelectedTenantId] = useState(() => {
    try {
      return localStorage.getItem(MASTER_SELECTED_TENANT_KEY) ?? "";
    } catch {
      return "";
    }
  });
  const [contactsSubtitle, setContactsSubtitle] = useState("");
  const [titlesPreset, setTitlesPreset] = useState<{ status: string; token: number } | null>(null);
  const {
    toast,
    show: showToast,
    logs: toastLogs,
    unreadCount: unreadToastCount,
    markAllRead: markAllToastRead,
    clearLogs: clearToastLogs,
  } = useToast();

  // Theme toggle
  const [isDark, setIsDark] = useState(() => {
    return localStorage.getItem("sc.theme") === "dark";
  });
  useEffect(() => {
    document.documentElement.classList.toggle("dark", isDark);
    localStorage.setItem("sc.theme", isDark ? "dark" : "light");
  }, [isDark]);
  const toggleTheme = useCallback(() => setIsDark(prev => !prev), []);

  // Login form state
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [authLoading, setAuthLoading] = useState(false);
  const [showPassword, setShowPassword] = useState(false);
  const [loginRateLimitedUntil, setLoginRateLimitedUntil] = useState<number | null>(() => {
    try {
      const stored = localStorage.getItem("smartcollect.loginRateLimitedUntil");
      if (stored) {
        const parsed = parseInt(stored, 10);
        if (!isNaN(parsed) && parsed > Date.now()) {
          return parsed;
        }
      }
    } catch {
      // Ignore storage read issues
    }
    return null;
  });
  const [loginRateLimitRemaining, setLoginRateLimitRemaining] = useState(0);
  const loginFieldNonce = useMemo(() => Math.random().toString(36).slice(2, 10), []);
  const [recentEmails, setRecentEmails] = useState<string[]>(() => {
    try {
      const raw = localStorage.getItem(LOGIN_RECENT_EMAILS_KEY);
      if (!raw) return [];
      const parsed = JSON.parse(raw);
      if (!Array.isArray(parsed)) return [];
      return parsed.filter((item): item is string => typeof item === "string");
    } catch {
      return [];
    }
  });
  const isLoginRateLimited = loginRateLimitRemaining > 0;
  const loginRetryAfterLabel = formatLoginRetryAfter(loginRateLimitRemaining);

  useEffect(() => {
    if (loginRateLimitedUntil === null) {
      localStorage.removeItem("smartcollect.loginRateLimitedUntil");
    } else {
      localStorage.setItem("smartcollect.loginRateLimitedUntil", loginRateLimitedUntil.toString());
    }
  }, [loginRateLimitedUntil]);

  useEffect(() => {
    if (!loginRateLimitedUntil) {
      setLoginRateLimitRemaining(0);
      return;
    }

    const updateRemaining = () => {
      const remaining = Math.max(0, Math.ceil((loginRateLimitedUntil - Date.now()) / 1000));
      setLoginRateLimitRemaining(remaining);

      if (remaining <= 0) {
        setLoginRateLimitedUntil(null);
      }
    };

    updateRemaining();
    const intervalId = window.setInterval(updateRemaining, 1000);
    return () => window.clearInterval(intervalId);
  }, [loginRateLimitedUntil]);

  const rememberRecentEmail = useCallback((rawEmail: string) => {
    const normalized = rawEmail.trim().toLowerCase();
    if (!normalized) return;

    setRecentEmails(prev => {
      const next = [normalized, ...prev.filter(item => item !== normalized)].slice(0, LOGIN_RECENT_EMAILS_MAX);
      localStorage.setItem(LOGIN_RECENT_EMAILS_KEY, JSON.stringify(next));
      return next;
    });
  }, []);

  // Handle expired session
  useEffect(() => {
    const onUnauthorized = () => {
      setSessionState(null);
      showToast(`Sessão expirada. Faça login novamente.`, "warn");
    };
    window.addEventListener("smartcollect:unauthorized", onUnauthorized);
    return () => window.removeEventListener("smartcollect:unauthorized", onUnauthorized);
  }, [showToast]);

  const navigate = useCallback((id: string) => setPage(id as PageId), []);

  const goImport = useCallback(() => {
    setPage("import");
    showToast(`${t("toast.importAreaOpened")}`, "info");
  }, [showToast]);

  const goOverdueTitles = useCallback(() => {
    if (session?.role === "Master" && !selectedTenantId) {
      showToast(`Selecione uma empresa para visualizar os títulos inadimplentes.`, "warn");
      return;
    }

    setTitlesPreset({ status: "Overdue", token: Date.now() });
    setPage("titles");
    showToast(`Exibindo todos os inadimplentes.`, "info");
  }, [session?.role, selectedTenantId, showToast]);

  const handleLogin = async (e: React.FormEvent) => {
    e.preventDefault();
    if (isLoginRateLimited) {
      showToast(`Muitas tentativas. Tente novamente em ${loginRetryAfterLabel}.`, "warn");
      return;
    }

    if (!email.trim() || !password) {
      showToast(`Informe e-mail e senha.`, "warn");
      return;
    }
    try {
      setAuthLoading(true);
      const emailValue = email.trim();
      const s = await login(emailValue, password);
      rememberRecentEmail(emailValue);
      setLoginRateLimitedUntil(null);
      setSessionState(s);
      showToast(`Bem-vindo, ${s.userName}!`, "success");
    } catch (err) {
      if (err instanceof ApiError && err.status === 429) {
        const retryAfterSeconds = err.retryAfterSeconds ?? 300;
        setLoginRateLimitRemaining(retryAfterSeconds);
        setLoginRateLimitedUntil(Date.now() + retryAfterSeconds * 1000);
        showToast(`Muitas tentativas. Tente novamente em ${formatLoginRetryAfter(retryAfterSeconds)}.`, "error");
        return;
      }

      const msg = err instanceof ApiError ? err.message : "Falha ao autenticar.";
      showToast(`${msg}`, "error");
    } finally {
      setAuthLoading(false);
    }
  };

  const handleLogout = () => {
    setSession(null);
    setSessionState(null);
    setTenants([]);
    setSelectedTenantId("");
    localStorage.removeItem(LAST_PAGE_KEY);
    localStorage.removeItem(MASTER_SELECTED_TENANT_KEY);
    setPage("dashboard");
    setEmail("");
    setPassword("");
    showToast(`Sessão encerrada.`, "info");
  };

  useEffect(() => {
    localStorage.setItem(LAST_PAGE_KEY, page);
  }, [page]);

  useEffect(() => {
    if (session?.role === "Master")
      localStorage.setItem(MASTER_SELECTED_TENANT_KEY, selectedTenantId);
  }, [session?.role, selectedTenantId]);

  useEffect(() => {
    if (!session || session.role !== "Master") return;

    let cancelled = false;
    const loadTenants = async () => {
      try {
        const items = await getTenants();
        if (!cancelled) {
          setTenants(items);
          setTenantsLoaded(true);
        }
      } catch (err) {
        const msg = err instanceof ApiError ? err.message : "Falha ao carregar empresas.";
        if (!cancelled) {
          showToast(`${msg}`, "error");
          setTenantsLoaded(true);
        }
      }
    };

    void loadTenants();
    return () => { cancelled = true; };
  }, [session, showToast]);

  useEffect(() => {
    if (session?.role !== "Master") {
      setSelectedTenantId("");
      localStorage.removeItem(MASTER_SELECTED_TENANT_KEY);
      return;
    }

    if (!selectedTenantId) return;
    if (!tenantsLoaded) return;
    if (tenants.some(tenant => tenant.id === selectedTenantId)) return;

    setSelectedTenantId("");
  }, [session?.role, selectedTenantId, tenants, tenantsLoaded]);

  useEffect(() => {
    if (!session) return;

    const masterBasePages: PageId[] = ["dashboard", "analytics", "tenants"];
    const adminOperationalPages: PageId[] = ["titles", "import", "contacts", "sequence", "templates", "integration", "workers"];

    const allowedByRole: Record<string, PageId[]> = {
      Master: selectedTenantId ? [...masterBasePages, ...adminOperationalPages] : masterBasePages,
      Admin: ["dashboard", "analytics", "titles", "import", "contacts", "sequence", "templates", "integration", "workers"],
      Worker: ["dashboard", "analytics", "titles", "import", "contacts", "sequence", "templates"],
    };

    const allowed = allowedByRole[session.role] ?? ["dashboard"];
    if (!allowed.includes(page)) setPage("dashboard");
  }, [session, page, selectedTenantId]);

  // ── Login screen ─────────────────────────────────────────────────────────
  if (!session) {
    return (
      <div className="min-h-screen bg-surface flex items-center justify-center p-6">
        <div className="w-full max-w-[440px]">
          {/* Card */}
          <div className="bg-white dark:bg-surface-2 rounded-2xl border border-border-subtle px-8 py-10 shadow-[0_20px_60px_rgba(0,0,0,0.10)]">

            {/* Logo + brand */}
            <div className="flex flex-col items-center mb-8">
              <img
                src="/atos-logo.png"
                alt="Atos Capital"
                className="h-14 w-auto object-contain"
                style={isDark
                  ? {
                      filter: "brightness(0) saturate(100%) invert(20%) sepia(90%) saturate(4020%) hue-rotate(353deg) brightness(92%) contrast(91%)",
                    }
                  : undefined}
                onError={e => { (e.currentTarget as HTMLImageElement).style.display = "none"; }}
              />
            </div>

            <form onSubmit={handleLogin} autoComplete="off" data-form-type="other">
              {/* Decoy fields to absorb aggressive browser/password-manager autofill heuristics */}
              <input
                type="text"
                name="username"
                autoComplete="username"
                tabIndex={-1}
                aria-hidden="true"
                className="absolute opacity-0 pointer-events-none h-0 w-0"
              />
              <input
                type="password"
                name="password"
                autoComplete="current-password"
                tabIndex={-1}
                aria-hidden="true"
                className="absolute opacity-0 pointer-events-none h-0 w-0"
              />

              {/* Email */}
              <div className="mb-4">
                <label className="block text-xs font-bold uppercase text-text-muted tracking-wider mb-1.5">
                  E-mail
                </label>
                <input
                  type="email"
                  id={`sc-email-${loginFieldNonce}`}
                  name={`sc-email-${loginFieldNonce}`}
                  className="login-email-input w-full rounded-lg border border-border-subtle-2 bg-surface px-3.5 py-2.5 text-[13px] text-text-primary outline-none transition-[border-color] focus:border-accent focus:ring-2 focus:ring-accent/10"
                  value={email}
                  onChange={e => setEmail(e.target.value)}
                  onFocus={e => e.currentTarget.removeAttribute("readonly")}
                  readOnly
                  placeholder="usuario@empresa.com.br"
                  list="smartcollect-login-emails"
                  autoComplete="off"
                />
                {recentEmails.length > 0 && (
                  <datalist id="smartcollect-login-emails">
                    {recentEmails.map(item => (
                      <option key={item} value={item} />
                    ))}
                  </datalist>
                )}
              </div>

              {/* Password */}
              <div className="mb-6">
                <label className="block text-xs font-bold uppercase text-text-muted tracking-wider mb-1.5">
                  Senha
                </label>
                <div className="relative">
                  <input
                    type={showPassword ? "text" : "password"}
                    id={`sc-password-${loginFieldNonce}`}
                    name={`sc-password-${loginFieldNonce}`}
                    value={password}
                    onChange={e => setPassword(e.target.value)}
                    onFocus={e => e.currentTarget.removeAttribute("readonly")}
                    readOnly
                    placeholder="••••••••"
                    autoComplete="off"
                    data-lpignore="true"
                    data-1p-ignore="true"
                    data-bwignore="true"
                    data-protonpass-ignore="true"
                    className="w-full rounded-lg border border-border-subtle-2 bg-surface px-3.5 py-2.5 text-[13px] text-text-primary outline-none transition-[border-color] focus:border-accent focus:ring-2 focus:ring-accent/10 pr-10"
                  />
                  <button
                    type="button"
                    tabIndex={-1}
                    onClick={() => setShowPassword(v => !v)}
                    className="absolute right-3 top-1/2 -translate-y-1/2 text-text-muted"
                    aria-label={showPassword ? "Ocultar senha" : "Exibir senha"}
                  >
                    {showPassword ? (
                      <svg width="18" height="18" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg" aria-hidden="true">
                        <path d="M3 3L21 21" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" />
                        <path d="M10.58 10.58C10.21 10.95 10 11.46 10 12C10 13.1 10.9 14 12 14C12.54 14 13.05 13.79 13.42 13.42" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" />
                        <path d="M9.88 5.09C10.56 4.92 11.27 4.83 12 4.83C16.58 4.83 20.41 8.18 21.17 12C20.9 13.38 20.2 14.64 19.17 15.6" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" />
                        <path d="M6.22 6.23C4.51 7.46 3.28 9.57 2.83 12C3.59 15.82 7.42 19.17 12 19.17C13.57 19.17 15.03 18.78 16.3 18.1" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" />
                      </svg>
                    ) : (
                      <svg width="18" height="18" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg" aria-hidden="true">
                        <path d="M2.83 12C3.59 8.18 7.42 4.83 12 4.83C16.58 4.83 20.41 8.18 21.17 12C20.41 15.82 16.58 19.17 12 19.17C7.42 19.17 3.59 15.82 2.83 12Z" stroke="currentColor" strokeWidth="1.8" />
                        <circle cx="12" cy="12" r="3" stroke="currentColor" strokeWidth="1.8" />
                      </svg>
                    )}
                  </button>
                </div>
              </div>

              {isLoginRateLimited && (
                <div
                  role="status"
                  className="mb-4 rounded-lg border border-danger/25 bg-danger/10 px-3.5 py-2.5 text-[12px] font-medium text-danger"
                >
                  Muitas tentativas. Tente novamente em <strong>{loginRetryAfterLabel}</strong>.
                </div>
              )}

              <button
                type="submit"
                disabled={authLoading || isLoginRateLimited}
                className="w-full rounded-lg bg-accent text-white font-bold py-2.5 text-[14px] hover:bg-[#B91C1C] transition-colors disabled:opacity-60 disabled:cursor-not-allowed"
              >
                {authLoading ? "Entrando..." : isLoginRateLimited ? `Aguarde ${loginRetryAfterLabel}` : "Entrar"}
              </button>
            </form>

            <p className="text-center text-xs text-text-muted mt-6">
              Problema com o acesso? Contate o administrador.
            </p>
          </div>

          {/* Footer */}
          <p className="text-center text-[11px] text-text-muted mt-4">
            © {new Date().getFullYear()} Atos Capital · SmartCollect v1.0
          </p>
        </div>

        <Toast message={toast.message} type={toast.type} visible={toast.visible} />
      </div>
    );
  }

  // ── Main app ──────────────────────────────────────────────────────────────
  const pages: Record<PageId, React.ReactNode> = {
    dashboard:   <PageDashboard showToast={showToast} session={session} selectedTenantId={selectedTenantId || undefined} onViewAllDefaulters={goOverdueTitles} />,
    analytics:   <PageAnalytics showToast={showToast} session={session} selectedTenantId={selectedTenantId || undefined} isDark={isDark} />,
    titles:      <PageTitles showToast={showToast} session={session} selectedTenantId={selectedTenantId || undefined} presetStatusFilter={titlesPreset?.status} presetFilterToken={titlesPreset?.token} />,
    import:      <PageImport showToast={showToast} session={session} selectedTenantId={selectedTenantId || undefined} />,
    contacts:    <PageContacts showToast={showToast} session={session} selectedTenantId={selectedTenantId || undefined} onSubtitleChange={setContactsSubtitle} />,
    sequence:    <PageSequence showToast={showToast} session={session} selectedTenantId={selectedTenantId || undefined} />,
    templates:   <PageTemplates showToast={showToast} session={session} selectedTenantId={selectedTenantId || undefined} />,
    integration: <PageIntegration showToast={showToast} session={session} selectedTenantId={selectedTenantId || undefined} />,
    workers:     <PageWorkers showToast={showToast} session={session} selectedTenantId={selectedTenantId || undefined} />,
    tenants:     <PageTenants showToast={showToast} />,
  };

  return (
    <div className="flex min-h-screen bg-surface">
      <Sidebar
        active={page}
        onNav={navigate}
        isDark={isDark}
        toggleTheme={toggleTheme}
        session={session}
        selectedTenantId={selectedTenantId}
        showToast={showToast}
        onSessionUpdate={setSessionState}
        onLogout={handleLogout}
      />

      <div className="ml-[248px] flex-1 flex flex-col min-h-screen">
        <Topbar
          page={page}
          onImport={goImport}
          showToast={showToast}
          session={session}
          tenants={tenants}
          selectedTenantId={selectedTenantId}
          onSelectTenant={setSelectedTenantId}
          toastLogs={toastLogs}
          unreadToastCount={unreadToastCount}
          onMarkToastLogsRead={markAllToastRead}
          onClearToastLogs={clearToastLogs}
          contactsSubtitle={contactsSubtitle}
        />

        <div className="p-7 pt-3 flex-1 text-text-primary">
          <Suspense fallback={<LoadingState label="Carregando módulo..." />}>
            {pages[page]}
          </Suspense>
        </div>
      </div>

      <Toast message={toast.message} type={toast.type} visible={toast.visible} />
    </div>
  );
}
