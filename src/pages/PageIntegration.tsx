import { useEffect, useState } from "react";
import { CardHeader, Button, FormInput, FormSelect } from "../components/UI";
import { ICONS } from "../utils/icons";
import { colors } from "../utils/colors";
import { t } from "../i18n";
import {
  ApiError, getSmtpConfig, saveSmtpConfig, testSmtpConfig,
  getWhatsAppConfig, saveWhatsAppConfig, testWhatsAppConfig,
  getSyncHealth,
  getExternalApiConfig,
  saveExternalApiConfig,
  testExternalApiConfig,
  getDispatchWindowConfig,
  saveDispatchWindowConfig,
  type SmtpConfigResponse, type StoredSession, type SyncHealthResponse,
  type WhatsAppProvider,
} from "../services/api";
import type { ShowToast } from "../types";

const normalizeTwilioSender = (raw: string) => {
  const text = raw.trim().replace(/^whatsapp:/i, "");
  if (!text) return "";

  const hasPlus = text.startsWith("+");
  let digits = text.replace(/\D/g, "");
  if (!digits) return "";
  if (digits.startsWith("00")) digits = digits.slice(2);

  if (hasPlus) return `+${digits}`;
  if (digits.length === 10 || digits.length === 11) return `+55${digits}`;
  if (digits.length === 11 && digits.startsWith("1")) return `+${digits}`;
  return `+${digits}`;
};

const normalizeTwilioAccountSid = (raw: string) => {
  const text = raw.trim();
  if (!text) return "";
  if (text.toUpperCase().startsWith("AC")) return text;

  const match = text.match(/\/Accounts\/(AC[a-zA-Z0-9]+)/i);
  if (match?.[1]) return match[1];

  return text;
};

const getBrowserTimeZone = () => {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC";
  } catch {
    return "UTC";
  }
};

