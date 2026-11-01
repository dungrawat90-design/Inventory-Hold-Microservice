import { useState } from 'react';
import type { Hold } from '../api/types';
import { useNow } from '../hooks/useNow';
import { ErrorNotice } from './ErrorNotice';

interface HoldCardProps {
  hold: Hold;
  onRelease: (id: string) => Promise<void>;
}

export function formatRemaining(expiresAt: string, now: number): string {
  const seconds = Math.max(0, Math.floor((new Date(expiresAt).getTime() - now) / 1000));
  const minutes = Math.floor(seconds / 60);
  return `${minutes}:${String(seconds % 60).padStart(2, '0')}`;
}

export function HoldCard({ hold, onRelease }: HoldCardProps) {
  const now = useNow();
  const [releasing, setReleasing] = useState(false);
  const [error, setError] = useState<unknown>();
  const active = hold.status === 'Active';
  const pastExpiry = new Date(hold.expiresAt).getTime() <= now;

  const release = async () => {
    setReleasing(true);
    setError(undefined);
    try {
      await onRelease(hold.id);
    } catch (caught) {
      setError(caught);
    } finally {
      setReleasing(false);
    }
  };

  return (
    <li className="hold" data-testid={`hold-${hold.id}`}>
      <div className="hold-head">
        <code title={hold.id}>{hold.id.slice(0, 8)}</code>
        <span className={`badge badge-${hold.status.toLowerCase()}`}>{hold.status}</span>
      </div>
      <ul className="hold-items">
        {hold.items.map((item) => (
          <li key={item.sku}>
            {item.quantity} × {item.sku}
          </li>
        ))}
      </ul>
      {active && (
        <p className="muted">
          {pastExpiry ? 'Expiring…' : <>Expires in <time dateTime={hold.expiresAt}>{formatRemaining(hold.expiresAt, now)}</time></>}
        </p>
      )}
      {!active && hold.releasedAt && <p className="muted">{hold.status} at {new Date(hold.releasedAt).toLocaleTimeString()}</p>}
      {error ? <ErrorNotice error={error} /> : null}
      {active && (
        <button type="button" onClick={release} disabled={releasing}>
          {releasing ? 'Releasing…' : 'Release hold'}
        </button>
      )}
    </li>
  );
}
