// ─── SmartCollect · API Service ────────────────────────────────────────────
// Centralizes all HTTP calls, session storage and error handling.
// ───────────────────────────────────────────────────────────────────────────

const API_BASE_URL =
  import.meta.env.VITE_API_BASE_URL ??
  "http://localhost:5013";

const SESSION_KEY = "smartcollect.session";

// ── Types ─────────────────────────────────────────────────────────────────

export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number
  ) {
    super(message);
    this.name = "ApiError";
  }
}

export interface StoredSession {
  token: string;
  userName: string;
  email: string;
  photoUrl?: string | null;
  role: string;          // "Master" | "Admin" | "Worker"
  tenantId: string;      // empty string for Master
  expiresAt: string;
}

export interface AuthResponse {
  token: string;
  userName: string;
  email: string;
  photoUrl?: string | null;
  role: string;
  tenantId: string;
  expiresAt: string;
}

export interface UpdateProfileRequest {
  name: string;
  email: string;
  currentPassword?: string;
  newPassword?: string;
  photoUrl?: string | null;
  removePhoto?: boolean;
}

export interface DashboardSummaryResponse {
  totalReceivable: number;
  totalOverdue: number;
  totalPaid: number;      // renamed from totalRecovered (B-06)
  recoveryRate: number;
}

export interface DashboardStatusBreakdownResponse {
  open: number;
  pendingData: number;
  overdue: number;
  paid: number;
  cancelled: number;
}

export interface FunnelItem {
  month: string;
  receivable: number;
  overdue: number;
  recovered: number;
}

export interface FunnelDataResponse { items: FunnelItem[] }

export interface AgingItemResponse {
  range: string;
  value: number;
  color: string;
}

export interface AgingListResponse { items: AgingItemResponse[] }

export interface DefaulterItem {
  clientName: string;
  taxId: string;
  totalAmount: number;
  titleCount: number;
}

export interface TopDefaultersResponse { items: DefaulterItem[] }

export interface SendsDayItem {
  day: string;
  emailCount: number;
  whatsAppCount: number;
}

export interface SendsPerDayResponse { items: SendsDayItem[] }

export interface ChannelMetricsResponse {
  emailSent: number;
  emailDelivered: number;
  emailViewed: number;
  whatsAppSent: number;
  whatsAppDelivered: number;
  whatsAppViewed: number;
}

export interface ActivityLogItemResponse {
  id: string;
  timestamp: string;
  channel: string;
  status: string;
  recipient: string;
  summary: string;
}

export interface ActivityLogResponse { items: ActivityLogItemResponse[] }

export interface PaginatedResponse<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface TitleResponse {
  id: string;
  clientId: string;
  clientName: string;
  clientTaxId: string;
  uniqueCode: string;
  amount: number;
  dueDate: string;
  issueDate: string;
  boletoUrl?: string | null;
  status: string;
  channels: string[];
  lastAction?: string | null;
  lastActionAt?: string | null;
  isBoletoOverdue: boolean;
}

export interface TitleHistoryResponse {
  id: string;
  timestamp: string;
  action: string;
  description: string;
}

export interface CreateTitleRequest {
  clientId: string;
  uniqueCode: string;
  amount: number;
  dueDate: string;
  issueDate: string;
  boletoUrl?: string;
}

export interface SendCollectionRequest {
  useQuickTemplate?: boolean;
  channel?: string;
  subject?: string;
  body?: string;
  contactIds?: string[];
}

export interface UpdateTitleStatusRequest {
  status: string;
}

export interface ClientResponse {
  id: string;
  legalName: string;
  taxId: string;
  tradeName?: string | null;
  contactCount: number;
  titleCount: number;
  sendToAllContacts: boolean;
  dispatchMode: "Primary" | "All" | "Selected";
  selectedContactIds: string[];
}

export interface UpdateClientDispatchPreferenceRequest {
  sendToAllContacts?: boolean;
  dispatchMode?: "Primary" | "All" | "Selected";
  selectedContactIds?: string[];
}

export interface CreateClientRequest {
  legalName: string;
  taxId: string;
  tradeName?: string;
}

export interface ContactResponse {
  id: string;
  clientId: string;
  companyName: string;
  companyTaxId: string;
  name: string;
  department: string;
  email?: string | null;
  whatsAppPhone?: string | null;
  isPrimary: boolean;
  titleCount: number;
  status: string;
}

export interface UpsertContactRequest {
  name: string;
  email: string;
  whatsAppPhone?: string;
  department: string;
  isPrimary: boolean;
}