export const PageIntegration = ({
  showToast,
  session,
  selectedTenantId,
}: {
  showToast: ShowToast;
  session: StoredSession;
  selectedTenantId?: string;
}) => {
  const [smtp, setSmtp] = useState<SmtpConfigResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [testing, setTesting] = useState(false);
  const [isConfigured, setIsConfigured] = useState(false);
  const [syncHealth, setSyncHealth] = useState<SyncHealthResponse | null>(null);
  const [syncHealthLoading, setSyncHealthLoading] = useState(true);

  const [waLoading, setWaLoading] = useState(true);
  const [waSaving, setWaSaving] = useState(false);
  const [waTesting, setWaTesting] = useState(false);
  const [waConfigured, setWaConfigured] = useState(false);
  const [waProvider, setWaProvider] = useState<WhatsAppProvider>("Twilio");
  const [waNumberId, setWaNumberId] = useState("");
  const [waToken, setWaToken] = useState("");
  const [waApiBaseUrl, setWaApiBaseUrl] = useState("");
  const [waWebhookUrl, setWaWebhookUrl] = useState("https://smartcollect.app/webhook/whatsapp");
  const [waClearToken, setWaClearToken] = useState(false);

  const [apiLoading, setApiLoading] = useState(true);
  const [apiSaving, setApiSaving] = useState(false);
  const [apiTesting, setApiTesting] = useState(false);
  const [apiHasToken, setApiHasToken] = useState(false);
  const [apiBaseUrl, setApiBaseUrl] = useState("");
  const [apiDocsUrl, setApiDocsUrl] = useState("");
  const [apiPendingPath, setApiPendingPath] = useState("reguacobranca?colecao=1&pageSize=0&pageNumber=0");
  const [apiOccurrencesPath, setApiOccurrencesPath] = useState("reguacobranca?colecao=2&dtOcorrencia={date}&pageSize=0&pageNumber=0");
  const [apiAuthScheme, setApiAuthScheme] = useState("Bearer");
  const [apiToken, setApiToken] = useState("");
  const [clearApiToken, setClearApiToken] = useState(false);

  const [windowLoading, setWindowLoading] = useState(true);
  const [windowSaving, setWindowSaving] = useState(false);
  const [dispatchWindowEnabled, setDispatchWindowEnabled] = useState(false);
  const [dispatchWindowStartTime, setDispatchWindowStartTime] = useState("09:00");
  const [dispatchWindowEndTime, setDispatchWindowEndTime] = useState("18:00");

  // Form state
  const [host, setHost] = useState("");
  const [port, setPort] = useState("587");
  const [user, setUser] = useState("");
  const [password, setPassword] = useState("");
  const [showSmtpPassword, setShowSmtpPassword] = useState(false);
  const [smtpSecurity, setSmtpSecurity] = useState<"TLS" | "SSL">("TLS");
  const [senderFrom, setSenderFrom] = useState("");
  const [senderName, setSenderName] = useState("");

  const tenantId = session.role === "Master" ? selectedTenantId : undefined;
  const requiresTenantSelection = session.role === "Master" && !tenantId;
  const canEdit = session.role === "Admin" || (session.role === "Master" && Boolean(tenantId));
  const canEditDispatchWindow = canEdit;

  useEffect(() => {
    const load = async () => {
      if (requiresTenantSelection) {
        setSmtp(null);
        setIsConfigured(false);
        setLoading(false);
        return;
      }

      try {
        const data = await getSmtpConfig(tenantId);
        setSmtp(data);
        setHost(data.host);
        setPort(String(data.port));
        setSmtpSecurity(data.port === 465 ? "SSL" : "TLS");
        setUser(data.user);
        setSenderFrom(data.senderFrom);
        setSenderName(data.senderName);
        setIsConfigured(true);
      } catch (err) {
        if (err instanceof ApiError && err.status === 404) {
          setIsConfigured(false);
        } else {
          showToast(`${t("integrationPage.errors.loadSmtp")}`, "error");
        }
      } finally {
        setLoading(false);
      }
    };
    void load();
  }, [showToast, tenantId, requiresTenantSelection]);

  const loadWhatsAppConfig = async () => {
    if (requiresTenantSelection) {
      setWaConfigured(false);
      setWaLoading(false);
      return;
    }

    try {
      setWaLoading(true);
      const data = await getWhatsAppConfig(tenantId);
      setWaProvider(data.provider);
      setWaNumberId(data.numberId);
      setWaApiBaseUrl(data.apiBaseUrl ?? "");
      setWaWebhookUrl(data.webhookUrl);
      setWaConfigured(data.hasAccessToken);
      setWaToken("");
      setWaClearToken(false);
    } catch (err) {
      if (err instanceof ApiError && err.status === 404) {
        setWaConfigured(false);
      } else {
        showToast(`${t("integrationPage.errors.loadWhatsApp")}`, "error");
      }
    } finally {
      setWaLoading(false);
    }
  };

  useEffect(() => {
    void loadWhatsAppConfig();
  }, [showToast, tenantId, requiresTenantSelection]);

  const loadSyncHealth = async () => {
    if (requiresTenantSelection) {
      setSyncHealth(null);
      setSyncHealthLoading(false);
      return;
    }

    try {
      setSyncHealthLoading(true);
      const data = await getSyncHealth(tenantId);
      setSyncHealth(data);
    } catch (err) {
      if (!(err instanceof ApiError && err.status === 403)) {
        showToast(`${t("integrationPage.errors.validateExternal")}`, "error");
      }
      setSyncHealth(null);
    } finally {
      setSyncHealthLoading(false);
    }
  };

  useEffect(() => {
    void loadSyncHealth();
  }, [showToast, tenantId, requiresTenantSelection]);

  const loadApiConfig = async () => {
    if (requiresTenantSelection) {
      setApiLoading(false);
      setApiHasToken(false);
      return;
    }

    try {
      setApiLoading(true);
      const data = await getExternalApiConfig(tenantId);
      setApiBaseUrl(data.baseUrl ?? "");
      setApiDocsUrl(data.docsUrl ?? "");
      setApiPendingPath(data.pendingTitlesPath ?? "reguacobranca?colecao=1&pageSize=0&pageNumber=0");
      setApiOccurrencesPath(data.occurrencesPath ?? "reguacobranca?colecao=2&dtOcorrencia={date}&pageSize=0&pageNumber=0");
      setApiAuthScheme(data.authenticationScheme ?? "Bearer");
      setApiHasToken(Boolean(data.hasToken));
      setClearApiToken(false);
      setApiToken("");
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : t("integrationPage.errors.loadApiConfig");
      showToast(`${msg}`, "error");
    } finally {
      setApiLoading(false);
    }
  };

  useEffect(() => {
    void loadApiConfig();
  }, [showToast, tenantId, requiresTenantSelection]);

  const loadDispatchWindowConfig = async () => {
    if (requiresTenantSelection) {
      setWindowLoading(false);
      setDispatchWindowEnabled(false);
      setDispatchWindowStartTime("09:00");
      setDispatchWindowEndTime("18:00");
      return;
    }

    try {
      setWindowLoading(true);
      const data = await getDispatchWindowConfig(tenantId);
      setDispatchWindowEnabled(data.enabled);
      setDispatchWindowStartTime(data.startTime || "09:00");
      setDispatchWindowEndTime(data.endTime || "18:00");
    } catch (err) {
      if (err instanceof ApiError && err.status === 404) {
        // Backend antigo ou rota ainda não publicada: mantém defaults locais sem exibir erro.
        setDispatchWindowEnabled(false);
        setDispatchWindowStartTime("09:00");
        setDispatchWindowEndTime("18:00");
      } else {
        const msg = err instanceof ApiError ? err.message : t("integrationPage.errors.loadDispatchWindow");
        showToast(`${msg}`, "error");
      }
    } finally {
      setWindowLoading(false);
    }
  };

  useEffect(() => {
    void loadDispatchWindowConfig();
  }, [showToast, tenantId, requiresTenantSelection]);

  const handleSave = async () => {
    if (!host || !port || !user) {
      showToast(`${t("integrationPage.validation.smtpRequired")}`, "warn");
      return;
    }
    try {
      setSaving(true);
      await saveSmtpConfig({
        host, port: parseInt(port, 10),
        user, password: password || undefined,
        senderFrom, senderName,
      }, tenantId);
      setIsConfigured(true);
      setPassword(""); // Clear password after save for security
      showToast(`${t("toast.smtpSaved")}`, "success");
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : t("integrationPage.errors.saveSmtp");
      showToast(`${msg}`, "error");
    } finally {
      setSaving(false);
    }
  };

  const getWaNumberLabel = () => {
    if (waProvider === "Twilio") return t("integrationPage.wa.numberLabelTwilio");
    if (waProvider === "Z-API") return t("integrationPage.wa.numberLabelZApi");
    if (waProvider === "Evolution API") return t("integrationPage.wa.numberLabelEvolution");
    return t("integrationPage.wa.numberLabelDefault");
  };

  const getWaBaseLabel = () => {
    if (waProvider === "Twilio") return t("integrationPage.wa.baseLabelTwilio");
    if (waProvider === "Evolution API") return t("integrationPage.wa.baseLabelEvolution");
    return t("integration.apiBaseUrlOptional");
  };

  const getWaBasePlaceholder = () => {
    if (waProvider === "Twilio") return t("integrationPage.wa.basePlaceholderTwilio");
    if (waProvider === "Evolution API") return t("integrationPage.wa.basePlaceholderEvolution");
    return t("integrationPage.wa.basePlaceholderDefault");
  };

  const getWaTokenLabel = () => {
    if (waProvider === "Twilio") return waConfigured
      ? t("integrationPage.wa.tokenLabelTwilioKeep")
      : t("integrationPage.wa.tokenLabelTwilio");
    if (waProvider === "360dialog") return waConfigured
      ? t("integrationPage.wa.tokenLabel360Keep")
      : t("integrationPage.wa.tokenLabel360");
    return waConfigured
      ? t("integrationPage.wa.tokenLabelKeep")
      : t("integrationPage.wa.tokenLabel");
  };

  const getWaNumberPlaceholder = () => {
    if (waProvider === "Twilio") return t("integrationPage.wa.numberPlaceholderTwilio");
    if (waProvider === "Z-API") return t("integrationPage.wa.numberPlaceholderZApi");
    if (waProvider === "Evolution API") return t("integrationPage.wa.numberPlaceholderEvolution");
    return t("integrationPage.wa.numberPlaceholderDefault");
  };

  const handleSaveWhatsApp = async () => {
    const normalizedNumberId = waProvider === "Twilio"
      ? normalizeTwilioSender(waNumberId)
      : waNumberId.trim();

    const normalizedApiBaseUrl = waProvider === "Twilio"
      ? normalizeTwilioAccountSid(waApiBaseUrl)
      : waApiBaseUrl.trim();

    if (!normalizedNumberId) {
      showToast(`${getWaNumberLabel()} ${t("integrationPage.validation.required")}`, "warn");
      return;
    }

    if (waProvider === "Twilio" && !normalizedApiBaseUrl) {
      showToast(`${t("integrationPage.validation.waTwilioAccountRequired")}`, "warn");
      return;
    }

    if (waProvider === "Evolution API" && !normalizedApiBaseUrl) {
      showToast(`${t("integrationPage.validation.waEvolutionBaseRequired")}`, "warn");
      return;
    }

    try {
      setWaSaving(true);
      await saveWhatsAppConfig({
        provider: waProvider,
        numberId: normalizedNumberId,
        accessToken: waToken.trim() || undefined,
        apiBaseUrl: normalizedApiBaseUrl || undefined,
        clearToken: waClearToken,
      }, tenantId);
      setWaConfigured(waClearToken ? false : (waToken.trim() ? true : waConfigured));
      setWaToken("");
      setWaClearToken(false);
      showToast(`${t("integrationPage.messages.waSaved")}`, "success");
      await loadWhatsAppConfig();
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : t("integrationPage.errors.saveWhatsApp");
      showToast(`${msg}`, "error");
    } finally {
      setWaSaving(false);
    }
  };

  const handleTestWhatsApp = async () => {
    try {
      setWaTesting(true);
      const res = await testWhatsAppConfig(tenantId);
      showToast(`${res.message}`, "success");
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : t("integrationPage.errors.testWhatsApp");
      showToast(`${msg}`, "error");
    } finally {
      setWaTesting(false);
    }
  };

  const handleTest = async () => {
    if (!isConfigured) {
      showToast(`${t("integrationPage.validation.smtpTestBeforeSave")}`, "warn");
      return;
    }
    try {
      setTesting(true);
      const res = await testSmtpConfig(tenantId);
      showToast(`${res.message}`, "success");
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : t("integrationPage.errors.testSmtp");
      showToast(`${msg}`, "error");
    } finally {
      setTesting(false);
    }
  };

  const handleSaveExternalApi = async () => {
    if (!apiBaseUrl.trim() || !apiPendingPath.trim() || !apiOccurrencesPath.trim()) {
      showToast(`${t("integrationPage.validation.apiRequired")}`, "warn");
      return;
    }

    try {
      setApiSaving(true);
      await saveExternalApiConfig({
        baseUrl: apiBaseUrl.trim(),
        docsUrl: apiDocsUrl.trim() || undefined,
        pendingTitlesPath: apiPendingPath.trim(),
        occurrencesPath: apiOccurrencesPath.trim(),
        authenticationScheme: apiAuthScheme,
        token: apiToken.trim() || undefined,
        clearToken: clearApiToken,
      }, tenantId);

      setApiHasToken(clearApiToken ? false : (apiToken.trim() ? true : apiHasToken));
      setApiToken("");
      setClearApiToken(false);
      showToast(`${t("integrationPage.messages.apiSaved")}`, "success");
      await loadSyncHealth();
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : t("integrationPage.errors.saveApiConfig");
      showToast(`${msg}`, "error");
    } finally {
      setApiSaving(false);
    }
  };

  const handleTestExternalApi = async () => {
    try {
      setApiTesting(true);
      const res = await testExternalApiConfig(tenantId);
      showToast(`${res.message}`, "success");
      await loadSyncHealth();
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : t("integrationPage.errors.testExternalApi");
      showToast(`${msg}`, "error");
      await loadSyncHealth();
    } finally {
      setApiTesting(false);
    }
  };

  const handleSaveDispatchWindow = async () => {
    if (!dispatchWindowStartTime.trim() || !dispatchWindowEndTime.trim()) {
      showToast(`${t("integrationPage.validation.windowRequired")}`, "warn");
      return;
    }

    try {
      setWindowSaving(true);
      const response = await saveDispatchWindowConfig({
        enabled: dispatchWindowEnabled,
        timeZone: getBrowserTimeZone(),
        startTime: dispatchWindowStartTime.trim(),
        endTime: dispatchWindowEndTime.trim(),
        pauseAutomaticDispatchDuringProcessing: true,
      }, tenantId);
      showToast(`${response.message}`, "success");
      await loadDispatchWindowConfig();
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : t("integrationPage.errors.saveDispatchWindow");
      showToast(`${msg}`, "error");
    } finally {
      setWindowSaving(false);
    }
  };

  return (
    <div className="animate-fade-up">
      {requiresTenantSelection && (
        <div className="rounded-xl border border-border-subtle bg-surface-2/60 px-4 py-3 text-sm text-text-secondary mb-4">
          {t("integrationPage.validation.selectTenant")}
        </div>
      )}

      <div className="grid grid-cols-2 gap-4 mb-4">
        {/* SMTP */}
        <div className="bg-surface border border-border-subtle rounded-[14px] overflow-hidden">
          <CardHeader
            title={<>{ICONS.email} {t("integration.smtpTitle")}</>}
            subtitle={t("integration.smtpSubtitle")}
            right={
              loading ? null : (
                <span className={`text-[11px] font-bold px-[9px] py-[3px] rounded-full ${
                  isConfigured ? "bg-success/[0.12] text-success" : "bg-warn/[0.12] text-warn"
                }`}>
                  {isConfigured ? t("common.configured") : t("common.pending")}
                </span>
              )
            }
          />
          <div className="p-5">
            {loading ? (
              <div className="text-sm text-text-muted py-4">{t("common.loading")}</div>
            ) : (
              <>
                <div className="grid grid-cols-2 gap-3.5">
                  <FormInput label={t("integration.smtpHost")} value={host} onChange={e => setHost(e.target.value)} disabled={!canEdit} placeholder="mail.empresa.com.br" />
                  <div>
                    <label className="text-[11px] font-bold tracking-[0.6px] uppercase text-text-muted mb-[5px] block">{t("integrationPage.smtp.securityLabel")}</label>
                    <select
                      value={smtpSecurity}
                      disabled={!canEdit}
                      onChange={e => {
                        const mode = e.target.value as "TLS" | "SSL";
                        setSmtpSecurity(mode);
                        setPort(mode === "SSL" ? "465" : "587");
                      }}
                      className="bg-surface-2 border border-border-subtle-2 rounded-lg px-[13px] py-[9px] text-[13px] text-text-primary outline-none w-full focus:border-accent disabled:opacity-70"
                    >
                      <option value="TLS">{t("integrationPage.smtp.securityTls")}</option>
                      <option value="SSL">{t("integrationPage.smtp.securitySsl")}</option>
                    </select>
                  </div>
                  <FormInput label={t("integration.port")} value={port} onChange={e => setPort(e.target.value)} disabled={!canEdit} placeholder={smtpSecurity === "SSL" ? "465" : "587"} type="number" />
                  <FormInput label={t("integration.user")} value={user} onChange={e => setUser(e.target.value)} disabled={!canEdit} placeholder="financeiro@empresa.com.br" />
                  <div>
                    <label className="text-[11px] font-bold tracking-[0.6px] uppercase text-text-muted mb-[5px] block">{t("integration.password")}</label>
                    <div className="relative">
                      <input
                        type={showSmtpPassword ? "text" : "password"}
                        value={password}
                        onChange={e => setPassword(e.target.value)}
                        disabled={!canEdit}
                        placeholder={isConfigured ? "••••••••" : t("integrationPage.smtp.passwordPlaceholder")}
                        className="bg-surface-2 border border-border-subtle-2 rounded-lg px-[13px] py-[9px] text-[13px] text-text-primary outline-none w-full focus:border-accent disabled:opacity-70 pr-10"
                      />
                      <button
                        type="button"
                        onClick={() => setShowSmtpPassword(v => !v)}
                        className="absolute right-3 top-1/2 -translate-y-1/2 text-text-muted"
                        aria-label={showSmtpPassword ? t("integrationPage.smtp.passwordHide") : t("integrationPage.smtp.passwordShow")}
                      >
                        {showSmtpPassword ? (
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
                  <FormInput label={t("integration.senderFrom")} value={senderFrom} onChange={e => setSenderFrom(e.target.value)} disabled={!canEdit} placeholder="financeiro@empresa.com.br" />
                  <FormInput label={t("integration.senderName")} value={senderName} onChange={e => setSenderName(e.target.value)} disabled={!canEdit} placeholder="Financeiro – Empresa" />
                </div>

                {senderFrom && (
                  <div className="mt-3 text-xs text-text-muted bg-surface-2 rounded-lg px-3 py-2 border border-border-subtle">
                    {ICONS.email} {t("integrationPage.smtp.senderLabel")} <strong className="text-text-primary">{senderName || t("integrationPage.smtp.senderFallback")} &lt;{senderFrom}&gt;</strong>
                  </div>
                )}

                {canEdit && (
                  <div className="mt-4 flex gap-2.5">
                    <Button size="sm" variant="secondary" onClick={handleTest}>
                      {testing ? t("integrationPage.smtp.testing") : <>{ICONS.mailbox} {t("integration.testSend")}</>}
                    </Button>
                    <Button size="sm" variant="primary" onClick={handleSave}>
                      {saving ? t("common.saving") : t("common.save")}
                    </Button>
                  </div>
                )}
              </>
            )}
          </div>
        </div>

        {/* WhatsApp */}
        <div className="bg-surface border border-border-subtle rounded-[14px] overflow-hidden">
          <CardHeader
            title={<>{ICONS.chat} {t("integration.whatsappTitle")}</>}
            subtitle={t("integration.whatsappSubtitle")}
            right={
              waLoading ? null : (
                <span className={`text-[11px] font-bold px-[9px] py-[3px] rounded-full ${
                  waConfigured ? "bg-success/[0.12] text-success" : "bg-warn/[0.12] text-warn"
                }`}>
                  {waConfigured ? t("common.configured") : t("common.pending")}
                </span>
              )
            }
          />
          <div className="p-5">
            {waLoading ? (
              <div className="text-sm text-text-muted py-4">{t("common.loading")}</div>
            ) : (
              <>
                <div className="grid grid-cols-2 gap-3.5">
                  <div className="col-span-2">
                    <FormSelect
                      label={t("integration.selectProvider")}
                      value={waProvider}
                      onChange={e => setWaProvider(e.target.value as WhatsAppProvider)}
                      disabled={!canEdit}
                    >
                      <option value="Twilio">Twilio</option>
                      <option value="Z-API">Z-API</option>
                      <option value="Evolution API">Evolution API</option>
                      <option value="360dialog">360dialog</option>
                    </FormSelect>
                  </div>
                  <FormInput
                    label={getWaNumberLabel()}
                    value={waNumberId}
                    onChange={e => setWaNumberId(e.target.value)}
                    placeholder={getWaNumberPlaceholder()}
                    disabled={!canEdit}
                  />
                  <FormInput
                    label={getWaTokenLabel()}
                    type="password"
                    value={waToken}
                    onChange={e => setWaToken(e.target.value)}
                    placeholder={waConfigured ? "••••••••" : t("integrationPage.wa.tokenPlaceholder")}
                    disabled={!canEdit}
                  />
                  <FormInput
                    label={getWaBaseLabel()}
                    value={waApiBaseUrl}
                    onChange={e => setWaApiBaseUrl(e.target.value)}
                    placeholder={getWaBasePlaceholder()}
                    disabled={!canEdit}
                  />
                  <div className="col-span-2">
                    <label className="text-[11px] font-bold tracking-[0.6px] uppercase text-text-muted mb-[5px] block">{t("integration.webhookUrl")}</label>
                    <input
                      readOnly
                      value={waWebhookUrl}
                      className="bg-surface-2 border border-white/[0.11] rounded-lg px-[13px] py-[9px] text-[13px] text-text-muted outline-none w-full"
                    />
                  </div>
                  {waConfigured && canEdit && (
                    <label className="col-span-2 flex items-center gap-2 text-xs text-text-muted">
                      <input
                        type="checkbox"
                        checked={waClearToken}
                        onChange={e => setWaClearToken(e.target.checked)}
                        className="accent-accent"
                      />
                      {t("integrationPage.wa.clearToken")}
                    </label>
                  )}
                </div>
                {canEdit && (
                  <div className="mt-4 flex gap-2.5">
                    <Button size="sm" variant="secondary" onClick={handleTestWhatsApp}>
                      {waTesting ? t("integrationPage.wa.testing") : <>{ICONS.checkmark} {t("integrationPage.wa.validate")}</>}
                    </Button>
                    <Button size="sm" variant="primary" onClick={handleSaveWhatsApp}>
                      {waSaving ? t("common.saving") : t("common.save")}
                    </Button>
                  </div>
                )}
              </>
            )}
          </div>
        </div>
      </div>

      {/* API externa */}
      <div className="bg-surface border border-border-subtle rounded-[14px] overflow-hidden">
        <CardHeader
          title={<>{ICONS.link} {t("integrationPage.api.title")}</>}
          subtitle={t("integrationPage.api.subtitle")}
          right={
            <span className={`${syncHealth?.connected ? "bg-success/[0.12] text-success" : "bg-danger/[0.12] text-danger"} text-[11px] font-bold px-[9px] py-[3px] rounded-full flex items-center gap-[5px]`}>
              <span className="text-[7px]">{ICONS.dot}</span>
              {syncHealthLoading ? t("integrationPage.api.checking") : syncHealth?.connected ? t("common.online") : t("integrationPage.api.offline")}
            </span>
          }
        />
        <div className="p-5">
          {apiLoading ? (
            <div className="text-sm text-text-muted py-4">{t("integrationPage.api.loading")}</div>
          ) : (
            <>
              <div className="grid grid-cols-2 gap-3.5 mb-4">
                <div className="col-span-2">
                  <FormInput
                    label={t("integrationPage.api.baseUrlLabel")}
                    value={apiBaseUrl}
                    onChange={e => setApiBaseUrl(e.target.value)}
                    disabled={!canEdit}
                    placeholder="https://api.parceiro.com.br/v1/"
                  />
                </div>
                <div className="col-span-2">
                  <FormInput
                    label={t("integrationPage.api.docsLabel")}
                    value={apiDocsUrl}
                    onChange={e => setApiDocsUrl(e.target.value)}
                    disabled={!canEdit}
                    placeholder="https://api.parceiro.com.br/swagger"
                  />
                </div>
                <FormInput
                  label={t("integrationPage.api.pendingEndpointLabel")}
                  value={apiPendingPath}
                  onChange={e => setApiPendingPath(e.target.value)}
                  disabled={!canEdit}
                  placeholder="reguacobranca?colecao=1&pageSize=0&pageNumber=0"
                />
                <FormInput
                  label={t("integrationPage.api.occurrencesEndpointLabel")}
                  value={apiOccurrencesPath}
                  onChange={e => setApiOccurrencesPath(e.target.value)}
                  disabled={!canEdit}
                  placeholder="reguacobranca?colecao=2&dtOcorrencia={date}&pageSize=0&pageNumber=0"
                />
                <FormSelect
                  label={t("integrationPage.api.authLabel")}
                  value={apiAuthScheme}
                  onChange={e => setApiAuthScheme(e.target.value)}
                  disabled={!canEdit}
                >
                  <option value="Bearer">Bearer</option>
                  <option value="None">{t("integrationPage.api.authNone")}</option>
                </FormSelect>
                <FormInput
                  label={apiHasToken ? t("integrationPage.api.tokenKeepLabel") : t("integrationPage.api.tokenLabel")}
                  type="password"
                  value={apiToken}
                  onChange={e => setApiToken(e.target.value)}
                  disabled={!canEdit}
                  placeholder={apiHasToken ? "••••••••••••" : t("integrationPage.api.tokenPlaceholder")}
                />
              </div>

              {canEdit && (
                <label className="flex items-center gap-2 text-xs text-text-secondary mb-4">
                  <input
                    type="checkbox"
                    checked={clearApiToken}
                    onChange={e => setClearApiToken(e.target.checked)}
                  />
                  {t("integrationPage.api.clearToken")}
                </label>
              )}

              <div className="bg-surface-2 rounded-[10px] px-[18px] py-4 font-mono text-[12.5px] leading-[2.2] border border-white/[0.06] mb-4">
                {([
                  [t("integrationPage.api.summaryBaseUrl"), syncHealth?.baseUrl ?? "N/A", colors.accent],
                  [t("integrationPage.api.summaryEndpoint1"), syncHealth?.endpoints.pendingTitles ?? "N/A", colors.text3],
                  [t("integrationPage.api.summaryEndpoint2"), syncHealth?.endpoints.occurrences ?? "N/A", colors.text3],
                  [t("integrationPage.api.summaryDocs"), syncHealth?.docsUrl ?? "N/A", colors.text2],
                  [t("integrationPage.api.summaryAuth"), syncHealth?.authentication ?? "N/A", colors.text2],
                ] as [string, string, string][]).map(([key, val, color]) => (
                  <div key={key}>
                    <span className="text-text-muted">{key}</span>{" "}
                    <span style={{ color }}>{val}</span>
                  </div>
                ))}
                <div>
                  <span className="text-text-muted">{t("integrationPage.api.summaryToken")}</span>{" "}
                  <span style={{ color: colors.text2 }}>{apiHasToken ? t("integrationPage.api.tokenConfigured") : t("integrationPage.api.tokenNotConfigured")}</span>
                </div>
                {syncHealth?.checkedAt && (
                  <div>
                    <span className="text-text-muted">{t("integrationPage.api.summaryLastCheck")}</span>{" "}
                    <span style={{ color: colors.text2 }}>{new Date(syncHealth.checkedAt).toLocaleString("pt-BR")}</span>
                  </div>
                )}
              </div>
            </>
          )}

          <div className="text-[12px] text-text-muted mb-4">
            {t("integrationPage.api.expectedFields")}
          </div>

          <div className="flex gap-2.5">
            {canEdit && (
              <Button
                size="sm"
                variant="primary"
                onClick={handleSaveExternalApi}
                disabled={apiSaving || apiLoading}
              >
                {apiSaving ? t("common.saving") : t("common.save")}
              </Button>
            )}
            <Button
              size="sm"
              variant="secondary"
              onClick={() => {
                if (!apiDocsUrl && !syncHealth?.docsUrl) {
                  showToast(`${t("integrationPage.api.docsUnavailable")}`, "warn");
                  return;
                }
                window.open(apiDocsUrl || syncHealth?.docsUrl, "_blank", "noopener,noreferrer");
              }}
            >
              {ICONS.document} {t("integration.viewDocs")}
            </Button>
            <Button
              size="sm"
              variant="secondary"
              onClick={handleTestExternalApi}
              disabled={apiTesting || apiLoading}
            >
              {apiTesting ? t("integrationPage.api.testing") : <>{ICONS.testTube} {t("integration.testEndpoint")}</>}
            </Button>
          </div>
        </div>
      </div>

      <div className="bg-surface border border-border-subtle rounded-[14px] overflow-hidden mt-4">
        <CardHeader
          title={<>{ICONS.timer} {t("integrationPage.window.title")}</>}
          subtitle={t("integrationPage.window.subtitle")}
          right={
            <span className={`${dispatchWindowEnabled ? "bg-success/[0.12] text-success" : "bg-warn/[0.12] text-warn"} text-[11px] font-bold px-[9px] py-[3px] rounded-full`}>
              {dispatchWindowEnabled ? t("integrationPage.window.active") : t("integrationPage.window.inactive")}
            </span>
          }
        />
        <div className="p-5">
          {windowLoading ? (
            <div className="text-sm text-text-muted py-4">{t("integrationPage.window.loading")}</div>
          ) : (
            <>
              <div className="mb-4 rounded-xl border border-border-subtle bg-surface-2 px-4 py-3 flex items-center justify-between gap-3">
                <div>
                  <div className="text-sm font-semibold text-text-primary">{t("integrationPage.window.enableTitle")}</div>
                  <div className="text-xs text-text-muted mt-0.5">{t("integrationPage.window.timezoneLabel")} {getBrowserTimeZone()}</div>
                </div>
                <button
                  type="button"
                  role="switch"
                  aria-checked={dispatchWindowEnabled}
                  disabled={!canEditDispatchWindow}
                  onClick={() => setDispatchWindowEnabled(prev => !prev)}
                  className={`inline-flex items-center gap-2 rounded-full border px-2 py-1 text-xs font-semibold transition-colors ${dispatchWindowEnabled ? "border-success/30 bg-success/12 text-success" : "border-border-subtle-2 bg-surface text-text-muted"} ${!canEditDispatchWindow ? "opacity-60 cursor-not-allowed" : "cursor-pointer"}`}
                >
                  <span className={`h-4 w-7 rounded-full p-[2px] transition-colors ${dispatchWindowEnabled ? "bg-success/75" : "bg-text-muted/40"}`}>
                    <span className={`block h-3 w-3 rounded-full bg-white transition-transform ${dispatchWindowEnabled ? "translate-x-3" : "translate-x-0"}`} />
                  </span>
                  <span>{dispatchWindowEnabled ? t("common.active") : t("common.inactive")}</span>
                </button>
              </div>

              <div className="grid grid-cols-2 gap-3.5 mb-4">
                <div>
                  <label className="text-[11px] font-bold tracking-[0.6px] uppercase text-text-muted mb-[5px] block">{t("integrationPage.window.startLabel")}</label>
                  <input
                    type="time"
                    step={300}
                    value={dispatchWindowStartTime}
                    onChange={e => setDispatchWindowStartTime(e.target.value)}
                    disabled={!canEditDispatchWindow}
                    className="bg-surface-2 border border-border-subtle-2 rounded-lg px-[13px] py-[9px] text-[15px] text-text-primary outline-none w-full focus:border-accent disabled:opacity-70"
                  />
                </div>

                <div>
                  <label className="text-[11px] font-bold tracking-[0.6px] uppercase text-text-muted mb-[5px] block">{t("integrationPage.window.endLabel")}</label>
                  <input
                    type="time"
                    step={300}
                    value={dispatchWindowEndTime}
                    onChange={e => setDispatchWindowEndTime(e.target.value)}
                    disabled={!canEditDispatchWindow}
                    className="bg-surface-2 border border-border-subtle-2 rounded-lg px-[13px] py-[9px] text-[15px] text-text-primary outline-none w-full focus:border-accent disabled:opacity-70"
                  />
                </div>

                <div className="col-span-2 flex flex-wrap gap-2">
                  {[
                    ["08:00", "18:00", t("integrationPage.window.presetCommercial")],
                    ["09:00", "18:00", t("integrationPage.window.presetStandard")],
                    ["10:00", "19:00", t("integrationPage.window.presetAfternoon")],
                  ].map(([start, end, label]) => (
                    <button
                      key={label}
                      type="button"
                      disabled={!canEditDispatchWindow}
                      onClick={() => {
                        setDispatchWindowStartTime(start);
                        setDispatchWindowEndTime(end);
                      }}
                      className="text-xs px-3 py-1.5 rounded-full border border-border-subtle bg-surface-2 text-text-secondary hover:border-accent/50 hover:text-text-primary disabled:opacity-60"
                    >
                      {label}: {start} - {end}
                    </button>
                  ))}
                </div>
              </div>

              <div className="text-[12px] text-text-muted mb-4">
                {t("integrationPage.window.rules")}
              </div>

              {canEditDispatchWindow ? (
                <Button size="sm" variant="primary" onClick={handleSaveDispatchWindow}>
                  {windowSaving ? t("common.saving") : t("common.save")}
                </Button>
              ) : (
                <div className="text-xs text-text-muted">{t("integrationPage.window.adminOnly")}</div>
              )}
            </>
          )}
        </div>
      </div>
    </div>
  );
};
