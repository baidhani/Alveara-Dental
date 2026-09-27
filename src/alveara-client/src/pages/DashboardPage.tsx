import { PageHeader } from "../components/PageHeader";
import { EmptyState } from "../components/StatePatterns";

/**
 * Placeholder primary workspace region. No feature story has shipped yet,
 * so this shows an honest empty state rather than sample dashboard data.
 */
export function DashboardPage() {
  return (
    <>
      <PageHeader
        title="Dashboard"
        description="The primary workspace region. Real content arrives with later feature stories."
      />
      <EmptyState
        title="Nothing to show yet"
        description="No feature modules have been built on top of this shell yet. This is the correct state for ALV-N001 — not a bug."
      />
    </>
  );
}
