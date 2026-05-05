import { useEffect, useState } from "react";
import { CardHeader, Button } from "../components/UI";
import { ICONS } from "../utils/icons";
import { t } from "../i18n";
import { ApiError, uploadImportFile, syncPendingTitles, syncOccurrences, getSyncHealth, type StoredSession, type SyncHealthResponse } from "../services/api";
import type { ShowToast } from "../types";

export const PageImport = ({
  showToast,
  session,
}: {
  showToast: ShowToast;
  session: StoredSession;
}) => {
  const [tab, setTab] = useState<"upload" | "api">("upload");
  const [dragging, setDragging] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [syncing1, setSyncing1] = useState(false);
  const [syncing2, setSyncing2] = useState(false);
  const [lastResult, setLastResult] = useState<{ totalRows: number; successRows: number; errorRows: number; fileName: string } | null>(null);
  const [syncHealth, setSyncHealth] = useState<SyncHealthResponse | null>(null);
  const [healthLoading, setHealthLoading] = useState(true);

  const canSync = session.role === "Admin";
  const layoutColumns = [
    { field: "nome_cliente", type: t("import.type.text"), required: true, example: "Construtora Alpha Ltda." },
    { field: "cnpj", type: t("import.type.text"), required: true, example: "12.345.678/0001-90" },
    { field: "codigo_titulo", type: t("import.type.text"), required: true, example: "TIT-2026-001" },
    { field: "valor", type: t("import.type.numeric"), required: true, example: "15000.00" },
    { field: "status", type: t("import.type.text"), required: true, example: "Em Aberto | Pendente de dados | Pago | Em Atraso | Cancelado" },
    { field: "data_vencimento", type: t("import.type.date"), required: true, example: "15/04/2026" },
    { field: "data_emissao", type: t("import.type.date"), required: false, example: "01/03/2026" },
    { field: "email", type: t("import.type.email"), required: false, example: "financeiro@empresa.com.br" },
    { field: "telefone_whatsapp", type: t("import.type.phone"), required: false, example: "5511999999999" },
    { field: "link_boleto", type: t("import.type.url"), required: false, example: "https://banco.com/boleto/..." },
  ] as const;

  const loadSyncHealth = async () => {
    try {
      setHealthLoading(true);
      const data = await getSyncHealth();
      setSyncHealth(data);
    } catch {
      setSyncHealth(null);
    } finally {
      setHealthLoading(false);
    }
  };

  useEffect(() => {
    void loadSyncHealth();
  }, []);

  const handleFile = async (file: File) => {
    if (!file.name.match(/\.(xlsx|csv|xlsm|xls)$/i)) {
      showToast(`${ICONS.cross} ${t("importPage.errors.invalidFormat")}`, "error");
      return;
    }
    try {
      setUploading(true);
      showToast(`${ICONS.dashboard} ${t("importPage.messages.uploading")} "${file.name}"`, "info");
      const res = await uploadImportFile(file);
      setLastResult(res);
      showToast(
        `${ICONS.checkmark} ${res.successRows} ${t("importPage.messages.imported")} · ${res.errorRows} ${t("importPage.messages.errors")}`,
        res.errorRows > 0 ? "warn" : "success"
      );
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : t("importPage.errors.uploadFailed");
      showToast(`${ICONS.cross} ${msg}`, "error");
    } finally {
      setUploading(false);
    }
  };

  const handleSync1 = async () => {
    try {
      setSyncing1(true);
      showToast(`${ICONS.refresh} ${t("toast.syncStarted")}`, "info");
      const res = await syncPendingTitles();
      showToast(`${ICONS.checkmark} ${res.message}`, "success");
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : t("importPage.errors.syncFailed");
      showToast(`${ICONS.cross} ${msg}`, "error");
    } finally {
      setSyncing1(false);
    }
  };

  const handleSync2 = async () => {
    try {
      setSyncing2(true);
      showToast(`${ICONS.refresh} ${t("toast.checkingOccurrences")}`, "info");
      const res = await syncOccurrences();
      showToast(`${ICONS.checkmark} ${res.message}`, "success");
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : t("importPage.errors.checkFailed");
      showToast(`${ICONS.cross} ${msg}`, "error");
    } finally {
      setSyncing2(false);
    }
  };

  return (
    <div className="animate-fade-up">
      {/* Tabs */}
      <div className="flex gap-0.5 bg-surface-2 rounded-[9px] p-[3px] w-fit mb-[22px]">
        {([["upload", t("import.tabUpload")], ["api", t("import.tabApi")]] as const).map(([id, label]) => (
          <div
            key={id}
            onClick={() => setTab(id)}
            className={`px-[18px] py-2 rounded-[7px] text-[13px] font-semibold cursor-pointer transition-all duration-[170ms] ${
              tab === id
                ? "bg-surface text-text-primary shadow-[0_1px_4px_rgba(0,0,0,0.15)]"
                : "bg-transparent text-text-secondary"
            }`}
          >
            {ICONS.folder} {label}
          </div>
        ))}
      </div>

      {tab === "upload" && (
        <div>
          {/* Drop zone */}
          <div
            onDragOver={e => { e.preventDefault(); setDragging(true); }}
            onDragLeave={() => setDragging(false)}
            onDrop={e => { e.preventDefault(); setDragging(false); const f = e.dataTransfer.files[0]; if (f) handleFile(f); }}
            onClick={() => !uploading && document.getElementById("sc-file-input")?.click()}
            className={`border-2 border-dashed rounded-[14px] px-6 py-[52px] text-center cursor-pointer transition-all duration-200 ${
              dragging ? "border-accent bg-accent/[0.03]" : uploading ? "border-border-subtle-2 opacity-60" : "border-border-subtle-2 bg-surface hover:border-accent/40"
            }`}
          >
            <input id="sc-file-input" type="file" accept=".xlsx,.csv,.xlsm,.xls" className="hidden"
              onChange={e => e.target.files?.[0] && handleFile(e.target.files[0])} />
            <div className="text-[42px] mb-3.5">{uploading ? "⏳" : ICONS.dashboard}</div>
            <div className="font-extrabold text-base mb-1.5">
              {uploading ? t("importPage.messages.processing") : t("import.dragOrClick")}
            </div>
            <div className="text-[13px] text-text-secondary mb-2">{t("import.fileTypes")}</div>
            <div className="text-[11.5px] text-text-muted">{t("import.requiredColumns")}</div>
          </div>

          {/* Last result */}
          {lastResult && (
            <div className="mt-4 p-4 bg-surface-2 rounded-xl border border-border-subtle grid grid-cols-3 gap-4 text-center">
              <div>
                <div className="text-2xl font-extrabold text-text-primary">{lastResult.totalRows}</div>
                <div className="text-xs text-text-muted">{t("importPage.summary.totalRows")}</div>
              </div>
              <div>
                <div className="text-2xl font-extrabold text-success">{lastResult.successRows}</div>
                <div className="text-xs text-text-muted">{t("importPage.summary.imported")}</div>
              </div>
              <div>
                <div className="text-2xl font-extrabold text-danger">{lastResult.errorRows}</div>
                <div className="text-xs text-text-muted">{t("importPage.summary.errors")}</div>
              </div>
            </div>
          )}

          {/* Layout reference table */}
          <div className="mt-7">
            <div className="font-extrabold text-[15px] mb-3.5 flex items-center gap-2">{ICONS.clipboard} {t("import.expectedLayout")}</div>
            <div className="rounded-xl border border-border-subtle overflow-hidden bg-surface mb-4">
              <div className="max-h-[240px] overflow-auto">
                <table className="w-full min-w-[940px] border-collapse text-[11.5px]">
                  <thead>
                    <tr className="sticky top-0 z-20 bg-surface-2 border-b border-border-subtle">
                      <th className="sticky left-0 z-30 w-[48px] px-3 py-2 text-center text-[10px] font-bold text-text-muted border-r border-border-subtle bg-surface-2">#</th>
                      {layoutColumns.map((_, idx) => (
                        <th
                          key={`col-${idx}`}
                          className="px-3 py-2 text-center text-[10px] font-bold text-text-muted border-r border-border-subtle last:border-r-0"
                        >
                          {String.fromCharCode(65 + idx)}
                        </th>
                      ))}
                    </tr>
                    <tr className="sticky top-[33px] z-10 bg-surface-3 border-b border-border-subtle">
                      <th className="sticky left-0 z-20 px-3 py-2 text-center text-[11px] font-bold text-text-secondary border-r border-border-subtle bg-surface-3">1</th>
                      {layoutColumns.map(col => (
                        <th
                          key={col.field}
                          className="px-3 py-2 text-left font-mono text-[10.5px] text-accent border-r border-border-subtle last:border-r-0 max-w-[130px] truncate"
                          title={col.field}
                        >
                          {col.field}
                        </th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    <tr>
                      <td className="sticky left-0 z-10 px-3 py-2 text-center text-[11px] text-text-secondary border-r border-border-subtle bg-surface-3">2</td>
                      {layoutColumns.map(col => (
                        <td
                          key={`sample-${col.field}`}
                          className="px-3 py-2 font-mono text-[10.5px] text-text-muted border-r border-border-subtle last:border-r-0 max-w-[130px] truncate"
                          title={col.example}
                        >
                          {col.example}
                        </td>
                      ))}
                    </tr>
                  </tbody>
                </table>
              </div>
            </div>

            <div className="rounded-xl border border-border-subtle overflow-hidden">
              <table className="w-full border-collapse text-[13px]">
                <thead>
                  <tr className="bg-surface-2">
                    {[t("import.col.field"), t("import.col.type"), t("import.col.required"), t("import.col.example")].map(h => (
                      <th key={h} className="px-4 py-2.5 text-left text-[10.5px] font-bold tracking-[0.5px] uppercase text-text-muted border-b border-border-subtle">{h}</th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {layoutColumns.map(col => (
                    <tr key={col.field} className="border-b border-border-subtle">
                      <td className="px-4 py-[11px] font-mono text-xs text-accent">{col.field}</td>
                      <td className="px-4 py-[11px] text-text-secondary text-[13px]">{col.type}</td>
                      <td className="px-4 py-[11px]">
                        <span className={`text-[11px] font-bold px-[9px] py-[3px] rounded-full ${col.required ? "bg-success/12 text-success" : "bg-surface-3 text-text-muted"}`}>
                          {col.required ? t("importPage.required") : t("importPage.optional")}
                        </span>
                      </td>
                      <td className="px-4 py-[11px] text-xs text-text-muted font-mono max-w-[220px] truncate" title={col.example}>{col.example}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      )}

      {tab === "api" && (
        <div className="space-y-4">
          <div className="bg-accent/[0.08] border border-accent/25 rounded-xl px-4 py-3 text-[12.5px] text-text-secondary">
            <strong className="text-text-primary">{t("importPage.api.noticeTitle")}</strong> {t("importPage.api.noticeBody")}
          </div>

          <div className="grid grid-cols-2 gap-4">
            {/* Endpoint 1 */}
            <div className="bg-surface border border-border-subtle rounded-[14px] overflow-hidden">
              <CardHeader
                title={t("import.endpoint1.title")}
                subtitle={t("import.endpoint1.subtitle")}
                right={
                  <span className={`${syncHealth?.connected ? "bg-success/12 text-success" : "bg-danger/12 text-danger"} text-[11px] font-bold px-[9px] py-[3px] rounded-full flex items-center gap-[5px]`}>
                    <span className="text-[7px]">{ICONS.dot}</span>
                    {healthLoading ? t("importPage.api.checking") : syncHealth?.connected ? t("importPage.api.online") : t("importPage.api.offline")}
                  </span>
                }
              />
              <div className="p-5">
                <div className="bg-surface-2 rounded-lg px-3.5 py-3 font-mono text-xs text-accent mb-4 border border-border-subtle">
                  {syncHealth?.endpoints.pendingTitles ?? "GET /titulos-pendentes"}
                </div>
                <div className="text-[12.5px] leading-[2.1] text-text-secondary mb-4">
                  <div>{t("importPage.api.baseUrl")} <strong className="text-text-primary break-all">{syncHealth?.baseUrl ?? t("importPage.api.notAvailable")}</strong></div>
                  <div>{t("importPage.api.frequency")} <strong className="text-text-primary">{t("importPage.api.frequencyTwiceDaily")}</strong></div>
                  <div>{t("importPage.api.operation")} <strong className="text-text-primary">{t("importPage.api.operationUpsert")}</strong></div>
                  <div>{t("importPage.api.noContactStatus")} <strong className="text-text-primary">{t("importPage.api.pendingData")}</strong></div>
                  {syncHealth?.checkedAt && <div>{t("importPage.api.lastCheck")} <strong className="text-text-primary">{new Date(syncHealth.checkedAt).toLocaleString("pt-BR")}</strong></div>}
                </div>
                {canSync && (
                  <Button size="sm" variant="secondary" onClick={handleSync1} disabled={!syncHealth?.connected || syncing1}>
                    {syncing1 ? t("importPage.api.syncing") : t("import.syncNow")}
                  </Button>
                )}
              </div>
            </div>

            {/* Endpoint 2 */}
            <div className="bg-surface border border-border-subtle rounded-[14px] overflow-hidden">
              <CardHeader
                title={t("import.endpoint2.title")}
                subtitle={t("import.endpoint2.subtitle")}
                right={
                  <span className={`${syncHealth?.connected ? "bg-success/12 text-success" : "bg-danger/12 text-danger"} text-[11px] font-bold px-[9px] py-[3px] rounded-full flex items-center gap-[5px]`}>
                    <span className="text-[7px]">{ICONS.dot}</span>
                    {healthLoading ? t("importPage.api.checking") : syncHealth?.connected ? t("importPage.api.online") : t("importPage.api.offline")}
                  </span>
                }
              />
              <div className="p-5">
                <div className="bg-surface-2 rounded-lg px-3.5 py-3 font-mono text-xs text-accent mb-4 border border-border-subtle">
                  {syncHealth?.endpoints.occurrences ?? "GET /ocorrencias?data={date}"}
                </div>
                <div className="text-[12.5px] leading-[2.1] text-text-secondary mb-4">
                  <div>{t("importPage.api.frequency")} <strong className="text-text-primary">{t("importPage.api.frequencyDaily")}</strong></div>
                  <div>{t("importPage.api.parameters")} <strong className="text-text-primary">{t("importPage.api.parametersD1D0")}</strong></div>
                  <div>{t("importPage.api.action")} <strong className="text-text-primary">{t("importPage.api.actionImmediateStop")}</strong></div>
                  <div>{t("importPage.api.authentication")} <strong className="text-text-primary">{syncHealth?.authentication ?? t("importPage.api.authDefault")}</strong></div>
                </div>
                {canSync && (
                  <Button size="sm" variant="secondary" onClick={handleSync2} disabled={!syncHealth?.connected || syncing2}>
                    {syncing2 ? t("importPage.api.checkingOccurrences") : t("import.checkNow")}
                  </Button>
                )}
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