export interface ImportResultResponse {
  importId: string;
  fileName: string;
  totalRows: number;
  successRows: number;
  errorRows: number;
  status: string;
}

export interface SyncResponse {
  message: string;
  count: number;
}

export interface SyncHealthResponse {
  connected: boolean;
  checkedAt: string;
  baseUrl: string;
  docsUrl: string;
  authentication: string;
  endpoints: {
    pendingTitles: string;
    occurrences: string;
  };
}

export interface ExternalApiConfigResponse {
  baseUrl: string;
  docsUrl?: string | null;
  pendingTitlesPath: string;
  occurrencesPath: string;
  authenticationScheme: string;
  hasToken: boolean;
  checkedAt?: string | null;
  connected?: boolean | null;
}

export interface ExternalApiConfigRequest {
  baseUrl: string;
  docsUrl?: string;
  pendingTitlesPath: string;
  occurrencesPath: string;
  authenticationScheme: string;
  token?: string;
  clearToken?: boolean;
}

export interface MessageTemplateResponse {
  id: string;
  name: string;
  channel: string;
  subject?: string | null;
  body: string;
  type: string;
  active: boolean;
}

export interface CreateTemplateRequest {
  name: string;
  channel: string;
  subject?: string;
  body: string;
  type: string;
}

export interface UpdateTemplateRequest {
  name: string;
  channel: string;
  subject?: string;
  body: string;
  type: string;
  active: boolean;
}

export interface TriggerResponse {
  id: string;
  templateId: string;
  channel: string;
  daysOffset: number;
  reference: string;
  order: number;
  active: boolean;
  templateName?: string | null;
}

export interface CreateTriggerPayload {
  templateId: string;
  channel: string;
  daysOffset: number;
  reference: string;
  order: number;
  active: boolean;
}

export interface CollectionRuleResponse {
  id: string;
  name: string;
  description?: string | null;
  active: boolean;
  triggers: TriggerResponse[];
  isDefault: boolean;
}

export interface CreateCollectionRuleRequest {
  name: string;
  description?: string;
  active: boolean;
  triggers: CreateTriggerPayload[];
}

export interface SmtpConfigResponse {
  host: string;
  port: number;
  user: string;
  senderFrom: string;
  senderName: string;
}

export interface SmtpConfigRequest {
  host: string;
  port: number;
  user: string;
  password?: string;
  senderFrom: string;
  senderName: string;
}

export type WhatsAppProvider = "Twilio" | "Z-API" | "Evolution API" | "360dialog";

export interface WhatsAppConfigResponse {
  provider: WhatsAppProvider;
  numberId: string;
  apiBaseUrl?: string | null;
  hasAccessToken: boolean;
  webhookUrl: string;
}

export interface WhatsAppConfigRequest {
  provider: WhatsAppProvider;
  numberId: string;
  accessToken?: string;
  apiBaseUrl?: string;
  clearToken?: boolean;
}

export interface TenantResponse {
  id: string;
  companyName: string;
  taxId: string;
  emailDomain: string;
  plan: string;
  active: boolean;
  userCount: number;
  titleCount: number;
  createdAt: string;
  adminName?: string | null;
  adminEmail?: string | null;
}

export interface CreateTenantRequest {
  companyName: string;
  taxId: string;
  emailDomain: string;
  adminName: string;
  adminEmail: string;
  adminPassword: string;
}

export interface UpdateTenantRequest {
  companyName: string;
  taxId: string;
  emailDomain: string;
  editAdminLogin: boolean;
  adminName?: string;
  adminEmail?: string;
  adminPassword?: string;
}

export interface UpdateTenantAccessRequest {
  active: boolean;
}

export interface WorkerResponse {
  id: string;
  name: string;
  email: string;
  active: boolean;
  createdAt: string;
  lastLogin?: string | null;
}

export interface UpdateWorkerRequest {
  name: string;
  active: boolean;
  password?: string;
}

// ── Session helpers ────────────────────────────────────────────────────────

export function getSession(): StoredSession | null {
  try {
    const raw = localStorage.getItem(SESSION_KEY);
    if (!raw) return null;
    const s = JSON.parse(raw) as StoredSession;
    // Check expiry
    if (new Date(s.expiresAt) < new Date()) {
      localStorage.removeItem(SESSION_KEY);
      return null;
    }
    return s;
  } catch {
    localStorage.removeItem(SESSION_KEY);
    return null;
  }
}

