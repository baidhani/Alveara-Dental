import { ErrorState } from "./StatePatterns";

/**
 * ALV-001-C01's "clear permission-denied presentation" requirement. Deliberately distinct wording
 * from a generic error state — the caller is authenticated and the request worked, they simply
 * lack the specific permission, which is a different situation from "something went wrong" and
 * should never be presented as if the page itself failed.
 */
export function PermissionDenied({ requiredPermission }: { requiredPermission?: string }) {
  return (
    <ErrorState
      title="You don't have permission to view this"
      description={
        requiredPermission
          ? `This requires the "${requiredPermission}" permission, which your role does not currently hold. Contact an administrator if you believe this is incorrect.`
          : "Your role does not hold the permission this page requires. Contact an administrator if you believe this is incorrect."
      }
    />
  );
}
