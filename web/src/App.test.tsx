import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from './App';
import { api, ApiError } from './api/client';
import type { Hold, InventoryPage } from './api/types';

vi.mock('./api/client', async (importOriginal) => {
  const original = await importOriginal<typeof import('./api/client')>();
  return {
    ...original,
    api: { getInventory: vi.fn(), createHold: vi.fn(), getHold: vi.fn(), releaseHold: vi.fn() },
  };
});

const page = (available: number, held: number): InventoryPage => ({
  items: [{ sku: 'DEMO-1001', available, held, total: available + held }],
  skip: 0,
  take: 200,
  totalCount: 1,
});

const activeHold: Hold = {
  id: 'hold-0001-aaaa',
  status: 'Active',
  items: [{ sku: 'DEMO-1001', quantity: 2 }],
  createdAt: '2099-01-01T00:00:00Z',
  expiresAt: '2099-01-01T00:15:00Z',
  releasedAt: null,
};

describe('App', () => {
  beforeEach(() => {
    window.localStorage.clear();
    vi.mocked(api.getInventory).mockResolvedValue(page(10, 0));
    vi.mocked(api.getHold).mockRejectedValue(new Error('none'));
  });

  it('shows inventory with available, held and total', async () => {
    render(<App />);

    const row = await screen.findByTestId('inventory-row-DEMO-1001');
    expect(within(row).getAllByRole('cell').map((cell) => cell.textContent)).toEqual(['10', '0', '10']);
  });

  it('tells the user when inventory cannot be loaded and lets them retry', async () => {
    vi.mocked(api.getInventory).mockRejectedValueOnce(new ApiError(500, 'Unexpected error', 'Something went wrong.', 'trace-3'));
    render(<App />);

    expect(await screen.findByText('trace-3')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByTestId('inventory-row-DEMO-1001')).toBeInTheDocument();
  });

  it('creates a hold, lists it and refreshes the stock', async () => {
    vi.mocked(api.createHold).mockResolvedValue(activeHold);
    vi.mocked(api.getInventory).mockResolvedValueOnce(page(10, 0)).mockResolvedValue(page(8, 2));
    render(<App />);
    await screen.findByTestId('inventory-row-DEMO-1001');

    await userEvent.type(screen.getByLabelText('SKU'), 'DEMO-1001');
    await userEvent.clear(screen.getByLabelText('Quantity'));
    await userEvent.type(screen.getByLabelText('Quantity'), '2');
    await userEvent.click(screen.getByRole('button', { name: 'Create hold' }));

    expect(await screen.findByTestId('hold-hold-0001-aaaa')).toBeInTheDocument();
    await waitFor(() => expect(within(screen.getByTestId('inventory-row-DEMO-1001')).getAllByRole('cell')[0]).toHaveTextContent('8'));
  });

  it('releases a hold and shows it as released', async () => {
    vi.mocked(api.createHold).mockResolvedValue(activeHold);
    vi.mocked(api.releaseHold).mockResolvedValue({ ...activeHold, status: 'Released', releasedAt: '2099-01-01T00:05:00Z' });
    render(<App />);
    await screen.findByTestId('inventory-row-DEMO-1001');

    await userEvent.type(screen.getByLabelText('SKU'), 'DEMO-1001');
    await userEvent.click(screen.getByRole('button', { name: 'Create hold' }));
    await userEvent.click(await screen.findByRole('button', { name: 'Release hold' }));

    await waitFor(() => expect(within(screen.getByTestId('hold-hold-0001-aaaa')).getByText('Released')).toBeInTheDocument());
    expect(api.releaseHold).toHaveBeenCalledWith('hold-0001-aaaa');
  });

  it('shows an empty state when there are no holds', async () => {
    render(<App />);

    expect(await screen.findByText(/No holds yet/)).toBeInTheDocument();
  });
});