export function setSession(session: StoredSession | null): void {
  if (!session) {
    localStorage.removeItem(SESSION_KEY);
  } else {
    localStorage.setItem(SESSION_KEY, JSON.stringify(session));
  }
}

function clearSessionAndNotify(): void {
  setSession(null);
  window.dispatchEvent(new Event("smartcollect:unauthorized"));
}

// ── Core request ───────────────────────────────────────────────────────────

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const session = getSession();
  const headers = new Headers(init?.headers);
  headers.set("Accept", "application/json");

  if (!headers.has("Content-Type") && init?.body && !(init.body instanceof FormData)) {
    headers.set("Content-Type", "application/json");
  }

  if (session?.token) {
    headers.set("Authorization", `Bearer ${session.token}`);
  }

  let response: Response;
  try {
    response = await fetch(`${API_BASE_URL}${path}`, { ...init, headers });
  } catch {
    throw new ApiError("Sem conexão com o servidor. Verifique se a API está rodando.", 0);
  }

  const isLoginRequest = path.startsWith("/api/auth/login");

  if (response.status === 401 && !!session?.token && !isLoginRequest) {
    clearSessionAndNotify();
    throw new ApiError("Sessão expirada. Faça login novamente.", 401);
  }

  if (!response.ok) {
    let message = `Erro HTTP ${response.status}`;
    try {
      const payload = await response.json() as { message?: string; title?: string; errors?: Record<string, string[]> };
      if (payload?.errors) {
        const list = Object.values(payload.errors).flat();
        message = list.length > 0 ? list.join(" | ") : (payload.title ?? message);
      } else if (payload?.message) {
        message = payload.message;
      } else if (payload?.title) {
        message = payload.title;
      }
    } catch { /* ignore */ }
    throw new ApiError(message, response.status);
  }

  if (response.status === 204) return undefined as T;
  return response.json() as Promise<T>;
}

// ── Auth ──────────────────────────────────────────────────────────────────

export async function login(email: string, password: string): Promise<StoredSession> {
  const auth = await request<AuthResponse>("/api/auth/login", {
    method: "POST",
    body: JSON.stringify({ email, password }),
  });
  const session: StoredSession = {
    token: auth.token,
    userName: auth.userName,
    email: auth.email,
    photoUrl: auth.photoUrl ?? null,
    role: auth.role,
    tenantId: auth.tenantId ?? "",
    expiresAt: auth.expiresAt,
  };
  setSession(session);
  return session;
}

export async function updateMyProfile(payload: UpdateProfileRequest): Promise<StoredSession> {
  const auth = await request<AuthResponse>("/api/auth/profile", {
    method: "PUT",
    body: JSON.stringify(payload),
  });

  const session: StoredSession = {
    token: auth.token,
    userName: auth.userName,
    email: auth.email,
    photoUrl: auth.photoUrl ?? null,
    role: auth.role,
    tenantId: auth.tenantId ?? "",
    expiresAt: auth.expiresAt,
  };

  setSession(session);
  return session;
}

export async function registerWorker(
  name: string,
  email: string,
  password: string
): Promise<AuthResponse> {
  return request<AuthResponse>("/api/auth/register", {
    method: "POST",
    body: JSON.stringify({ name, email, password }),
  });
}

export async function registerMaster(
  name: string,
  email: string,
  password: string
): Promise<AuthResponse> {
  return request<AuthResponse>("/api/auth/register-master", {
    method: "POST",
    body: JSON.stringify({ name, email, password }),
  });
}

// ── Tenants (Master only) ─────────────────────────────────────────────────

export async function getTenants(): Promise<TenantResponse[]> {
  return request<TenantResponse[]>("/api/tenants");
}

export async function getTenantById(id: string): Promise<TenantResponse> {
  return request<TenantResponse>(`/api/tenants/${id}`);
}

export async function createTenant(payload: CreateTenantRequest): Promise<TenantResponse> {
  return request<TenantResponse>("/api/tenants", {
    method: "POST",
    body: JSON.stringify(payload),
  });
}

export async function updateTenant(id: string, payload: UpdateTenantRequest): Promise<TenantResponse> {
  return request<TenantResponse>(`/api/tenants/${id}`, {
    method: "PUT",
    body: JSON.stringify(payload),
  });
}

export async function updateTenantAccess(id: string, payload: UpdateTenantAccessRequest): Promise<TenantResponse> {
  try {
    return await request<TenantResponse>(`/api/tenants/${id}/access`, {
      method: "PATCH",
      body: JSON.stringify(payload),
    });
  } catch (err) {
    if (err instanceof ApiError && err.status === 404) {
      return request<TenantResponse>(`/api/tenants/${id}/access/${payload.active}`, {
        method: "PUT",
      });
    }

    throw err;
  }
}

