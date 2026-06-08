"use client";

import type {
  AuditPayload,
  BusinessCheck,
  DemoClassification,
  DemoDocument,
  DispositionDecision,
  DispositionPayload,
  DispositionSnapshot,
  EmailDraftPayload,
  EntitiesPayload,
  FraudAcksPayload,
  FraudCheckPayload,
  RecommendationPayload,
  SIUHandoffResponse,
  SummaryPayload,
} from "./types";

const API_BASE = process.env.NEXT_PUBLIC_API_BASE_URL || "/api";

// In the demo, auth is optional. When MSAL is not configured we send requests
// unauthenticated. When it IS configured we acquire a token silently.
async function getToken(): Promise<string | null> {
  // Dynamically import so this is only ever executed client-side.
  const { isMsalConfigured, msalInstance, apiTokenRequest } = await import("../lib/msalConfig");
  if (!isMsalConfigured()) return null;
  const account = msalInstance.getActiveAccount() ?? msalInstance.getAllAccounts()[0];
  if (!account) return null;
  try {
    const result = await msalInstance.acquireTokenSilent({ ...apiTokenRequest, account });
    return result.accessToken;
  } catch {
    return null;
  }
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const token = await getToken();
  const headers: Record<string, string> = {};
  if (token) headers["Authorization"] = `Bearer ${token}`;
  if (body !== undefined) headers["Content-Type"] = "application/json";

  const res = await fetch(`${API_BASE}${path}`, {
    method,
    headers,
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });
  if (!res.ok) {
    const text = await res.text().catch(() => "");
    throw new Error(`${method} ${path} failed: ${res.status} ${text}`);
  }
  const text = await res.text();
  return text ? (JSON.parse(text) as T) : (undefined as T);
}

// ---- Real async intake ----
export interface AutoSubmitFile {
  file_name: string;
  mime_type: string;
  size: number;
  category: string;
  confidence: number;
  schema_id: string;
}

export interface AutoSubmitResponse {
  claim_id: string;
  schema_set_id: string;
  status?: string;
  files: AutoSubmitFile[];
}

export async function autoSubmitClaim(files: File[]): Promise<AutoSubmitResponse> {
  const token = await getToken();
  const form = new FormData();
  for (const f of files) {
    form.append("files", f, f.name);
  }
  const headers: Record<string, string> = {};
  if (token) headers["Authorization"] = `Bearer ${token}`;

  const res = await fetch(`${API_BASE}/claimsdemo/claims/auto-submit`, {
    method: "POST",
    headers,
    body: form,
  });
  if (!res.ok) {
    const text = await res.text().catch(() => "");
    throw new Error(`auto-submit failed: ${res.status} ${text}`);
  }
  return (await res.json()) as AutoSubmitResponse;
}

// ---- Claims-demo router ----
export const claimsdemo = {
  start: () =>
    request<{ claim_id: string; schema_set_id?: string; status?: string; files?: AutoSubmitFile[] }>(
      "POST",
      "/claimsdemo/claims/start",
    ),
  documents: (claimId: string) =>
    request<{ claim_id: string; documents: DemoDocument[] }>(
      "GET",
      `/claimsdemo/claims/${claimId}/documents`,
    ),
  classification: (claimId: string) =>
    request<{ claim_id: string; classification: DemoClassification[] }>(
      "GET",
      `/claimsdemo/claims/${claimId}/classification`,
    ),
  entities: (claimId: string) =>
    request<EntitiesPayload>("GET", `/claimsdemo/claims/${claimId}/entities`),
  fraudCheck: (claimId: string) =>
    request<FraudCheckPayload>("GET", `/claimsdemo/claims/${claimId}/fraud-check`),
  fraudAcks: (claimId: string) =>
    request<FraudAcksPayload>("GET", `/claimsdemo/claims/${claimId}/fraud-acks`),
  setFraudAck: (claimId: string, finding_id: string, acknowledged: boolean, note?: string) =>
    request<FraudAcksPayload>("POST", `/claimsdemo/claims/${claimId}/fraud-acks`, {
      finding_id,
      acknowledged,
      note,
    }),
  businessChecks: (claimId: string) =>
    request<{ claim_id: string; checks: BusinessCheck[] }>(
      "GET",
      `/claimsdemo/claims/${claimId}/business-checks`,
    ),
  getSummary: (claimId: string) =>
    request<SummaryPayload>("GET", `/claimsdemo/claims/${claimId}/summary`),
  putSummary: (claimId: string, payload: Record<string, unknown>) =>
    request<{ claim_id: string; saved: boolean; summary: Record<string, unknown> }>(
      "PUT",
      `/claimsdemo/claims/${claimId}/summary`,
      payload,
    ),
  recommendation: (claimId: string) =>
    request<RecommendationPayload>("POST", `/claimsdemo/claims/${claimId}/recommendation`),
  getDisposition: (claimId: string) =>
    request<DispositionPayload>("GET", `/claimsdemo/claims/${claimId}/disposition`),
  setDisposition: (
    claimId: string,
    decision: DispositionDecision,
    snapshot: DispositionSnapshot,
    note?: string,
  ) =>
    request<DispositionPayload>("POST", `/claimsdemo/claims/${claimId}/disposition`, {
      decision,
      snapshot,
      note,
    }),
  clearDisposition: (claimId: string) =>
    request<DispositionPayload>("DELETE", `/claimsdemo/claims/${claimId}/disposition`),
  audit: (claimId: string) =>
    request<AuditPayload>("GET", `/claimsdemo/claims/${claimId}/audit`),
  siuHandoff: (claimId: string, snapshot: DispositionSnapshot, note?: string) =>
    request<SIUHandoffResponse>("POST", `/claimsdemo/claims/${claimId}/siu`, { snapshot, note }),
  emailDraft: (claimId: string) =>
    request<EmailDraftPayload>("GET", `/claimsdemo/claims/${claimId}/email-draft`),
  emailSend: (claimId: string, payload: Record<string, unknown>) =>
    request<{ claim_id: string; queued: boolean; delivery_id: string }>(
      "POST",
      `/claimsdemo/claims/${claimId}/email-send`,
      payload,
    ),
  emailStatus: (claimId: string) =>
    request<{
      claim_id: string;
      queued: { delivery_id: string; queued_at: string; to: string; subject: string } | null;
    }>("GET", `/claimsdemo/claims/${claimId}/email-status`),
  fileBlobUrl: async (claimId: string, fileName: string): Promise<string> => {
    const token = await getToken();
    const headers: Record<string, string> = {};
    if (token) headers["Authorization"] = `Bearer ${token}`;
    const res = await fetch(
      `${API_BASE}/claimsdemo/claims/${encodeURIComponent(claimId)}/files/${encodeURIComponent(fileName)}/raw`,
      { headers },
    );
    if (!res.ok) throw new Error(`raw fetch failed: ${res.status}`);
    const blob = await res.blob();
    return URL.createObjectURL(blob);
  },
};

// ---- Content-processor passthrough ----
export const contentprocessor = {
  processed: (processId: string) =>
    request<Record<string, unknown>>(
      "GET",
      `/contentprocessor/processed/${encodeURIComponent(processId)}`,
    ),
};
