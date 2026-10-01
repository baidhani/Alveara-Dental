import { describe, it, expect, vi, afterEach } from "vitest";
import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ConfigEntityPanel } from "./ConfigEntityPanel";
import { validateFields } from "./validateFields";
import type { ConfigField } from "./ConfigEntityPanel";
import { NotificationProvider } from "./Notification";
import { ApiError } from "../services/authApi";

interface Row {
  id: string;
  isActive: boolean;
  rowVersion: string;
  name: string;
  minutes: number;
}

const fields: ConfigField[] = [
  { name: "name", label: "Name", type: "text", required: true, maxLength: 20 },
  { name: "minutes", label: "Minutes", type: "number", required: true, min: 5, max: 480, step: 5 },
];

const row = (over: Partial<Row> = {}): Row => ({ id: "r1", isActive: true, rowVersion: "v1", name: "Exam", minutes: 30, ...over });

function setup(over: Partial<React.ComponentProps<typeof ConfigEntityPanel<Row>>> = {}) {
  const props = {
    noun: "appointment type",
    nounPlural: "appointment types",
    fields,
    columns: [{ header: "Name", render: (r: Row) => r.name }],
    load: vi.fn().mockResolvedValue([row()]),
    create: vi.fn().mockResolvedValue({}),
    update: vi.fn().mockResolvedValue({}),
    setActive: vi.fn().mockResolvedValue({}),
    toValues: (r: Row) => ({ name: r.name, minutes: String(r.minutes) }),
    searchText: (r: Row) => r.name,
    ...over,
  };
  render(
    <NotificationProvider>
      <ConfigEntityPanel<Row> {...props} />
    </NotificationProvider>
  );
  return props;
}

afterEach(() => vi.restoreAllMocks());

