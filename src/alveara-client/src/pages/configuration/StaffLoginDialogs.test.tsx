import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { NotificationProvider } from "../../components/Notification";
import { ConfirmStaffInactivation, UnlinkLoginAction } from "./StaffLoginDialogs";
import type { StaffRecord } from "../../services/configApi";

afterEach(() => vi.unstubAllGlobals());

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
const row = (over: Partial<StaffRecord> = {}): StaffRecord =>
  ({ id: "s1", displayName: "Dr. Dana", jobTitle: "Dentist", userAccountId: "a1", isActive: true, rowVersion: "v1", ...over }) as StaffRecord;

describe("ConfirmStaffInactivation (ALV-N020, F-021)", () => {
  it("says the login will be disabled and not re-enabled by reactivation, focuses 'Keep active', and reports the answer", async () => {
    const onAnswer = vi.fn();
    render(<ConfirmStaffInactivation row={row()} loginLabel="dentist1" onAnswer={onAnswer} />);

    const dialog = screen.getByRole("alertdialog", { name: "Inactivate Dr. Dana?" });
    expect(dialog).toHaveTextContent(/also disables their login \(dentist1\)/);
    expect(dialog).toHaveTextContent(/does not enable the login again/);
    expect(screen.getByRole("button", { name: "Keep active" })).toHaveFocus();

    await userEvent.setup().click(screen.getByRole("button", { name: "Inactivate and disable login" }));
    expect(onAnswer).toHaveBeenCalledWith(true);
  });
});

describe("UnlinkLoginAction (ALV-N020, F-021)", () => {
  it("is not offered for an active profile or a profile without a login", () => {
    const { container, rerender } = render(<NotificationProvider><UnlinkLoginAction row={row({ isActive: true })} onDone={vi.fn()} /></NotificationProvider>);
    expect(container.querySelector("button")).toBeNull();
    rerender(<NotificationProvider><UnlinkLoginAction row={row({ isActive: false, userAccountId: null })} onDone={vi.fn()} /></NotificationProvider>);
    expect(container.querySelector("button")).toBeNull();
  });

  it("requires a reason, then sends it with the version and reloads", async () => {
    const fetchMock = vi.fn().mockImplementation((url: string) =>
      Promise.resolve(String(url).includes("csrf") ? json({ token: "t" }) : json({ ...row({ isActive: false, userAccountId: null }) })));
    vi.stubGlobal("fetch", fetchMock);
    const onDone = vi.fn().mockResolvedValue(undefined);
    render(<NotificationProvider><UnlinkLoginAction row={row({ isActive: false })} onDone={onDone} /></NotificationProvider>);
    const user = userEvent.setup();

    await user.click(screen.getByRole("button", { name: "Unlink login from Dr. Dana" }));
    await user.click(screen.getByRole("button", { name: "Unlink" }));
    expect(screen.getByRole("alert")).toHaveTextContent(/say why/i);
    expect(fetchMock.mock.calls.some(([u]) => String(u).includes("unlink-account"))).toBe(false);

    await user.type(screen.getByLabelText("Reason"), "Left the practice");
    await user.click(screen.getByRole("button", { name: "Unlink" }));
    await waitFor(() => expect(onDone).toHaveBeenCalled());
    const call = fetchMock.mock.calls.find(([u]) => String(u).includes("/api/config/staff/s1/unlink-account"))!;
    expect(JSON.parse(String((call[1] as RequestInit).body))).toEqual({ reason: "Left the practice", rowVersion: "v1" });
  });
});
