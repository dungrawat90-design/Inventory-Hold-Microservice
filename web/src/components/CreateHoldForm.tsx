import { useId, useState, type FormEvent } from 'react';
import { ApiError } from '../api/client';
import type { CreateHoldRequest } from '../api/types';
import { ErrorNotice } from './ErrorNotice';

interface Line {
  key: number;
  sku: string;
  quantity: string;
}

interface CreateHoldFormProps {
  knownSkus: string[];
  onCreate: (request: CreateHoldRequest) => Promise<void>;
}

const DEFAULT_TTL = 15;
const MAX_TTL = 60;

export function CreateHoldForm({ knownSkus, onCreate }: CreateHoldFormProps) {
  const listId = useId();
  const [lines, setLines] = useState<Line[]>([{ key: 1, sku: '', quantity: '1' }]);
  const [nextKey, setNextKey] = useState(2);
  const [ttl, setTtl] = useState(String(DEFAULT_TTL));
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<unknown>();
  const [problems, setProblems] = useState<string[]>([]);

  const update = (key: number, patch: Partial<Line>) =>
    setLines((current) => current.map((line) => (line.key === key ? { ...line, ...patch } : line)));

  const validate = (): string[] => {
    const found: string[] = [];
    lines.forEach((line, index) => {
      const quantity = Number(line.quantity);
      if (!line.sku.trim()) found.push(`Line ${index + 1}: enter a SKU.`);
      if (!Number.isInteger(quantity) || quantity < 1) found.push(`Line ${index + 1}: quantity must be a whole number of at least 1.`);
    });
    const minutes = Number(ttl);
    if (!Number.isInteger(minutes) || minutes < 1 || minutes > MAX_TTL) {
      found.push(`Hold time must be between 1 and ${MAX_TTL} minutes.`);
    }
    return found;
  };

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    const found = validate();
    setProblems(found);
    setError(undefined);
    if (found.length > 0) return;

    setSubmitting(true);
    try {
      await onCreate({
        items: lines.map((line) => ({ sku: line.sku.trim(), quantity: Number(line.quantity) })),
        ttlMinutes: Number(ttl),
      });
      setLines([{ key: nextKey, sku: '', quantity: '1' }]);
      setNextKey((k) => k + 1);
    } catch (caught) {
      if (caught instanceof ApiError && Object.keys(caught.fieldErrors).length > 0) {
        setProblems(Object.values(caught.fieldErrors).flat());
      } else {
        setError(caught);
      }
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <section aria-labelledby="create-heading" className="panel">
      <h2 id="create-heading">Create a hold</h2>
      <form onSubmit={submit} noValidate>
        <datalist id={listId}>
          {knownSkus.map((sku) => (
            <option key={sku} value={sku} />
          ))}
        </datalist>

        {lines.map((line, index) => (
          <div className="line" key={line.key}>
            <label>
              <span>SKU{lines.length > 1 ? ` ${index + 1}` : ''}</span>
              <input
                value={line.sku}
                list={listId}
                autoComplete="off"
                onChange={(e) => update(line.key, { sku: e.target.value })}
              />
            </label>
            <label>
              <span>Quantity{lines.length > 1 ? ` ${index + 1}` : ''}</span>
              <input
                type="number"
                min={1}
                inputMode="numeric"
                value={line.quantity}
                onChange={(e) => update(line.key, { quantity: e.target.value })}
              />
            </label>
            {lines.length > 1 && (
              <button
                type="button"
                className="secondary"
                aria-label={`Remove line ${index + 1}`}
                onClick={() => setLines((current) => current.filter((l) => l.key !== line.key))}
              >
                Remove
              </button>
            )}
          </div>
        ))}

        <div className="actions">
          <button
            type="button"
            className="secondary"
            onClick={() => {
              setLines((current) => [...current, { key: nextKey, sku: '', quantity: '1' }]);
              setNextKey((k) => k + 1);
            }}
          >
            Add another SKU
          </button>
          <label className="inline">
            <span>Hold for (minutes)</span>
            <input type="number" min={1} max={MAX_TTL} value={ttl} onChange={(e) => setTtl(e.target.value)} />
          </label>
        </div>

        {problems.length > 0 && (
          <ul role="alert" className="notice notice-error problems">
            {problems.map((problem) => (
              <li key={problem}>{problem}</li>
            ))}
          </ul>
        )}
        {error ? <ErrorNotice error={error} /> : null}

        <button type="submit" disabled={submitting}>
          {submitting ? 'Creating…' : 'Create hold'}
        </button>
      </form>
    </section>
  );
}
