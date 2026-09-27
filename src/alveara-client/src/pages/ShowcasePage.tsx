import { useState } from "react";
import { PageHeader } from "../components/PageHeader";
import { Button } from "../components/Button";
import { FormField } from "../components/FormField";
import { LoadingState, EmptyState, ErrorState } from "../components/StatePatterns";
import { useNotifications } from "../components/Notification";
import "./ShowcasePage.css";

/**
 * Small pattern showcase for the controls that actually exist. Deliberately
 * does not pre-build components no story needs yet.
 */
export function ShowcasePage() {
  const [emailError, setEmailError] = useState<string | undefined>();
  const { notify } = useNotifications();

  return (
    <>
      <PageHeader
        title="Component Showcase"
        description="The reusable controls built for the shell so far. Grows only when a real story needs a new pattern."
      />

      <section className="showcase-section">
        <h2>Buttons</h2>
        <div className="showcase-row">
          <Button variant="primary">Primary</Button>
          <Button variant="secondary">Secondary</Button>
          <Button variant="danger">Danger</Button>
          <Button variant="primary" disabled>
            Disabled
          </Button>
        </div>
      </section>

      <section className="showcase-section">
        <h2>Form field</h2>
        <FormField
          label="Email address"
          hint="We'll never share this."
          error={emailError}
          onBlur={(e) => setEmailError(e.target.value.includes("@") ? undefined : "Enter a valid email address.")}
        />
      </section>

      <section className="showcase-section">
        <h2>Loading / empty / error states</h2>
        <div className="showcase-stack">
          <LoadingState />
          <EmptyState title="No results" description="Nothing matches your filters yet." />
          <ErrorState description="The request failed. Try again." />
        </div>
      </section>

      <section className="showcase-section">
        <h2>Notifications</h2>
        <div className="showcase-row">
          <Button onClick={() => notify("info", "This is an informational message.")}>Info</Button>
          <Button onClick={() => notify("success", "Saved successfully.")}>Success</Button>
          <Button onClick={() => notify("warning", "Double-check this before continuing.")}>Warning</Button>
          <Button variant="danger" onClick={() => notify("danger", "Something failed.")}>
            Danger
          </Button>
        </div>
      </section>
    </>
  );
}
