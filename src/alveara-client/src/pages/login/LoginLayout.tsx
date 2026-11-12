import { useEffect, useState } from "react";
import type { ReactNode } from "react";
import logo from "../../assets/login/dentalkeep-logo.png";
import { getPublicLoginPresentation, PUBLIC_PRESENTATION_TIMEOUT_MS } from "../../services/loginPresentationApi";
import type { PublicLoginPresentation } from "../../services/loginPresentationApi";
import { backgroundCss, bundledBackground, DEFAULT_BACKGROUND } from "./loginBackgrounds";
import "./LoginLayout.css";

const DEFAULT_PRESENTATION: PublicLoginPresentation = { background: { kind: "bundled", key: DEFAULT_BACKGROUND.key }, displayMode: "Tile", footerNotice: null };

/**
 * ALV-N020-C01: the shared sign-in layout (sign-in, MFA challenge, password reset). The page renders at once with the bundled default background and no
 * footer; the configured presentation is applied when it arrives. Sign-in never waits for it: a slow, failed or unexpected answer leaves the default in place,
 * and an added image that fails to load falls back to the default. The background is decorative (aria-hidden, CSS only) and never sits behind text: the solid
 * centre panel holds the logo, the page's own content and the footer notice, which is rendered as plain text (a text node, never HTML).
 */
export function LoginLayout({ children }: { children: ReactNode }) {
  const [presentation, setPresentation] = useState<PublicLoginPresentation>(DEFAULT_PRESENTATION);
  const [failedUrl, setFailedUrl] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), PUBLIC_PRESENTATION_TIMEOUT_MS);
    getPublicLoginPresentation(controller.signal)
      .then((p) => { if (p) setPresentation(p); })
      .catch(() => { /* network error, timeout or unmount: the bundled default stays (logged by nothing: it is the expected offline state) */ })
      .finally(() => clearTimeout(timer));
    return () => { clearTimeout(timer); controller.abort(); };
  }, []);

  const background = presentation.background;
  const addedUrl = background.kind === "added" ? background.url : null;
  useEffect(() => {
    if (!addedUrl) return;
    const probe = new Image();
    probe.onerror = () => setFailedUrl(addedUrl);                                                        // only this URL is known to be broken
    probe.src = addedUrl;
    return () => { probe.onerror = null; };
  }, [addedUrl]);
  const addedFailed = addedUrl !== null && failedUrl === addedUrl;

  const style = (() => {
    if (addedUrl && !addedFailed) return backgroundCss(addedUrl, presentation.displayMode, DEFAULT_BACKGROUND.fallbackColor);
    const bundled = background.kind === "bundled" ? bundledBackground(background.key) : undefined;
    if (!bundled) return backgroundCss(DEFAULT_BACKGROUND.url, "Tile", DEFAULT_BACKGROUND.fallbackColor);
    return backgroundCss(bundled.url, presentation.displayMode, bundled.fallbackColor);
  })();
  const effectiveMode = addedUrl && !addedFailed ? presentation.displayMode : background.kind === "bundled" && bundledBackground(background.key) ? presentation.displayMode : "Tile";

  return (
    <div className="alv-signin">
      <div className="alv-signin__backdrop" aria-hidden="true" data-testid="signin-backdrop" data-display-mode={effectiveMode} style={style} />
      <div className="alv-signin__panel">
        {/* Landmarks: the logo and the page's own content are the main region; the notice is the page footer (contentinfo). */}
        <main className="alv-signin__main">
          <img className="alv-signin__logo" src={logo} alt="DentalKeep" width={280} height={126} />
          <div className="alv-signin__content">{children}</div>
        </main>
        {presentation.footerNotice && (
          <footer className="alv-signin__notice">
            <p>{presentation.footerNotice}</p>
          </footer>
        )}
      </div>
    </div>
  );
}