describe("ConfigEntityPanel (ALV-N003 reusable configuration pattern)", () => {
  it("shows loading, then the loaded rows with their status", async () => {
    setup();
    expect(screen.getByText(/Loading appointment types/)).toBeInTheDocument();
    expect(await screen.findByText("Exam")).toBeInTheDocument();
    expect(screen.getByText("Active")).toBeInTheDocument();
  });

  it("shows a truthful empty state when nothing is configured", async () => {
    setup({ load: vi.fn().mockResolvedValue([]) });
    expect(await screen.findByText("No appointment types yet")).toBeInTheDocument();
  });

  it("shows permission-denied (not a generic error) on a 403, and a retryable error otherwise", async () => {
    const load = vi.fn().mockRejectedValueOnce(new ApiError(403, "permission_denied", "no"));
    setup({ load, permission: "ManagePracticeConfiguration" });
    expect(await screen.findByText(/don't have permission/i)).toBeInTheDocument();
  });

  it("offers retry after a load failure", async () => {
    const load = vi.fn().mockRejectedValueOnce(new Error("down")).mockResolvedValue([row()]);
    setup({ load });
    await userEvent.click(await screen.findByRole("button", { name: "Retry" }));
    expect(await screen.findByText("Exam")).toBeInTheDocument();
  });

  it("filters by search text and reloads including inactive records when asked", async () => {
    const load = vi.fn().mockImplementation(async (includeInactive: boolean) =>
      includeInactive ? [row(), row({ id: "r2", name: "Old type", isActive: false })] : [row()]
    );
    setup({ load });
    await screen.findByText("Exam");
    expect(screen.queryByText("Old type")).not.toBeInTheDocument();

    await userEvent.click(screen.getByLabelText("Show inactive"));
    expect(await screen.findByText("Old type")).toBeInTheDocument();
    expect(screen.getByText("Inactive")).toBeInTheDocument();
    expect(load).toHaveBeenLastCalledWith(true);

    await userEvent.type(screen.getByPlaceholderText("Search appointment types"), "old");
    expect(screen.queryByText("Exam")).not.toBeInTheDocument();
    await userEvent.clear(screen.getByPlaceholderText("Search appointment types"));
    await userEvent.type(screen.getByPlaceholderText("Search appointment types"), "zzz");
    expect(await screen.findByText(/No appointment types match "zzz"/)).toBeInTheDocument();
  });

  it("validates inline and does not call the server when a field is invalid", async () => {
    const props = setup();
    await screen.findByText("Exam");
    await userEvent.click(screen.getByRole("button", { name: "Add appointment type" }));
    await userEvent.type(screen.getByLabelText("Minutes"), "7");
    await userEvent.click(document.querySelector("form button[type=submit]")!);

    expect(await screen.findByText("Name is required.")).toBeInTheDocument();
    expect(screen.getByText("Minutes must be in steps of 5.")).toBeInTheDocument();
    expect(props.create).not.toHaveBeenCalled();
  });

  it("creates a record, reloads, and closes the form on success", async () => {
    const props = setup();
    await screen.findByText("Exam");
    await userEvent.click(screen.getByRole("button", { name: "Add appointment type" }));
    await userEvent.type(screen.getByLabelText("Name"), "Cleaning");
    await userEvent.type(screen.getByLabelText("Minutes"), "45");
    await userEvent.click(document.querySelector("form button[type=submit]")!);

    await waitFor(() => expect(props.create).toHaveBeenCalledWith({ name: "Cleaning", minutes: "45" }));
    await waitFor(() => expect(screen.queryByRole("form", { name: "Add appointment type" })).not.toBeInTheDocument());
    expect(props.load).toHaveBeenCalledTimes(2);
  });

  it("shows the server's message inline when a save is rejected, and keeps the form open", async () => {
    const create = vi.fn().mockRejectedValue(new ApiError(409, "appointment_type_name_taken", "An appointment type with that name already exists."));
    setup({ create });
    await screen.findByText("Exam");
    await userEvent.click(screen.getByRole("button", { name: "Add appointment type" }));
    await userEvent.type(screen.getByLabelText("Name"), "Exam");
    await userEvent.type(screen.getByLabelText("Minutes"), "30");
    await userEvent.click(document.querySelector("form button[type=submit]")!);

    expect(await screen.findByText("An appointment type with that name already exists.")).toBeInTheDocument();
    expect(screen.getByRole("form", { name: "Add appointment type" })).toBeInTheDocument();
  });

  it("protects unsaved edits: cancel asks first, and declining keeps the form and its text", async () => {
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    setup();
    await screen.findByText("Exam");
    await userEvent.click(screen.getByRole("button", { name: "Add appointment type" }));
    await userEvent.type(screen.getByLabelText("Name"), "Half typed");
    expect(screen.getByText("Unsaved changes")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(confirm).toHaveBeenCalled();
    expect(screen.getByLabelText("Name")).toHaveValue("Half typed");

    confirm.mockReturnValue(true);
    await userEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(screen.queryByLabelText("Name")).not.toBeInTheDocument();
  });

  it("cancels without a prompt when nothing was changed", async () => {
    const confirm = vi.spyOn(window, "confirm");
    setup();
    await userEvent.click(await screen.findByRole("button", { name: "Edit Exam" }));
    await userEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(confirm).not.toHaveBeenCalled();
  });

  it("reports whether the open form is dirty to its host", async () => {
    const onDirtyChange = vi.fn();
    setup({ onDirtyChange });
    await userEvent.click(await screen.findByRole("button", { name: "Edit Exam" }));
    expect(onDirtyChange).toHaveBeenLastCalledWith(false);
    await userEvent.type(screen.getByLabelText("Name"), "x");
    expect(onDirtyChange).toHaveBeenLastCalledWith(true);
  });

  it("presents a stale edit with the shared conflict banner and reloads the current version on request", async () => {
    const conflictBody = { error: "concurrency_conflict", entityType: "AppointmentType", entityId: "r1" };
    const update = vi.fn().mockRejectedValue(new ApiError(409, "concurrency_conflict", "conflict", conflictBody));
    const load = vi.fn().mockResolvedValueOnce([row()]).mockResolvedValue([row({ rowVersion: "v2", name: "Exam (changed elsewhere)" })]);
    setup({ update, load });

    await userEvent.click(await screen.findByRole("button", { name: "Edit Exam" }));
    await userEvent.type(screen.getByLabelText("Name"), "!");
    await userEvent.click(document.querySelector("form button[type=submit]")!);

    expect(await screen.findByText("Someone else changed this while you were editing")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Reload current version" }));

    await waitFor(() => expect(screen.getByLabelText("Name")).toHaveValue("Exam (changed elsewhere)"));
    expect(update).toHaveBeenCalledTimes(1); // reloading must never re-submit the stale edit
    await waitFor(() => expect(screen.queryByText("Someone else changed this while you were editing")).not.toBeInTheDocument());
  });

  it("inactivates and reactivates instead of deleting - there is no delete action at all", async () => {
    const props = setup();
    await userEvent.click(await screen.findByRole("button", { name: "Inactivate Exam" }));
    await waitFor(() => expect(props.setActive).toHaveBeenCalledWith(expect.objectContaining({ id: "r1" }), false));
    expect(screen.queryByRole("button", { name: /delete/i })).not.toBeInTheDocument();
  });

  it("blocks creating, with the reason, when a prerequisite is missing", async () => {
    setup({ createDisabledReason: "Create an active location first." });
    expect(await screen.findByText("Create an active location first.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Add appointment type" })).toBeDisabled();
  });
});

describe("ConfigEntityPanel conflict recovery (ALV-N003 R02)", () => {
  const conflictBody = { error: "concurrency_conflict", entityType: "AppointmentType", entityId: "r1" };
  const conflictingUpdate = () => vi.fn().mockRejectedValue(new ApiError(409, "concurrency_conflict", "conflict", conflictBody));

  async function reachConflict(props: ReturnType<typeof setup>) {
    await userEvent.click(await screen.findByRole("button", { name: "Edit Exam" }));
    await userEvent.type(screen.getByLabelText("Name"), "!");
    await userEvent.click(document.querySelector("form button[type=submit]")!);
    await screen.findByText("Someone else changed this while you were editing");
    return props;
  }

  it("keeps the draft, the form and the conflict when the reload request fails, and recovers on retry", async () => {
    const load = vi.fn().mockResolvedValueOnce([row()]).mockRejectedValueOnce(new Error("network down")).mockResolvedValue([row({ rowVersion: "v2", name: "Exam (current)" })]);
    await reachConflict(setup({ update: conflictingUpdate(), load }));

    await userEvent.click(screen.getByRole("button", { name: "Reload current version" }));
    expect(await screen.findByText(/Could not reload the appointment type/)).toBeInTheDocument();
    expect(screen.getByLabelText("Name")).toHaveValue("Exam!");                                          // draft intact
    expect(screen.getByText("Someone else changed this while you were editing")).toBeInTheDocument();   // conflict intact

    await userEvent.click(screen.getByRole("button", { name: "Reload current version" }));              // retry
    await waitFor(() => expect(screen.getByLabelText("Name")).toHaveValue("Exam (current)"));
    expect(screen.queryByText(/Could not reload/)).not.toBeInTheDocument();
  });

  it("finds a record that was inactivated by the other editor instead of treating it as gone", async () => {
    const load = vi.fn().mockImplementation(async (includeInactive: boolean) =>
      load.mock.calls.length === 1 ? [row()] : includeInactive ? [row({ isActive: false, rowVersion: "v2", name: "Exam (retired)" })] : []
    );
    await reachConflict(setup({ update: conflictingUpdate(), load }));

    await userEvent.click(screen.getByRole("button", { name: "Reload current version" }));
    await waitFor(() => expect(screen.getByLabelText("Name")).toHaveValue("Exam (retired)"));
  });

  it("closes the form only when a successful read proves the record no longer exists", async () => {
    const load = vi.fn().mockResolvedValueOnce([row()]).mockResolvedValue([]);
    await reachConflict(setup({ update: conflictingUpdate(), load }));

    await userEvent.click(screen.getByRole("button", { name: "Reload current version" }));
    await waitFor(() => expect(screen.queryByLabelText("Name")).not.toBeInTheDocument());
  });
});

describe("ConfigEntityPanel completion protection (ALV-N003 R03)", () => {
  it("enforces a pending-write policy: inputs and other rows' Edit buttons are disabled while a save is in flight", async () => {
    let finish!: (v: unknown) => void;
    const update = vi.fn().mockReturnValue(new Promise((resolve) => (finish = resolve)));
    const load = vi.fn().mockResolvedValue([row(), row({ id: "r2", name: "Cleaning" })]);
    setup({ update, load });

    await userEvent.click(await screen.findByRole("button", { name: "Edit Exam" }));
    await userEvent.type(screen.getByLabelText("Name"), "!");
    await userEvent.click(document.querySelector("form button[type=submit]")!);

    await waitFor(() => expect(screen.getByLabelText("Name")).toBeDisabled());
    expect(screen.getByRole("button", { name: "Edit Cleaning" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Add appointment type" })).toBeDisabled();

    await act(async () => finish({}));
    await waitFor(() => expect(screen.queryByRole("form", { name: "Edit appointment type" })).not.toBeInTheDocument());
    expect(screen.getByRole("button", { name: "Edit Cleaning" })).toBeEnabled();
  });

  it("ignores a delayed conflict reload that completes after the user closed the form", async () => {
    const conflictBody = { error: "concurrency_conflict", entityType: "AppointmentType", entityId: "r1" };
    const update = vi.fn().mockRejectedValue(new ApiError(409, "concurrency_conflict", "conflict", conflictBody));
    let release!: (rows: Row[]) => void;
    const load = vi
      .fn()
      .mockResolvedValueOnce([row()])
      .mockReturnValueOnce(new Promise<Row[]>((resolve) => (release = resolve)));
    vi.spyOn(window, "confirm").mockReturnValue(true);
    setup({ update, load });

    await userEvent.click(await screen.findByRole("button", { name: "Edit Exam" }));
    await userEvent.type(screen.getByLabelText("Name"), "!");
    await userEvent.click(document.querySelector("form button[type=submit]")!);
    await userEvent.click(await screen.findByRole("button", { name: "Reload current version" })); // reload held
    await userEvent.click(screen.getByRole("button", { name: "Cancel" }));                          // user abandons the session
    expect(screen.queryByLabelText("Name")).not.toBeInTheDocument();

    await act(async () => release([row({ rowVersion: "v2", name: "Exam (changed elsewhere)" })]));

    expect(screen.queryByLabelText("Name")).not.toBeInTheDocument(); // the abandoned session's reload did not reopen the form
  });
});

describe("validateFields", () => {
  it("accepts boundary values and rejects out-of-range ones", () => {
    expect(validateFields(fields, { name: "ok", minutes: "5" }, true)).toEqual({});
    expect(validateFields(fields, { name: "ok", minutes: "480" }, true)).toEqual({});
    expect(validateFields(fields, { name: "ok", minutes: "4" }, true).minutes).toMatch(/at least 5/);
    expect(validateFields(fields, { name: "ok", minutes: "485" }, true).minutes).toMatch(/at most 480/);
    expect(validateFields(fields, { name: "ok", minutes: "12.5" }, true).minutes).toMatch(/whole number/);
    expect(validateFields(fields, { name: "x".repeat(21), minutes: "30" }, true).name).toMatch(/20 characters/);
  });

  it("skips create-only fields when editing", () => {
    const withCreateOnly: ConfigField[] = [{ name: "owner", label: "Owner", type: "select", required: true, createOnly: true }, ...fields];
    expect(validateFields(withCreateOnly, { name: "ok", minutes: "30" }, false)).toEqual({});
    expect(validateFields(withCreateOnly, { name: "ok", minutes: "30" }, true).owner).toBe("Owner is required.");
  });
});
