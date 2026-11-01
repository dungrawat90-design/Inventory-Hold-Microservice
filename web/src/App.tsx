import { useCallback, useEffect, useState } from 'react';
import { api } from './api/client';
import type { CreateHoldRequest, Hold, InventoryPage } from './api/types';
import { CreateHoldForm } from './components/CreateHoldForm';
import { HoldCard } from './components/HoldCard';
import { InventoryTable } from './components/InventoryTable';

const STORAGE_KEY = 'inventory-hold:ids';
const REFRESH_MS = 10_000;

function loadIds(): string[] {
  try {
    const parsed: unknown = JSON.parse(window.localStorage.getItem(STORAGE_KEY) ?? '[]');
    return Array.isArray(parsed) ? parsed.filter((id): id is string => typeof id === 'string') : [];
  } catch {
    return [];
  }
}

function saveIds(ids: string[]) {
  try {
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(ids.slice(0, 20)));
  } catch {
    // Storage can be blocked; the page still works without remembering holds.
  }
}

export function App() {
  const [inventory, setInventory] = useState<InventoryPage>();
  const [inventoryLoading, setInventoryLoading] = useState(true);
  const [inventoryError, setInventoryError] = useState<unknown>();
  const [holds, setHolds] = useState<Hold[]>([]);

  const refreshInventory = useCallback(async () => {
    setInventoryLoading(true);
    try {
      setInventory(await api.getInventory(0, 200));
      setInventoryError(undefined);
    } catch (caught) {
      setInventoryError(caught);
    } finally {
      setInventoryLoading(false);
    }
  }, []);

  const refreshHolds = useCallback(async () => {
    const ids = loadIds();
    const results = await Promise.allSettled(ids.map((id) => api.getHold(id)));
    const found = results.flatMap((result) => (result.status === 'fulfilled' ? [result.value] : []));
    setHolds(found);
    saveIds(found.map((hold) => hold.id));
  }, []);

  useEffect(() => {
    void refreshInventory();
    void refreshHolds();
    const timer = window.setInterval(() => {
      void refreshInventory();
      void refreshHolds();
    }, REFRESH_MS);
    return () => window.clearInterval(timer);
  }, [refreshInventory, refreshHolds]);

  const createHold = async (request: CreateHoldRequest) => {
    const hold = await api.createHold(request);
    saveIds([hold.id, ...loadIds()]);
    setHolds((current) => [hold, ...current]);
    void refreshInventory();
  };

  const releaseHold = async (id: string) => {
    const released = await api.releaseHold(id);
    setHolds((current) => current.map((hold) => (hold.id === id ? released : hold)));
    void refreshInventory();
  };

  return (
    <main>
      <h1>Inventory holds</h1>
      <p className="muted">Reserve stock for a short time while a customer completes checkout.</p>
      <div className="layout">
        <InventoryTable page={inventory} loading={inventoryLoading} error={inventoryError} onRefresh={() => void refreshInventory()} />
        <div className="stack">
          <CreateHoldForm knownSkus={inventory?.items.map((item) => item.sku) ?? []} onCreate={createHold} />
          <section aria-labelledby="holds-heading" className="panel">
            <h2 id="holds-heading">Your holds</h2>
            {holds.length === 0 ? (
              <p className="muted">No holds yet. Create one to see it here.</p>
            ) : (
              <ul className="holds">
                {holds.map((hold) => (
                  <HoldCard key={hold.id} hold={hold} onRelease={releaseHold} />
                ))}
              </ul>
            )}
          </section>
        </div>
      </div>
    </main>
  );
}
