import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { ApiError } from '../api/client';
import { CreateHoldForm } from './CreateHoldForm';

describe('CreateHoldForm', () => {
  it('submits trimmed SKUs, numeric quantities and the hold time', async () => {
    const onCreate = vi.fn().mockResolvedValue(undefined);
    render(<CreateHoldForm knownSkus={['DEMO-1001']} onCreate={onCreate} />);

    await userEvent.type(screen.getByLabelText('SKU'), '  DEMO-1001 ');
    await userEvent.clear(screen.getByLabelText('Quantity'));
    await userEvent.type(screen.getByLabelText('Quantity'), '3');
    await userEvent.click(screen.getByRole('button', { name: 'Create hold' }));

    await waitFor(() => expect(onCreate).toHaveBeenCalledWith({ items: [{ sku: 'DEMO-1001', quantity: 3 }], ttlMinutes: 15 }));
  });

  it('blocks submission and explains what is wrong', async () => {
    const onCreate = vi.fn();
    render(<CreateHoldForm knownSkus={[]} onCreate={onCreate} />);

    await userEvent.click(screen.getByRole('button', { name: 'Create hold' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Line 1: enter a SKU.');
    expect(onCreate).not.toHaveBeenCalled();
  });

  it('rejects a hold time above the maximum', async () => {
    render(<CreateHoldForm knownSkus={[]} onCreate={vi.fn()} />);

    await userEvent.type(screen.getByLabelText('SKU'), 'A');
    await userEvent.clear(screen.getByLabelText('Hold for (minutes)'));
    await userEvent.type(screen.getByLabelText('Hold for (minutes)'), '90');
    await userEvent.click(screen.getByRole('button', { name: 'Create hold' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('between 1 and 60 minutes');
  });

  it('adds and removes lines', async () => {
    render(<CreateHoldForm knownSkus={[]} onCreate={vi.fn()} />);

    await userEvent.click(screen.getByRole('button', { name: 'Add another SKU' }));
    expect(screen.getByLabelText('SKU 2')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Remove line 2' }));
    expect(screen.queryByLabelText('SKU 2')).not.toBeInTheDocument();
  });

  it('shows the server problem with its trace id', async () => {
    const onCreate = vi.fn().mockRejectedValue(new ApiError(409, 'Insufficient stock', 'Not enough stock available for SKU A.', 'trace-9'));
    render(<CreateHoldForm knownSkus={[]} onCreate={onCreate} />);

    await userEvent.type(screen.getByLabelText('SKU'), 'A');
    await userEvent.click(screen.getByRole('button', { name: 'Create hold' }));

    expect(await screen.findByText(/Not enough stock available/)).toBeInTheDocument();
    expect(screen.getByText('trace-9')).toBeInTheDocument();
  });

  it('shows server-side field errors from a validation response', async () => {
    const onCreate = vi.fn().mockRejectedValue(new ApiError(400, 'Validation failed', '', undefined, { 'Items[0].Sku': ['Sku is required.'] }));
    render(<CreateHoldForm knownSkus={[]} onCreate={onCreate} />);

    await userEvent.type(screen.getByLabelText('SKU'), 'A');
    await userEvent.click(screen.getByRole('button', { name: 'Create hold' }));

    expect(await screen.findByText('Sku is required.')).toBeInTheDocument();
  });

  it('disables the button while the request is in flight', async () => {
    let finish: () => void = () => undefined;
    const onCreate = vi.fn(() => new Promise<void>((resolve) => { finish = resolve; }));
    render(<CreateHoldForm knownSkus={[]} onCreate={onCreate} />);

    await userEvent.type(screen.getByLabelText('SKU'), 'A');
    await userEvent.click(screen.getByRole('button', { name: 'Create hold' }));

    expect(await screen.findByRole('button', { name: 'Creating…' })).toBeDisabled();
    finish();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Create hold' })).toBeEnabled());
  });
});
