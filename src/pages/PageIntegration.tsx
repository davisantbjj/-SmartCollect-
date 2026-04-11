import { useEffect, useState } from "react";
import { CardHeader, Button, FormInput, FormSelect } from "../components/UI";
import { ICONS } from "../utils/icons";
import { colors } from "../utils/colors";
import { t } from "../i18n";
import {
  ApiError, getSmtpConfig, saveSmtpConfig, testSmtpConfig,
  getSyncHealth,
  getExternalApiConfig,
  saveExternalApiConfig,
  testExternalApiConfig,
  type SmtpConfigResponse, type StoredSession, type SyncHealthResponse,
} from "../services/api";
import type { ShowToast } from "../types";

export const PageIntegration = ({
  showToast,
  session,
}: {
  showToast: ShowToast;
  session: StoredSession;
}) => {
  const [smtp, setSmtp] = useState<SmtpConfigResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [testing, setTesting] = useState(false);
  const [isConfigured, setIsConfigured] = useState(false);
  const [syncHealth, setSyncHealth] = useState<SyncHealthResponse | null>(null);
  const [syncHealthLoading, setSyncHealthLoading] = useState(true);

  const [apiLoading, setApiLoading] = useState(true);
  const [apiSaving, setApiSaving] = useState(false);
  const [apiTesting, setApiTesting] = useState(false);
  const [apiHasToken, setApiHasToken] = useState(false);
  const [apiBaseUrl, setApiBaseUrl] = useState("");
  const [apiDocsUrl, setApiDocsUrl] = useState("");
  const [apiPendingPath, setApiPendingPath] = useState("titulos-pendentes");
  const [apiOccurrencesPath, setApiOccurrencesPath] = useState("ocorrencias?data={date}");
  const [apiAuthScheme, setApiAuthScheme] = useState("Bearer");
  const [apiToken, setApiToken] = useState("");
  const [clearApiToken, setClearApiToken] = useState(false);

  // Form state
  const [host, setHost] = useState("");
  const [port, setPort] = useState("587");
  const [user, setUser] = useState("");
  const [password, setPassword] = useState("");
  const [senderFrom, setSenderFrom] = useState("");
  const [senderName, setSenderName] = useState("");

  const canEdit = session.role === "Admin" || session.role === "Master";

  useEffect(() => {
    const load = async () => {
      try {
        const data = await getSmtpConfig();
        setSmtp(data);
        setHost(data.host);
        setPort(String(data.port));
        setUser(data.user);
        setSenderFrom(data.senderFrom);
        setSenderName(data.senderName);
        setIsConfigured(true);
      } catch (err) {
        if (err instanceof ApiError && err.status === 404) {
          setIsConfigured(false);
        } else {
          showToast(`${ICONS.cross} Erro ao carregar config SMTP.`, "error");
        }
      } finally {
        setLoading(false);
      }
    };
    void load();
  }, [showToast]);

  const loadSyncHealth = async () => {
    try {
      setSyncHealthLoading(true);
      const data = await getSyncHealth();
      setSyncHealth(data);
    } catch (err) {
      if (!(err instanceof ApiError && err.status === 403)) {
        showToast(`${ICONS.cross} Falha ao validar integração externa.`, "error");
      }
      setSyncHealth(null);
    } finally {
      setSyncHealthLoading(false);
    }
  };

  useEffect(() => {
    void loadSyncHealth();
  }, [showToast]);

  const loadApiConfig = async () => {
    try {
      setApiLoading(true);
      const data = await getExternalApiConfig();
      setApiBaseUrl(data.baseUrl ?? "");
      setApiDocsUrl(data.docsUrl ?? "");
      setApiPendingPath(data.pendingTitlesPath ?? "titulos-pendentes");
      setApiOccurrencesPath(data.occurrencesPath ?? "ocorrencias?data={date}");
      setApiAuthScheme(data.authenticationScheme ?? "Bearer");
      setApiHasToken(Boolean(data.hasToken));
      setClearApiToken(false);
      setApiToken("");
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : "Falha ao carregar configuracao da API externa.";
      showToast(`${ICONS.cross} ${msg}`, "error");
    } finally {
      setApiLoading(false);
    }
  };

  useEffect(() => {
    void loadApiConfig();
  }, [showToast]);

  const handleSave = async () => {
    if (!host || !port || !user) {
      showToast(`${ICONS.warning} Host, porta e usuário são obrigatórios.`, "warn");
      return;
    }
    try {
      setSaving(true);
      await saveSmtpConfig({
        host, port: parseInt(port, 10),
        user, password: password || undefined,
        senderFrom, senderName,
      });
      setIsConfigured(true);
      setPassword(""); // Clear password after save for security
      showToast(`${ICONS.checkmark} ${t("toast.smtpSaved")}`, "success");
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : "Erro ao salvar SMTP.";
      showToast(`${ICONS.cross} ${msg}`, "error");
    } finally {
      setSaving(false);
    }
  };

  const handleTest = async () => {
    if (!isConfigured) {
      showToast(`${ICONS.warning} Salve a configuração SMTP antes de testar.`, "warn");
      return;
    }
    try {
      setTesting(true);
      const res = await testSmtpConfig();
      showToast(`${ICONS.mailbox} ${res.message}`, "success");
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : "Falha no teste SMTP.";
      showToast(`${ICONS.cross} ${msg}`, "error");
    } finally {
      setTesting(false);
    }
  };

  const handleSaveExternalApi = async () => {
    if (!apiBaseUrl.trim() || !apiPendingPath.trim() || !apiOccurrencesPath.trim()) {
      showToast(`${ICONS.warning} Base URL e endpoints sao obrigatorios.`, "warn");
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
      });

      setApiHasToken(clearApiToken ? false : (apiToken.trim() ? true : apiHasToken));
      setApiToken("");
      setClearApiToken(false);
      showToast(`${ICONS.checkmark} Configuracao da API externa salva.`, "success");
      await loadSyncHealth();
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : "Falha ao salvar configuracao da API externa.";
      showToast(`${ICONS.cross} ${msg}`, "error");
    } finally {
      setApiSaving(false);
    }
  };

  const handleTestExternalApi = async () => {
    try {
      setApiTesting(true);
      const res = await testExternalApiConfig();
      showToast(`${ICONS.checkmark} ${res.message}`, "success");
      await loadSyncHealth();
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : "Falha ao testar API externa.";
      showToast(`${ICONS.cross} ${msg}`, "error");
      await loadSyncHealth();
    } finally {
      setApiTesting(false);
    }
  };

  return (
    <div className="animate-fade-up">
      <div className="mb-4 bg-accent/[0.08] border border-accent/25 rounded-xl px-4 py-3 text-[12.5px] text-text-secondary">
        <strong className="text-text-primary">Tela de Integracao = configuracao.</strong> Configure SMTP, WhatsApp e API externa aqui.
        A tela de Importacao e para operacao (upload e sincronizacao manual).
      </div>

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
              <div className="text-sm text-text-muted py-4">Carregando...</div>
            ) : (
              <>
                <div className="grid grid-cols-2 gap-3.5">
                  <FormInput label={t("integration.smtpHost")} value={host} onChange={e => setHost(e.target.value)} disabled={!canEdit} placeholder="mail.empresa.com.br" />
                  <FormInput label={t("integration.port")} value={port} onChange={e => setPort(e.target.value)} disabled={!canEdit} placeholder="587" type="number" />
                  <FormInput label={t("integration.user")} value={user} onChange={e => setUser(e.target.value)} disabled={!canEdit} placeholder="financeiro@empresa.com.br" />
                  <FormInput label={t("integration.password")} type="password" value={password} onChange={e => setPassword(e.target.value)} disabled={!canEdit} placeholder={isConfigured ? "••••••••" : "Senha SMTP"} />
                  <FormInput label={t("integration.senderFrom")} value={senderFrom} onChange={e => setSenderFrom(e.target.value)} disabled={!canEdit} placeholder="financeiro@empresa.com.br" />
                  <FormInput label={t("integration.senderName")} value={senderName} onChange={e => setSenderName(e.target.value)} disabled={!canEdit} placeholder="Financeiro – Empresa" />
                </div>

                {senderFrom && (
                  <div className="mt-3 text-xs text-text-muted bg-surface-2 rounded-lg px-3 py-2 border border-border-subtle">
                    {ICONS.email} Remetente: <strong className="text-text-primary">{senderName || "SmartCollect"} &lt;{senderFrom}&gt;</strong>
                  </div>
                )}

                {canEdit && (
                  <div className="mt-4 flex gap-2.5">
                    <Button size="sm" variant="secondary" onClick={handleTest}>
                      {testing ? "Testando..." : <>{ICONS.mailbox} {t("integration.testSend")}</>}
                    </Button>
                    <Button size="sm" variant="primary" onClick={handleSave}>
                      {saving ? "Salvando..." : t("common.save")}
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
            right={<span className="bg-warn/[0.12] text-warn text-[11px] font-bold px-[9px] py-[3px] rounded-full">{t("common.pending")}</span>}
          />
          <div className="p-5">
            <div className="grid grid-cols-2 gap-3.5">
              <div className="col-span-2">
                <FormSelect label={t("integration.selectProvider")} disabled={!canEdit}>
                  <option>{t("integration.selectProvider")}</option>
                  <option>Twilio</option>
                  <option>Z-API</option>
                  <option>Evolution API</option>
                  <option>360dialog</option>
                </FormSelect>
              </div>
              <FormInput label={t("integration.numberId")} placeholder={t("integration.numberId")} disabled={!canEdit} />
              <FormInput label={t("integration.accessToken")} type="password" placeholder={t("integration.accessToken")} disabled={!canEdit} />
              <div className="col-span-2">
                <label className="text-[11px] font-bold tracking-[0.6px] uppercase text-text-muted mb-[5px] block">{t("integration.webhookUrl")}</label>
                <input readOnly value="https://smartcollect.app/webhook/whatsapp"
                  className="bg-surface-2 border border-white/[0.11] rounded-lg px-[13px] py-[9px] text-[13px] text-text-muted outline-none w-full" />
              </div>
            </div>
            {canEdit && (
              <div className="mt-4">
                <Button size="sm" variant="primary" onClick={() => showToast(`${ICONS.checkmark} ${t("toast.configSaved")}`, "success")}>
                  {t("common.save")}
                </Button>
              </div>
            )}
          </div>
        </div>
      </div>

      {/* API externa */}
      <div className="bg-surface border border-border-subtle rounded-[14px] overflow-hidden">
        <CardHeader
          title={<>{ICONS.link} API Externa - Atos (JSON)</>}
          subtitle="A API deve enviar JSON com o mesmo formato da planilha de importacao"
          right={
            <span className={`${syncHealth?.connected ? "bg-success/[0.12] text-success" : "bg-danger/[0.12] text-danger"} text-[11px] font-bold px-[9px] py-[3px] rounded-full flex items-center gap-[5px]`}>
              <span className="text-[7px]">{ICONS.dot}</span>
              {syncHealthLoading ? "Verificando" : syncHealth?.connected ? t("common.online") : "Offline"}
            </span>
          }
        />
        <div className="p-5">
          {apiLoading ? (
            <div className="text-sm text-text-muted py-4">Carregando configuracao...</div>
          ) : (
            <>
              <div className="grid grid-cols-2 gap-3.5 mb-4">
                <div className="col-span-2">
                  <FormInput
                    label="Base URL"
                    value={apiBaseUrl}
                    onChange={e => setApiBaseUrl(e.target.value)}
                    disabled={!canEdit}
                    placeholder="https://api.parceiro.com.br/v1/"
                  />
                </div>
                <div className="col-span-2">
                  <FormInput
                    label="URL Documentacao"
                    value={apiDocsUrl}
                    onChange={e => setApiDocsUrl(e.target.value)}
                    disabled={!canEdit}
                    placeholder="https://api.parceiro.com.br/swagger"
                  />
                </div>
                <FormInput
                  label="Endpoint Titulos Pendentes"
                  value={apiPendingPath}
                  onChange={e => setApiPendingPath(e.target.value)}
                  disabled={!canEdit}
                  placeholder="titulos-pendentes"
                />
                <FormInput
                  label="Endpoint Ocorrencias"
                  value={apiOccurrencesPath}
                  onChange={e => setApiOccurrencesPath(e.target.value)}
                  disabled={!canEdit}
                  placeholder="ocorrencias?data={date}"
                />
                <FormSelect
                  label="Autenticacao"
                  value={apiAuthScheme}
                  onChange={e => setApiAuthScheme(e.target.value)}
                  disabled={!canEdit}
                >
                  <option value="Bearer">Bearer</option>
                  <option value="None">Sem autenticacao</option>
                </FormSelect>
                <FormInput
                  label={apiHasToken ? "Token (deixe vazio para manter)" : "Token"}
                  type="password"
                  value={apiToken}
                  onChange={e => setApiToken(e.target.value)}
                  disabled={!canEdit}
                  placeholder={apiHasToken ? "••••••••••••" : "Cole o token da API"}
                />
              </div>

              {canEdit && (
                <label className="flex items-center gap-2 text-xs text-text-secondary mb-4">
                  <input
                    type="checkbox"
                    checked={clearApiToken}
                    onChange={e => setClearApiToken(e.target.checked)}
                  />
                  Remover token salvo
                </label>
              )}

              <div className="bg-surface-2 rounded-[10px] px-[18px] py-4 font-mono text-[12.5px] leading-[2.2] border border-white/[0.06] mb-4">
                {([
                  ["Base URL:", syncHealth?.baseUrl ?? "N/A", colors.accent],
                  ["Endpoint 1:", syncHealth?.endpoints.pendingTitles ?? "N/A", colors.text3],
                  ["Endpoint 2:", syncHealth?.endpoints.occurrences ?? "N/A", colors.text3],
                  ["Documentacao:", syncHealth?.docsUrl ?? "N/A", colors.text2],
                  ["Autenticacao:", syncHealth?.authentication ?? "N/A", colors.text2],
                ] as [string, string, string][]).map(([key, val, color]) => (
                  <div key={key}>
                    <span className="text-text-muted">{key}</span>{" "}
                    <span style={{ color }}>{val}</span>
                  </div>
                ))}
                <div>
                  <span className="text-text-muted">Token:</span>{" "}
                  <span style={{ color: colors.text2 }}>{apiHasToken ? "Configurado" : "Nao configurado"}</span>
                </div>
                {syncHealth?.checkedAt && (
                  <div>
                    <span className="text-text-muted">Ultima verificacao:</span>{" "}
                    <span style={{ color: colors.text2 }}>{new Date(syncHealth.checkedAt).toLocaleString("pt-BR")}</span>
                  </div>
                )}
              </div>
            </>
          )}

          <div className="text-[12px] text-text-muted mb-4">
            Campos esperados no JSON (mesmo layout da planilha): nome_cliente, cnpj, codigo_titulo, valor, data_vencimento, data_emissao,
            status, email, telefone_whatsapp, link_boleto.
          </div>

          <div className="flex gap-2.5">
            {canEdit && (
              <Button
                size="sm"
                variant="primary"
                onClick={handleSaveExternalApi}
                disabled={apiSaving || apiLoading}
              >
                {apiSaving ? "Salvando..." : t("common.save")}
              </Button>
            )}
            <Button
              size="sm"
              variant="secondary"
              onClick={() => {
                if (!apiDocsUrl && !syncHealth?.docsUrl) {
                  showToast(`${ICONS.warning} URL de documentação não disponível.`, "warn");
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
              {apiTesting ? "Testando..." : <>{ICONS.testTube} {t("integration.testEndpoint")}</>}
            </Button>
          </div>
        </div>
      </div>
    </div>
  );
};
