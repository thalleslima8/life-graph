import createClient, { type Middleware } from "openapi-fetch";
import type { paths } from "./schema.gen";

/**
 * The single HTTP client of the SPA (FE-013, DA-097). Same origin, authenticated by the
 * HttpOnly session cookie (DA-010); the CSRF token lives only in this module's memory,
 * never in storage or logs.
 */

const CSRF_HEADER = "X-CSRF-TOKEN";
const CSRF_TOKEN_PATH = "/api/csrf-token";
const CSRF_INVALID_CODE = "csrf_token_invalid";
const SAFE_METHODS = new Set(["GET", "HEAD", "OPTIONS"]);

// Same origin as the page. An absolute base keeps Request construction valid outside a browser too.
const baseUrl = window.location.origin;

let csrfToken: string | undefined;
let csrfTokenRequest: Promise<string> | undefined;
let unauthorizedHandler: (() => void) | undefined;

export class ApiError extends Error {
  readonly status: number;
  readonly code: string | undefined;
  readonly fieldErrors: Record<string, string[]>;

  constructor(status: number, code: string | undefined, message: string, fieldErrors: Record<string, string[]> = {}) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.code = code;
    this.fieldErrors = fieldErrors;
  }
}

export function isApiError(error: unknown, code?: string): error is ApiError {
  return error instanceof ApiError && (code === undefined || error.code === code);
}

/** Forgets the CSRF token; the next unsafe request reads a new one. Call it whenever the identity changes. */
export function resetCsrfToken(): void {
  csrfToken = undefined;
  csrfTokenRequest = undefined;
}

/** Runs when an authenticated call answers 401 through {@link unwrap}; the session feature decides what that means. */
export function setUnauthorizedHandler(handler: (() => void) | undefined): void {
  unauthorizedHandler = handler;
}

type ApiResult<T> = { data?: T; error?: unknown; response: Response };

/** The data of a successful call, or an {@link ApiError} built from the Problem Details (API-050). */
export function unwrap<T>(result: ApiResult<T>): T {
  const { response } = result;
  if (response.ok) {
    return result.data as T;
  }

  if (response.status === 401) {
    unauthorizedHandler?.();
  }

  throw toApiError(response.status, result.error);
}

function toApiError(status: number, body: unknown): ApiError {
  const problem = isRecord(body) ? body : {};
  const code = typeof problem.code === "string" ? problem.code : undefined;
  const title = typeof problem.title === "string" ? problem.title : `HTTP ${status}`;
  const fieldErrors = isRecord(problem.errors) ? (problem.errors as Record<string, string[]>) : {};
  return new ApiError(status, code, title, fieldErrors);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function getCsrfToken(): Promise<string> {
  if (csrfToken) {
    return Promise.resolve(csrfToken);
  }

  // Parallel unsafe requests share one token request.
  csrfTokenRequest ??= fetchCsrfToken().then(
    (token) => {
      csrfToken = token;
      return token;
    },
    (error: unknown) => {
      csrfTokenRequest = undefined;
      throw error;
    },
  );
  return csrfTokenRequest;
}

async function fetchCsrfToken(): Promise<string> {
  const response = await globalThis.fetch(new Request(`${baseUrl}${CSRF_TOKEN_PATH}`, { credentials: "same-origin" }));
  if (!response.ok) {
    throw new ApiError(response.status, undefined, "Could not read the CSRF token");
  }

  const body: unknown = await response.json();
  if (!isRecord(body) || typeof body.token !== "string") {
    throw new ApiError(response.status, undefined, "Malformed CSRF token response");
  }

  return body.token;
}

async function isCsrfRejection(response: Response): Promise<boolean> {
  if (response.status !== 400 && response.status !== 403) {
    return false;
  }

  try {
    const body: unknown = await response.clone().json();
    return isRecord(body) && body.code === CSRF_INVALID_CODE;
  } catch {
    return false;
  }
}

// The body of a sent request is consumed, so a pristine copy is kept for the one replay.
const replayableRequests = new WeakMap<Request, Request>();

const csrfMiddleware: Middleware = {
  async onRequest({ request }) {
    if (SAFE_METHODS.has(request.method)) {
      return undefined;
    }

    request.headers.set(CSRF_HEADER, await getCsrfToken());
    replayableRequests.set(request, request.clone());
    return request;
  },
  async onResponse({ request, response }) {
    const replay = replayableRequests.get(request);
    if (!replay || !(await isCsrfRejection(response))) {
      return undefined;
    }

    // The token went stale (identity changed elsewhere, server restarted): read a new one and retry once.
    resetCsrfToken();
    replay.headers.set(CSRF_HEADER, await getCsrfToken());
    return globalThis.fetch(replay);
  },
};

export const api = createClient<paths>({
  baseUrl,
  credentials: "same-origin",
  // Resolved per call, so test doubles installed after import are honored.
  fetch: (request) => globalThis.fetch(request),
});

api.use(csrfMiddleware);