// ── Dashboard ─────────────────────────────────────────────────────────────

function tenantParam(tenantId?: string): string {
  return tenantId ? `?tenantId=${tenantId}` : "";
}

function buildDashboardQuery(tenantId?: string, startDate?: string, endDate?: string): string {
  const q = new URLSearchParams();
  if (tenantId) q.set("tenantId", tenantId);
  if (startDate) q.set("startDate", startDate);
  if (endDate) q.set("endDate", endDate);
  const query = q.toString();
  return query ? `?${query}` : "";
}

export async function getDashboardSummary(tenantId?: string) {
  return request<DashboardSummaryResponse>(`/api/dashboard/summary${tenantParam(tenantId)}`);
}

export async function getDashboardStatusBreakdown(tenantId?: string) {
  return request<DashboardStatusBreakdownResponse>(`/api/dashboard/status-breakdown${tenantParam(tenantId)}`);
}

export async function getDashboardFunnel(tenantId?: string) {
  return request<FunnelDataResponse>(`/api/dashboard/funnel${tenantParam(tenantId)}`);
}

export async function getDashboardAging(tenantId?: string) {
  return request<AgingListResponse>(`/api/dashboard/aging${tenantParam(tenantId)}`);
}

export async function getDashboardTopDefaulters(tenantId?: string) {
  return request<TopDefaultersResponse>(`/api/dashboard/top-defaulters${tenantParam(tenantId)}`);
}

export async function getDashboardSendsPerDay(tenantId?: string) {
  return request<SendsPerDayResponse>(`/api/dashboard/sends-per-day${tenantParam(tenantId)}`);
}

export async function getDashboardChannelMetrics(tenantId?: string, startDate?: string, endDate?: string) {
  return request<ChannelMetricsResponse>(`/api/dashboard/channel-metrics${buildDashboardQuery(tenantId, startDate, endDate)}`);
}

export async function getDashboardActivityLog(tenantId?: string, startDate?: string, endDate?: string) {
  return request<ActivityLogResponse>(`/api/dashboard/activity-log${buildDashboardQuery(tenantId, startDate, endDate)}`);
}

// ── Titles ────────────────────────────────────────────────────────────────

export async function getTitles(params: {
  status?: string;
  search?: string;
  page?: number;
  pageSize?: number;
}) {
  const q = new URLSearchParams();
  if (params.status) q.set("status", params.status);
  if (params.search) q.set("search", params.search);
  q.set("page", String(params.page ?? 1));
  q.set("pageSize", String(params.pageSize ?? 20));
  return request<PaginatedResponse<TitleResponse>>(`/api/titles?${q.toString()}`);
}

export async function getTitleById(id: string) {
  return request<TitleResponse>(`/api/titles/${id}`);
}

export async function getTitleHistory(id: string) {
  return request<TitleHistoryResponse[]>(`/api/titles/${id}/history`);
}

export async function createTitle(payload: CreateTitleRequest) {
  return request<TitleResponse>("/api/titles", {
    method: "POST",
    body: JSON.stringify(payload),
  });
}

export async function updateTitleStatus(id: string, payload: UpdateTitleStatusRequest) {
  return request<TitleResponse>(`/api/titles/${id}/status`, {
    method: "PATCH",
    body: JSON.stringify(payload),
  });
}

export async function sendCollection(titleId: string, payload?: SendCollectionRequest) {
  return request<{ message: string }>(`/api/titles/${titleId}/collect`, {
    method: "POST",
    body: payload ? JSON.stringify(payload) : undefined,
  });
}

// ── Clients ───────────────────────────────────────────────────────────────

export async function getClients() {
  return request<ClientResponse[]>("/api/clients");
}

export async function createClient(payload: CreateClientRequest) {
  return request<ClientResponse>("/api/clients", {
    method: "POST",
    body: JSON.stringify(payload),
  });
}

export async function updateClientDispatchPreference(clientId: string, payload: UpdateClientDispatchPreferenceRequest) {
  return request<ClientResponse>(`/api/clients/${clientId}/dispatch-preference`, {
    method: "PATCH",
    body: JSON.stringify(payload),
  });
}

// ── Contacts ──────────────────────────────────────────────────────────────

export async function getContactsByClient(clientId: string) {
  return request<ContactResponse[]>(`/api/clients/${clientId}/contacts`);
}

