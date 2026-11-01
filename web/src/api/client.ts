import type { CreateHoldRequest, Hold, InventoryPage } from './types';

/** An error answered by the API as ProblemDetails, with the trace id kept so a report can be matched to the log. */
export class ApiError extends Error {
  readonly status: number;
  readonly title: string;
  readonly traceId: string | undefined;
  readonly fieldErrors: Record<string, string[]>;

  constructor(status: number, title: string, detail: string, traceId?: string, fieldErrors: Record<string, string[]> = {}) {
    super(detail);
    this.name = 'ApiError';
    this.status = status;
    this.title = title;
    this.traceId = traceId;
    this.fieldErrors = fieldErrors;
  }
}

interface ProblemBody {
  title?: string;
  detail?: string;
  traceId?: string;
  errors?: Record<string, string[]>;
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response;
  try {
    response = await fetch(path, {
      ...init,
      headers: { Accept: 'application/json', ...(init?.body ? { 'Content-Type': 'application/json' } : {}) },
    });
  } catch {
    throw new ApiError(0, 'Network error', 'The server could not be reached. Check your connection and try again.');
  }

  if (response.ok) {
    return (await response.json()) as T;
  }

  const problem = (await response.json().catch(() => ({}))) as ProblemBody;
  throw new ApiError(
    response.status,
    problem.title ?? 'Request failed',
    problem.detail ?? `The request failed with status ${response.status}.`,
    problem.traceId,
    problem.errors,
  );
}

export const api = {
  getInventory: (skip = 0, take = 50) => request<InventoryPage>(`/api/inventory?skip=${skip}&take=${take}`),
  createHold: (body: CreateHoldRequest) => request<Hold>('/api/holds', { method: 'POST', body: JSON.stringify(body) }),
  getHold: (id: string) => request<Hold>(`/api/holds/${encodeURIComponent(id)}`),
  releaseHold: (id: string) => request<Hold>(`/api/holds/${encodeURIComponent(id)}`, { method: 'DELETE' }),
};
