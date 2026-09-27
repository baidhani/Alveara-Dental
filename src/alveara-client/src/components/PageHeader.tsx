import type { ReactNode } from "react";
import "./PageHeader.css";

interface PageHeaderProps {
  title: string;
  description?: string;
  actions?: ReactNode;
}

/**
 * Standard page-header pattern: title + optional description on the left,
 * a predictable slot for the page's primary/secondary actions on the right.
 * Every module page should use this rather than inventing its own header.
 */
export function PageHeader({ title, description, actions }: PageHeaderProps) {
  return (
    <header className="alv-page-header">
      <div>
        <h1 className="alv-page-header__title">{title}</h1>
        {description && <p className="alv-page-header__description">{description}</p>}
      </div>
      {actions && <div className="alv-page-header__actions">{actions}</div>}
    </header>
  );
}
