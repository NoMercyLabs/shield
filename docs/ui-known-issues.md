# Shield Web UI — known issues (2026-05-16 audit)

This document captures the state of `src/Shield.Web/src/` after a contract audit
against the live API at `http://localhost:8842`. It lists what was fixed in the
UI in this pass, and what still needs server-side work before the SPA can be
considered feature-complete.

## What was fixed in the UI

All fixes are scoped to `src/Shield.Web/src/`. The server contract is treated
as authoritative — wire types and component bindings were rewritten to match
what the API actually returns today.

### Contract / type model

- `src/Shield.Web/src/types/api.ts` — rewritten end-to-end. Enums are now
  numeric (matching `System.Text.Json`'s default `JsonStringEnumConverter`-off
  behaviour) with companion `<Enum>Names` lookup tables. Response shapes for
  `MeResponse`, `DashboardResponse`, `SourceDetailResponse`, `FindingResponse`,
  `FindingDetailResponse`, `AlertChannel`, and `FeedStatusResponse` match the
  current C# contracts. Old aliases that no longer exist on the wire
  (`countsBySeverity`, `totalSources`, flat `Source` from the detail endpoint,
  `sourceName`/`packageName`/`packageVersion`/`ecosystem` on `Finding`,
  `references[]` on `Advisory`, `singleUser`/`role` on `Me`) were removed.

### Cross-cutting helpers

- `src/Shield.Web/src/lib/format.ts:10` — `severityClass`, `severityRank` and
  the new `severityName` helper now operate on the numeric `Severity` enum.
- `src/Shield.Web/src/lib/format.ts:34` — added `parseJsonArray<T>()` to safely
  read the `referencesJson` / `affectedRangesJson` string fields the server
  stores as JSON-encoded arrays.
- `src/Shield.Web/src/components/SeverityBadge.vue:7` — renders the severity
  name via `SeverityNames`, since the prop is now a number.

### Auth

- `src/Shield.Web/src/stores/auth.ts:5` — `Me` shape uses `userId` / `roles[]`
  / `singleUserMode` (not `id` / `role` / `singleUser`). `useAuth()` exposes
  `isAdmin` derived from `roles`.
- `src/Shield.Web/src/stores/auth.ts:25` — `login()` posts a typed
  `LoginRequest` (matches `twoFactorCode` / `rememberMe` field names from
  `AuthContracts.cs`), interprets `LoginResponse.succeeded`, and refreshes
  `/auth/me` to populate the authenticated user (the controller's login
  response does not carry the identity).
- `src/Shield.Web/src/components/Topbar.vue:19` — uses `singleUserMode`.

### Dashboard

- `src/Shield.Web/src/views/DashboardView.vue:13` — reads `openCounts` (lower-
  case `low/medium/high/critical`) and adds `sourcesHealthy` / `sourcesStale`
  cards (server returns both, frontend previously rendered neither). Recent
  findings render `dedupKey` + `sourceId` because `FindingResponse` does not
  carry `packageName` / `sourceName` (see "remaining gaps" below).

### Sources

