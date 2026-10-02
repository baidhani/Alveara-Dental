import { Link, useInRouterContext } from "react-router-dom";
import type { ReactNode } from "react";

/**
 * A link that works with or without a router above it: inside the app it is a client-side <Link>; rendered on its own
 * (e.g. a page component under test with no router) it degrades to a plain anchor. Lets a page offer navigation without
 * making a router a hard requirement of rendering it.
 */
export function SafeLink({ to, className, children }: { to: string; className?: string; children: ReactNode }) {
  const inRouter = useInRouterContext();
  return inRouter ? (
    <Link to={to} className={className}>
      {children}
    </Link>
  ) : (
    <a href={to} className={className}>
      {children}
    </a>
  );
}
