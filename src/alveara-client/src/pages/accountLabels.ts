import type { AdminUserSummary } from "../services/authApi";
/**
 * ALV-N030 (brief section 9): what Security Administration says about an account besides its role. The Deployment Administrator and the Practice
 * Owner are named; accounts from before logins were tied to staff are labelled legacy, so an administrator can review or disable them.
 */
export function accountLabels(user: AdminUserSummary, practiceOwnerName: string | null): string[] {
  const labels: string[] = [];
  if (user.accountKind === "DeploymentAdministrator") labels.push("Deployment Administrator");
  else if (user.role === "Unassigned") labels.push("Legacy account (unassigned)");
  else if (!user.linkedStaffDisplayName) labels.push("Legacy account without a staff profile");
  if (practiceOwnerName && user.linkedStaffDisplayName === practiceOwnerName) labels.push("Practice Owner");
  if (user.mustChangePassword) labels.push("Must change password");
  return labels;
}
