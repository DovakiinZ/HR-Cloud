// Centralized API client for the live Azure backend.
// - Reads NEXT_PUBLIC_API_URL (falls back to the deployed App Service URL).
// - Attaches the Bearer token from storage.
// - Unwraps the backend ApiResponse envelope: { success, data, message, errors }.
// - Centrally handles 401 (session expired → logout/redirect), 403 (forbidden) and 5xx (server)
//   with toast messages. Other failures (400/404/409) throw ApiError for the caller to surface.

import { toast } from "sonner";
import { getAccessToken, clearSession } from "./auth-storage";

const DEFAULT_API_BASE_URL = "https://hrcloud-api-v4xd.azurewebsites.net";

// Resolve + sanitize the base URL. Env values can pick up a UTF-8 BOM (﻿),
// zero-width space (​) or stray whitespace (e.g. from CLI piping); strip them.
// Fall back to the default unless the result is a real http(s) URL.
function resolveBaseUrl(): string {
  const raw = (process.env.NEXT_PUBLIC_API_URL ?? "")
    .replace(/[﻿​\s]/g, "")
    .replace(/\/+$/, "");
  return /^https?:\/\//i.test(raw) ? raw : DEFAULT_API_BASE_URL;
}

export const API_BASE_URL = resolveBaseUrl();

export class ApiError extends Error {
  status: number;
  errors?: string[];
  /** Machine-readable error code from the envelope (e.g. PAYROLL_RUN_STALE, PAYROLL_PERIOD_CLOSED).
   *  Branch on this instead of parsing `message`. */
  code?: string;
  /** Structured payload the backend attaches to some errors (e.g. the blocking run for a closed period). */
  data?: unknown;
  constructor(message: string, status: number, errors?: string[], code?: string, data?: unknown) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.errors = errors;
    this.code = code;
    this.data = data;
  }
}

interface ApiEnvelope<T> {
  success: boolean;
  data: T | null;
  message: string | null;
  errors: string[] | null;
  code?: string | null;
}

interface RequestOptions {
  method?: "GET" | "POST" | "PUT" | "DELETE" | "PATCH";
  body?: unknown;
  signal?: AbortSignal;
}

export async function apiFetch<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = "GET", body, signal } = options;
  const token = getAccessToken();

  const headers: Record<string, string> = { "Content-Type": "application/json" };
  if (token) headers["Authorization"] = `Bearer ${token}`;

  let res: Response;
  try {
    res = await fetch(`${API_BASE_URL}${path}`, {
      method,
      headers,
      body: body !== undefined ? JSON.stringify(body) : undefined,
      signal,
    });
  } catch (e) {
    if ((e as Error)?.name === "AbortError") throw e;
    toast.error("تعذر الاتصال بالخادم");
    throw new ApiError("Network error", 0);
  }

  const text = await res.text();
  let envelope: ApiEnvelope<T> | null = null;
  if (text) {
    try {
      envelope = JSON.parse(text) as ApiEnvelope<T>;
    } catch {
      envelope = null;
    }
  }

  if (res.status === 401) {
    clearSession();
    toast.error("انتهت الجلسة. يرجى تسجيل الدخول من جديد");
    if (typeof window !== "undefined" && !window.location.pathname.startsWith("/login")) {
      window.location.href = "/login";
    }
    throw new ApiError(envelope?.message || "Unauthorized", 401);
  }

  if (res.status === 403) {
    toast.error("ليس لديك صلاحية لتنفيذ هذا الإجراء");
    throw new ApiError(envelope?.message || "Forbidden", 403, envelope?.errors ?? undefined, envelope?.code ?? undefined, envelope?.data ?? undefined);
  }

  if (res.status >= 500) {
    toast.error("حدث خطأ في الخادم. حاول مرة أخرى لاحقاً");
    throw new ApiError(envelope?.message || "Server error", res.status);
  }

  if (!res.ok || (envelope && !envelope.success)) {
    throw new ApiError(envelope?.message || "حدث خطأ غير متوقع", res.status, envelope?.errors ?? undefined, envelope?.code ?? undefined, envelope?.data ?? undefined);
  }

  return envelope ? (envelope.data as T) : (undefined as T);
}
