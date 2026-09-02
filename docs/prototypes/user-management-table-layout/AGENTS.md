# Prototype Instructions

Run the local server yourself and open the preview in the browser available to this environment. Do not give the user server-start instructions when you can run it.

Before making substantial visual changes, use the Product Design plugin's `get-context` skill when the visual source is unclear or no longer matches the current goal. When the user gives durable prototype-specific design feedback, preferences, or decisions, record them in `AGENTS.md`.

When implementing from a selected generated mock, treat that image as the source of truth for layout, component anatomy, density, spacing, color, typography, visible content, and hierarchy.

Build app UI in `src/`. Keep `.openai/hosting.json`, `worker/index.js`, `scripts/prepare-sites-build.mjs`, and `tests/sites-worker.test.mjs` intact so the same local prototype can be handed to Sites. Before a Sites handoff, run `npm run build` and `npm run test:sites`; the build must leave `dist/client/index.html`, `dist/server/index.js`, and `dist/.openai/hosting.json`.

## Accepted prototype constraints

- Business actions sit above the table toolbar.
- Toolbar left: query-mode toggle, sort, quick search, download, print.
- Toolbar right: clear query, refresh, table fullscreen, column settings, row settings.
- Header filters render as a separate `tr` below titles; date filtering uses one range control.
- The table footer always shows the current selected-row state for both single- and multi-select tables.
- The lower-right selection summary shows only the selected count and clear action.
- A grouping tool follows sort and supports one field or multiple fields in selection order.
- Do not connect this prototype to production frontend code before explicit approval.
