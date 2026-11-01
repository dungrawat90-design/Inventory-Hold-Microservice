import { ApiError } from '../api/client';

interface ErrorNoticeProps {
  error: unknown;
  onRetry?: () => void;
}

/** Shows what failed and what the user can do next, keeping the server trace id visible for support. */
export function ErrorNotice({ error, onRetry }: ErrorNoticeProps) {
  const apiError = error instanceof ApiError ? error : undefined;
  const message = apiError?.message ?? 'Something unexpected went wrong. Try again.';

  return (
    <div role="alert" className="notice notice-error">
      <p>
        <strong>{apiError?.title ?? 'Unexpected error'}.</strong> {message}
      </p>
      {apiError?.traceId && (
        <p className="muted">
          Trace id: <code>{apiError.traceId}</code>
        </p>
      )}
      {onRetry && (
        <button type="button" onClick={onRetry}>
          Try again
        </button>
      )}
    </div>
  );
}
