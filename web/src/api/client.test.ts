import { afterEach, describe, expect, it, vi } from 'vitest';
import { api, ApiError } from './client';

function respond(status: number, body: unknown) {
  return vi.fn().mockResolvedValue(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }));
}

afterEach(() => vi.unstubAllGlobals());

describe('api client', () => {
  it('returns the parsed body on success', async () => {
    vi.stubGlobal('fetch', respond(200, { items: [], skip: 0, take: 50, totalCount: 0 }));

    await expect(api.getInventory()).resolves.toMatchObject({ totalCount: 0 });
  });

  it('sends the hold as JSON with a POST', async () => {
    const fetchMock = respond(201, { id: 'h1' });
    vi.stubGlobal('fetch', fetchMock);

    await api.createHold({ items: [{ sku: 'A', quantity: 1 }], ttlMinutes: 15 });

    const [path, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(path).toBe('/api/holds');
    expect(init.method).toBe('POST');
    expect(JSON.parse(init.body as string)).toEqual({ items: [{ sku: 'A', quantity: 1 }], ttlMinutes: 15 });
  });

  it('turns ProblemDetails into an ApiError that keeps the trace id', async () => {
    vi.stubGlobal('fetch', respond(409, { title: 'Insufficient stock', detail: 'Not enough stock available for SKU A.', traceId: 'trace-1' }));

    const error = await api.createHold({ items: [{ sku: 'A', quantity: 9 }] }).catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ status: 409, title: 'Insufficient stock', traceId: 'trace-1' });
  });

  it('keeps field errors from a validation problem', async () => {
    vi.stubGlobal('fetch', respond(400, { title: 'Validation failed', errors: { 'Items[0].Quantity': ['Quantity must be between 1 and 10000.'] } }));

    const error = (await api.createHold({ items: [] }).catch((e: unknown) => e)) as ApiError;

    expect(error.fieldErrors['Items[0].Quantity']).toEqual(['Quantity must be between 1 and 10000.']);
  });

  it('reports a network failure as an ApiError with status 0', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('offline')));

    await expect(api.getInventory()).rejects.toMatchObject({ status: 0, title: 'Network error' });
  });

  it('survives an error response that is not JSON', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('Bad gateway', { status: 502 })));

    await expect(api.getInventory()).rejects.toMatchObject({ status: 502, title: 'Request failed' });
  });
});
