import type { ReactNode } from "react";
import "./StatePatterns.css";

/**
 * Standard loading / empty / error state patterns. These exist so a real
 * feature story can drop in a truthful state immediately instead of
 * inventing its own "still loading" spinner or blank screen. None of these
 * ever imply data exists that hasn't actually been fetched.
 */

export function LoadingState({ label = "Loading…" }: { label?: string }) {
  return (
    <div className="alv-state alv-state--loading" role="status" aria-live="polite">
      <span className="alv-spinner" aria-hidden="true" />
      <span>{label}</span>
    </div>
  );
}

export function EmptyState({ title, description }: { title: string; description?: string }) {
  return (
    <div className="alv-state alv-state--empty">
      <p className="alv-state__title">{title}</p>
      {description && <p className="alv-state__description">{description}</p>}
    </div>
  );
}

export function ErrorState({
  title = "Something went wrong",
  description,
  action,
}: {
  title?: string;
  description?: string;
  action?: ReactNode;
}) {
  return (
    <div className="alv-state alv-state--error" role="alert">
      <p className="alv-state__title">{title}</p>
      {description && <p className="alv-state__description">{description}</p>}
      {action}
    </div>
  );
}
