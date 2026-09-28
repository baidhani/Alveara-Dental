import { PageHeader } from "../components/PageHeader";
import { ButtonLink } from "../components/Button";

export function NotFoundPage() {
  return (
    <>
      <PageHeader title="Page not found" description="There is no module at this address." />
      <ButtonLink to="/" variant="primary">
        Back to Dashboard
      </ButtonLink>
    </>
  );
}