export async function createContact(clientId: string, payload: UpsertContactRequest) {
  return request<ContactResponse>(`/api/clients/${clientId}/contacts`, {
    method: "POST",
    body: JSON.stringify(payload),
  });
}

export async function updateContact(
  clientId: string,
  contactId: string,
  payload: UpsertContactRequest
) {
  return request<ContactResponse>(`/api/clients/${clientId}/contacts/${contactId}`, {
    method: "PUT",
    body: JSON.stringify(payload),
  });
}

export async function deleteContact(clientId: string, contactId: string) {
  return request<void>(`/api/clients/${clientId}/contacts/${contactId}`, {
    method: "DELETE",
  });
}

// ── Import ────────────────────────────────────────────────────────────────

export async function uploadImportFile(file: File) {
  const formData = new FormData();
  formData.append("file", file);
  return request<ImportResultResponse>("/api/import/upload", {
    method: "POST",
    body: formData,
  });
}

// ── Sync ──────────────────────────────────────────────────────────────────

export async function syncPendingTitles() {
  return request<SyncResponse>("/api/sync/pending-titles", { method: "POST" });
}

export async function syncOccurrences() {
  return request<SyncResponse>("/api/sync/occurrences", { method: "POST" });
}

export async function getSyncHealth() {
  return request<SyncHealthResponse>("/api/sync/health");
}

// ── Templates ─────────────────────────────────────────────────────────────

export async function getTemplates() {
  return request<MessageTemplateResponse[]>("/api/templates");
}

export async function createTemplate(payload: CreateTemplateRequest) {
  return request<MessageTemplateResponse>("/api/templates", {
    method: "POST",
    body: JSON.stringify(payload),
  });
}

export async function updateTemplate(id: string, payload: UpdateTemplateRequest) {
  return request<MessageTemplateResponse>(`/api/templates/${id}`, {
    method: "PUT",
    body: JSON.stringify(payload),
  });
}

// ── Collection Rules ──────────────────────────────────────────────────────

export async function getCollectionRules() {
  return request<CollectionRuleResponse[]>("/api/collection-rules");
}

export async function createCollectionRule(payload: CreateCollectionRuleRequest) {
  return request<CollectionRuleResponse>("/api/collection-rules", {
    method: "POST",
    body: JSON.stringify(payload),
  });
}

export async function updateCollectionRule(id: string, payload: CreateCollectionRuleRequest) {
  return request<CollectionRuleResponse>(`/api/collection-rules/${id}`, {
    method: "PUT",
    body: JSON.stringify(payload),
  });
}

export async function deleteCollectionRule(id: string) {
  return request<void>(`/api/collection-rules/${id}`, {
    method: "DELETE",
  });
}

// ── SMTP Config ───────────────────────────────────────────────────────────

export async function getSmtpConfig() {
  return request<SmtpConfigResponse>("/api/config/smtp");
}

export async function saveSmtpConfig(payload: SmtpConfigRequest) {
  return request<{ message: string }>("/api/config/smtp", {
    method: "POST",
    body: JSON.stringify(payload),
  });
}

export async function testSmtpConfig() {
  return request<{ message: string }>("/api/config/smtp/test", { method: "POST" });
}

export async function getWhatsAppConfig() {
  return request<WhatsAppConfigResponse>("/api/config/whatsapp");
}

export async function saveWhatsAppConfig(payload: WhatsAppConfigRequest) {
  return request<{ message: string }>("/api/config/whatsapp", {
    method: "POST",
    body: JSON.stringify(payload),
  });
}

export async function testWhatsAppConfig() {
  return request<{ message: string }>("/api/config/whatsapp/test", { method: "POST" });
}

export async function getExternalApiConfig() {
  return request<ExternalApiConfigResponse>("/api/config/external-api");
}

export async function saveExternalApiConfig(payload: ExternalApiConfigRequest) {
  return request<{ message: string }>("/api/config/external-api", {
    method: "POST",
    body: JSON.stringify(payload),
  });
}

export async function testExternalApiConfig() {
  return request<{ message: string }>("/api/config/external-api/test", { method: "POST" });
}

// ── Workers (Admin only) ──────────────────────────────────────────────────

export async function getWorkers() {
  return request<WorkerResponse[]>("/api/workers");
}

export async function updateWorker(id: string, payload: UpdateWorkerRequest) {
  return request<WorkerResponse>(`/api/workers/${id}`, {
    method: "PUT",
    body: JSON.stringify(payload),
  });
}
