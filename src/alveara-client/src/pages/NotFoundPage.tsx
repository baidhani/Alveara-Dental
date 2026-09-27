import { Link } from "react-router-dom";
import { PageHeader } from "../components/PageHeader";
import { Button } from "../components/Button";

export function NotFoundPage() {
  return (
    <>
      <PageHeader title="Page not found" description="There is no module at this address." />
      <Link to="/">
        <Button variant="primary">Back to Dashboard</Button>
      </Link>
    </>
  );
}