- `src/Shield.Web/src/views/SourcesView.vue:17` — `type` v-model is numeric
  (`SourceType.GithubRepo` etc.); form now sends the required `scanInterval`
  + `enabled` fields (previously omitted, which 400'd on every save).
- `src/Shield.Web/src/views/SourcesView.vue:109` — type column resolved via
  `SourceTypeNames` instead of trying to render the raw int.
- `src/Shield.Web/src/queries/sources.ts:17` — `useSourceQuery` now returns
  `SourceDetail` (`{ source, lastSnapshot }`) to match `SourcesController.Get`.
- `src/Shield.Web/src/views/SourceDetailView.vue:16` — unwraps `source` /
  `lastSnapshot`; renders snapshot metadata when present.

### Findings

- `src/Shield.Web/src/queries/findings.ts:12` — `useFindingQuery` returns
  `FindingDetail` (`{ finding, advisory, item }`). Suppress mutation now
  always sends a JSON object (`{ reason }`), since the controller binds
  `[FromBody] SuppressFindingRequest` and requires a body.
- `src/Shield.Web/src/views/FindingsView.vue:11` — severity / state / ecosystem
  filters bind numeric enum values via `v-model.number`, so the URL query
  string carries the integers the controller's `[FromQuery]` binder accepts.
  Added pagination footer (server already returned `total` / `page` /
  `pageSize`, UI ignored them).
- `src/Shield.Web/src/views/FindingsView.vue:90` — table column renders
  `state` via `FindingStateNames` and `dedupKey` (the only stable identifier
  on `FindingResponse` until the server joins `InventoryItem` / `Source`).
- `src/Shield.Web/src/views/FindingDetailView.vue:23` — re-shaped to unwrap
  `finding` / `advisory` / `item`; references list driven by
  `parseJsonArray(advisory.referencesJson)`; ecosystem displayed from the
  inventory item when available, otherwise from the advisory.

### Channels

- `src/Shield.Web/src/views/ChannelsView.vue:8` — `type` and `minSeverity`
  sent as numeric enum values (`ChannelType.Discord`, `Severity.High`); UI
  resolves `ChannelTypeNames[channel.type]` + `severityName(channel.minSeverity)`
  for display.

### Feeds

- `src/Shield.Web/src/queries/feeds.ts:16` — refresh mutation translates the
  numeric `Feed` enum to its name on the URL, since
  `FeedsController.Refresh(string feed)` parses the enum name (not the int).
- `src/Shield.Web/src/views/FeedsView.vue:23` — feed column renders the name
  via `FeedNames`; refresh button disabled for `!registered` rows so users
  don't post against feeds that have no `IFeedSync` registered.

### Settings

- `src/Shield.Web/src/views/SettingsView.vue:11` — explicitly handles the
  404-from-server case with a friendly "API not yet implemented" panel
  instead of falling through to the generic "Failed to load" message.
  Rendered fields match the `Settings` contract draft already present in
  `types/api.ts` (`singleUserMode`, `oidcEnabled`, `alertSeverityFloor`, etc.).

## What still needs server work (out of UI scope)

Severity levels: **Critical** — the UI is broken without it. **High** — UI works
but features are missing. **Medium** — cosmetic / nice-to-have.

### Critical — `GET /api/settings` returns 404

The SettingsView gracefully degrades, but there is no
`Shield.Api/Controllers/SettingsController.cs`. To deliver the Settings page
end-to-end, add a controller that returns the `Settings` / `SettingsUpdate`
shape already typed in `src/Shield.Web/src/types/api.ts`. Until then,
single-user / OIDC / alert-floor / retention values can only be inspected via
appsettings.

Reproduce:
```bash
curl -i http://localhost:8842/api/settings   # → HTTP/1.1 404 Not Found
```

### Critical — `FindingResponse` lacks display fields

`Shield.Api/Contracts/FindingContracts.cs:5` only returns
`SourceId`, `InventoryItemId`, `AdvisoryRefId`. The UI cannot render a
human-readable list of findings (no package name, no source name, no
ecosystem) without `N+1` round-trips to `/findings/{id}`. Recommend adding
a projection to `FindingResponse` (or a `FindingListItem`) that joins:

- `SourceName` from `Sources`
- `PackageName`, `PackageVersion`, `Ecosystem` from `InventoryItems`
- `Summary`, `Cvss` from `Advisories`

`DashboardView` and `FindingsView` currently show `dedupKey` + `sourceId` as a
fallback, which is correct but ugly.

Reproduce (after seeding any finding):
```bash
curl -s http://localhost:8842/api/findings | jq '.items[0]'
```

### High — enum wire format is integer-only

`Program.cs` does not register a `JsonStringEnumConverter`, so the API both
emits and accepts enums as integers. The OpenAPI doc therefore advertises
integers, which propagates to any generated SDKs and to external HTTP
consumers (Discord webhook payloads etc.). Recommend:

```csharp
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(
        new JsonStringEnumConverter()));
```

If this lands, the UI can drop the numeric-enum bookkeeping in
`types/api.ts` and revert to plain string unions. Document it as a breaking
API change before shipping.

### High — `Settings` controller / surface area undefined

The `Settings` / `SettingsUpdate` / `OidcTestRequest` shapes were authored in
`types/api.ts` ahead of the server. The matching backend work (which keys are
editable at runtime vs. read-only, which require a process restart, how
OIDC client secrets are stored) needs to land before the SPA can become a
real configuration surface.

### Medium — `/api/auth/login` does not return identity

`AuthController.Login` returns only `{succeeded, requiresTwoFactor, error}`,
so the SPA has to round-trip `/auth/me` after a successful login. Acceptable,
but slightly wasteful. Either include a `Me` payload in `LoginResponse` or
document the two-call flow.

### Medium — `Advisory.references` is a JSON string

`Advisory.ReferencesJson` is `string` (likely because SQLite-EFCore stores
it as TEXT). The UI parses it via `parseJsonArray` defensively, but consumers
of the public API would benefit from the server projecting it to `string[]`
in `FindingDetailResponse`.

### Medium — `/api/sources` does not paginate

`SourcesController.List` returns an unbounded array. Fine for now, but if
Shield ever scans dozens of repos the SPA will balloon. Mirror the
`FindingsPage` pattern.

### Medium — Vite dev proxy targets `localhost:5099`

`src/Shield.Web/vite.config.ts:20` proxies `/api` to port 5099, but the live
server in this environment listens on 8080. `npm run dev` against the
running server therefore fails. Either parameterise the proxy via env
(`VITE_API_TARGET`) or align the dev port. Leaving as-is for now since the
built bundle ships under the API's own origin.

### Low — Sources view lacks edit / delete UI

`SourcesController` exposes `PUT` and `DELETE` (`SourcesController.cs:80`,
`:100`) but the SPA only shows create + scan-now. Add an inline edit panel
and a delete confirmation to make the page production-usable.

### Low — Channels view only handles Discord webhooks

`ChannelsView` hard-codes `ChannelType.Discord`. Server supports Ntfy / Smtp /
Inbox. Either branch the form per-type or add a generic JSON-config editor.

### Low — `Finding` actions have no busy-state per row

`FindingDetailView` reuses a single `ack` / `resolve` / `suppress` mutation
across the page, so the disabled state lights up correctly for the current
detail view. The findings list does not yet have row-level actions; if added,
each row needs its own mutation instance.
