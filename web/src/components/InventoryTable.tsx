import type { InventoryPage } from '../api/types';
import { ErrorNotice } from './ErrorNotice';

interface InventoryTableProps {
  page: InventoryPage | undefined;
  loading: boolean;
  error: unknown;
  onRefresh: () => void;
}

export function InventoryTable({ page, loading, error, onRefresh }: InventoryTableProps) {
  return (
    <section aria-labelledby="inventory-heading" className="panel">
      <div className="panel-header">
        <h2 id="inventory-heading">Inventory</h2>
        <button type="button" onClick={onRefresh} disabled={loading}>
          {loading ? 'Refreshing…' : 'Refresh'}
        </button>
      </div>

      {error ? <ErrorNotice error={error} onRetry={onRefresh} /> : null}

      {!page && loading && <p role="status">Loading inventory…</p>}

      {page && page.items.length === 0 && <p className="muted">No inventory yet. Seed stock to get started.</p>}

      {page && page.items.length > 0 && (
        <div className="table-scroll">
          <table>
            <caption className="visually-hidden">Stock per SKU: available, held and total quantity</caption>
            <thead>
              <tr>
                <th scope="col">SKU</th>
                <th scope="col" className="num">Available</th>
                <th scope="col" className="num">Held</th>
                <th scope="col" className="num">Total</th>
              </tr>
            </thead>
            <tbody>
              {page.items.map((item) => (
                <tr key={item.sku} data-testid={`inventory-row-${item.sku}`}>
                  <th scope="row">{item.sku}</th>
                  <td className={`num ${item.available === 0 ? 'out' : ''}`}>{item.available}</td>
                  <td className="num">{item.held}</td>
                  <td className="num">{item.total}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {page && <p className="muted">Showing {page.items.length} of {page.totalCount} SKUs.</p>}
    </section>
  );
}
