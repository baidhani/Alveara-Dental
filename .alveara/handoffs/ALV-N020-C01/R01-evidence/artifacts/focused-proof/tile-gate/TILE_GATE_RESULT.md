# ALV-N020-C01 tile-readiness gate (review BRIEFING-R02 correction 4)

Date: 2026-11-12. Real browser (Playwright Chromium, built client, mocked API), e2e/login-presentation.spec.ts, every bundled background in Tile mode at 1920x1080, 1280x800 and 375x812 (15 full-page screenshots in this folder). Bundled repeat unit 624 x 624 px, so repeat boundaries fall at x and y = 624, 1248, 1872.

Inspection: the full screenshots, plus seam-zoom-horizontal.png (2x zoom of y 544-704 around the y=624 boundary, all five) and seam-zoom-vertical.png (2x zoom of x 1824-1920 around the x=1872 boundary, all five).

| Background (bundled key) | Source file (owner, Desktop\Epic) | Verdict | Note |
|---|---|---|---|
| teal-doodle (default) | dentalKeep-background-tile-00.png | Tile ready | shapes continue across both boundaries |
| blue-doodle | dentalKeep-background-tile-01.png | Tile ready | continuous |
| blue-operatory | dentalKeep-background-tile-02.png | Tile ready | the measured top/bottom difference (10.1 vs 4.1) is content, not a visible seam: the cabinet continues across y=624 |
| violet-operatory | dentalKeep-background-tile-03.png | Tile ready | continuous |
| neon-doodle | dentalKeep-background-tile-04.png (the owner's reworked neon tile, 2026-11-12 14:30; edges 0.3/0.3 vs 1.9) | Tile ready | strokes near the boundary end inside the tile with rounded ends; no cut line |

Result: all five Tile ready; no reprocessing; tileAllowed = true for all five (LoginBackgroundCatalog).
