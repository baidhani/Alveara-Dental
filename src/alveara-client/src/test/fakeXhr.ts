import { vi } from "vitest";

/**
 * A scripted XMLHttpRequest for tests of uploads (jsdom has no network). Every request is handed to the handler as a plain call, the way a stubbed `fetch` is, and its Response becomes the XHR's status and body.
 * Progress events are sent first (`progress`: the fractions to report, default none); `hold` lets a test keep the request open until it releases it; aborting before the response ends the request with `onabort`.
 */
export interface XhrCall { url: string; method: string; headers: Record<string, string>; form: FormData | null }
export type XhrHandler = (call: XhrCall) => Response | Promise<Response>;

export interface FakeXhrOptions { progress?: number[]; hold?: Promise<void>; networkError?: boolean }

export function installFakeXhr(handler: XhrHandler, options: FakeXhrOptions = {}) {
  const calls: XhrCall[] = [];
  const instances: FakeXhr[] = [];

  class FakeXhr {
    upload: { onprogress: ((e: { lengthComputable: boolean; loaded: number; total: number }) => void) | null } = { onprogress: null };
    onload: (() => void) | null = null;
    onerror: (() => void) | null = null;
    onabort: (() => void) | null = null;
    ontimeout: (() => void) | null = null;
    status = 0;
    responseText = "";
    withCredentials = false;
    timeout = 0;
    aborted = false;
    private call: XhrCall = { url: "", method: "", headers: {}, form: null };

    constructor() { instances.push(this); }
    open(method: string, url: string) { this.call = { ...this.call, method, url }; }
    setRequestHeader(name: string, value: string) { this.call.headers[name] = value; }
    abort() { this.aborted = true; this.onabort?.(); }

    send(body: FormData) {
      this.call.form = body;
      calls.push(this.call);
      void (async () => {
        for (const fraction of options.progress ?? []) this.upload.onprogress?.({ lengthComputable: true, loaded: fraction * 1000, total: 1000 });
        if (options.hold) await options.hold;
        if (this.aborted) return;
        if (options.networkError) { this.onerror?.(); return; }
        const response = await handler(this.call);
        if (this.aborted) return;
        this.status = response.status;
        this.responseText = await response.text();
        this.onload?.();
      })();
    }
  }

  vi.stubGlobal("XMLHttpRequest", FakeXhr);
  return { calls, instances };
}
