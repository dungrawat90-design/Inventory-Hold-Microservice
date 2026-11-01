import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../api/client';
import type { Hold } from '../api/types';
import { formatRemaining, HoldCard } from './HoldCard';

const NOW = new Date('2026-11-01T09:00:00Z');

const hold = (overrides: Partial<Hold> = {}): Hold => ({
  id: 'abcdef1234567890',
  status: 'Active',
  items: [{ sku: 'DEMO-1001', quantity: 2 }],
  createdAt: '2026-11-01T09:00:00Z',
  expiresAt: '2026-11-01T09:15:00Z',
  releasedAt: null,
  ...overrides,
});

describe('formatRemaining', () => {
  it('formats minutes and zero-padded seconds', () => {
    expect(formatRemaining('2026-11-01T09:15:00Z', NOW.getTime())).toBe('15:00');
    expect(formatRemaining('2026-11-01T09:01:05Z', NOW.getTime())).toBe('1:05');
  });

  it('never goes below zero', () => {
    expect(formatRemaining('2026-11-01T08:00:00Z', NOW.getTime())).toBe('0:00');
  });
});

describe('HoldCard', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date', 'setInterval', 'clearInterval'] });
    vi.setSystemTime(NOW);
  });

  afterEach(() => vi.useRealTimers());

  it('shows items, status and the countdown for an active hold', () => {
    render(<HoldCard hold={hold()} onRelease={vi.fn()} />);

    expect(screen.getByText('2 × DEMO-1001')).toBeInTheDocument();
    expect(screen.getByText('Active')).toBeInTheDocument();
    expect(screen.getByText('15:00')).toBeInTheDocument();
  });

  it('releases the hold when the button is pressed', async () => {
    const onRelease = vi.fn().mockResolvedValue(undefined);
    render(<HoldCard hold={hold()} onRelease={onRelease} />);

    await userEvent.setup({ advanceTimers: vi.advanceTimersByTime }).click(screen.getByRole('button', { name: 'Release hold' }));

    await waitFor(() => expect(onRelease).toHaveBeenCalledWith('abcdef1234567890'));
  });

  it('offers no release button once the hold is released', () => {
    render(<HoldCard hold={hold({ status: 'Released', releasedAt: '2026-11-01T09:05:00Z' })} onRelease={vi.fn()} />);

    expect(screen.queryByRole('button', { name: 'Release hold' })).not.toBeInTheDocument();
    expect(screen.getByText('Released')).toBeInTheDocument();
  });

  it('says it is expiring when past its time but not yet marked expired', () => {
    render(<HoldCard hold={hold({ expiresAt: '2026-11-01T08:59:00Z' })} onRelease={vi.fn()} />);

    expect(screen.getByText('Expiring…')).toBeInTheDocument();
  });

  it('shows the failure and its trace id when release fails', async () => {
    const onRelease = vi.fn().mockRejectedValue(new ApiError(500, 'Unexpected error', 'Something went wrong.', 'trace-7'));
    render(<HoldCard hold={hold()} onRelease={onRelease} />);

    await userEvent.setup({ advanceTimers: vi.advanceTimersByTime }).click(screen.getByRole('button', { name: 'Release hold' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Something went wrong.');
    expect(screen.getByText('trace-7')).toBeInTheDocument();
  });
});
