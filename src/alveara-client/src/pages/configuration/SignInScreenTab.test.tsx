import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { SignInScreenTab } from "./SignInScreenTab";
import { AddBackgroundForm } from "./AddBackgroundForm";
import { PresentationAdministratorControl } from "../PresentationAdministratorControl";

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
afterEach(() => vi.unstubAllGlobals());

const admin = (over: Record<string, unknown> = {}) => ({
  backgroundKind: "Added", bundledKey: null, addedImageId: "img-1", displayMode: "Fill", footerNoticeText: null, showFooterNotice: true,
  deploymentDefaultNotice: "Please ensure you have taken appropriate precautions.",
  bundled: [
    { key: "teal-doodle", displayName: "Teal doodle", tileAllowed: true, suggestedDisplayMode: "Tile" },
    { key: "blue-operatory", displayName: "Blue operatory", tileAllowed: false, suggestedDisplayMode: "Tile" },
  ],
  added: [
    { id: "img-1", displayName: "Waiting room", width: 800, height: 600, suggestedDisplayMode: "Fill", isActive: true, addedAtUtc: "2026-11-12T00:00:00Z", rowVersion: "v1" },
    { id: "img-2", displayName: "Old lobby", width: 800, height: 600, suggestedDisplayMode: "Fill", isActive: false, addedAtUtc: "2026-11-11T00:00:00Z", rowVersion: "v2" },
  ],
  maxActiveAdded: 30, rowVersion: "s1", ...over,
});

function api(handlers: { put?: (body: Record<string, unknown>) => Response } = {}) {
  const calls: { url: string; init?: RequestInit }[] = [];
  vi.stubGlobal("fetch", vi.fn().mockImplementation((url: string, init?: RequestInit) => {
    calls.push({ url: String(url), init });
    if (String(url).includes("csrf")) return Promise.resolve(json({ token: "t" }));
    if (init?.method === "PUT" && handlers.put) return Promise.resolve(handlers.put(JSON.parse(String(init.body))));
    return Promise.resolve(json(admin()));
  }));
  return calls;
}

describe("SignInScreenTab (ALV-N020-C01)", () => {
  it("shows the catalog, the public-image warning, the Stretch distortion note, and the shown background cannot be made unavailable", async () => {
    api();
    render(<SignInScreenTab />);
    expect(await screen.findByRole("radio", { name: /Waiting room/ })).toBeTruthy();
    expect(screen.getAllByRole("note")[0].textContent).toMatch(/public.*before signing in.*patient information, faces/s);
    expect(screen.getByText(/May distort the image/)).toBeTruthy();
    expect(screen.getByText(/Recommended for photographs/)).toBeTruthy();
    expect((screen.getByRole("button", { name: "Make Waiting room unavailable" }) as HTMLButtonElement).disabled).toBe(true);
    expect(screen.getByRole("list", { name: "Unavailable backgrounds" }).textContent).toContain("Old lobby");
  });

  it("refuses Tile for a bundled background that cannot be tiled", async () => {
    api();
    render(<SignInScreenTab />);
    await userEvent.setup().click(await screen.findByRole("radio", { name: /Blue operatory/ }));
    expect((screen.getByRole("radio", { name: /^Tile —/ }) as HTMLInputElement).disabled).toBe(true);
    expect((screen.getByRole("radio", { name: /^Fill —/ }) as HTMLInputElement).checked).toBe(true);
  });

  it("saves the choice, a custom notice and the show setting with the version it read", async () => {
    let sent: Record<string, unknown> | null = null;
    api({ put: (body) => { sent = body; return json(admin({ ...body, rowVersion: "s2" })); } });
    const user = userEvent.setup();
    render(<SignInScreenTab />);
    await user.click(await screen.findByRole("radio", { name: /Teal doodle/ }));
    await user.click(screen.getByRole("radio", { name: /^Tile —/ }));
    await user.click(screen.getByRole("button", { name: "Write a custom notice" }));
    await user.clear(screen.getByRole("textbox", { name: /Notice text/ }));
    await user.type(screen.getByRole("textbox", { name: /Notice text/ }), "Authorized users only.");
    await user.click(screen.getByRole("checkbox", { name: "Show footer notice on login screen" }));
    await user.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() => expect(sent).not.toBeNull());
    expect(sent).toMatchObject({ backgroundKind: "Bundled", bundledKey: "teal-doodle", addedImageId: null, displayMode: "Tile", footerNoticeText: "Authorized users only.",
      useDefaultNotice: false, showFooterNotice: false, rowVersion: "s1" });
    expect((await screen.findByRole("status")).textContent).toBe("Sign-in screen saved.");
  });

  it("says plainly when someone else changed the settings", async () => {
    api({ put: () => json({ title: "Conflict", error: "concurrency_conflict" }, 409) });
    const user = userEvent.setup();
    render(<SignInScreenTab />);
    await user.click(await screen.findByRole("radio", { name: /Teal doodle/ }));
    await user.click(screen.getByRole("button", { name: "Save" }));
    expect((await screen.findByRole("alert")).textContent).toMatch(/Someone else changed these settings/);
  });
});

describe("AddBackgroundForm (ALV-N020-C01)", () => {
  it("will not upload until the public-image warning is acknowledged, then sends the file, name, mode and acknowledgement", async () => {
    const calls = api();
    const onAdded = vi.fn();
    const user = userEvent.setup();
    const { container } = render(<AddBackgroundForm onAdded={onAdded} onCancel={vi.fn()} />);
    await user.upload(container.querySelector("input[type=file]") as HTMLInputElement, new File([new Uint8Array([1, 2, 3])], "lobby.png", { type: "image/png" }));
    await user.type(screen.getByRole("textbox", { name: "Name" }), "Lobby");
    await user.click(screen.getByRole("button", { name: "Add image" }));
    expect(screen.getByRole("alert").textContent).toMatch(/Confirm that the image is public/);
    expect(calls.some((c) => c.url.includes("/backgrounds"))).toBe(false);

    await user.click(screen.getByRole("checkbox", { name: /contains no patient or other confidential information/ }));
    await user.click(screen.getByRole("button", { name: "Add image" }));
    await waitFor(() => expect(onAdded).toHaveBeenCalled());
    const upload = calls.find((c) => c.url.endsWith("/api/config/login-presentation/backgrounds"))!;
    const form = upload.init!.body as FormData;
    expect([form.get("displayName"), form.get("suggestedDisplayMode"), form.get("publicAcknowledged"), (form.get("file") as File).name]).toEqual(["Lobby", "Fill", "true", "lobby.png"]);
  });
});

describe("PresentationAdministratorControl (ALV-N020-C01)", () => {
  it("reads and changes the account's sign-in presentation permission", async () => {
    let granted = false;
    vi.stubGlobal("fetch", vi.fn().mockImplementation((url: string, init?: RequestInit) => {
      if (String(url).includes("csrf")) return Promise.resolve(json({ token: "t" }));
      if (init?.method === "PUT") { granted = JSON.parse(String(init.body)).granted; return Promise.resolve(json({ granted, changed: true })); }
      return Promise.resolve(json({ granted }));
    }));
    const user = userEvent.setup();
    render(<PresentationAdministratorControl accountId="a1" username="dana" />);
    expect((await screen.findByText(/is not a sign-in presentation administrator/)).textContent).toBeTruthy();
    await user.click(screen.getByRole("button", { name: "Make sign-in presentation administrator" }));
    expect(await screen.findByText("dana can now change the sign-in screen.")).toBeTruthy();
    expect(granted).toBe(true);
  });
});
