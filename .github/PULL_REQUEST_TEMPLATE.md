## What
<!-- One sentence. -->

## Why

## How to verify
<!-- Steps that prove the change works end-to-end. -->

## Checklist
- [ ] `dotnet build Shield.sln -c Release` clean
- [ ] `dotnet test Shield.sln -c Release` clean (or every new failure is intentional + documented)
- [ ] SPA: `cd src/Shield.Web && npm run build` clean
- [ ] CSharpier ran on all touched `.cs` files
- [ ] User-facing strings go through i18n (`src/Shield.Web/src/i18n/locales/en.json`, snake_case meaning-only keys)
- [ ] Migrations (if any) hand-written + Up/Down symmetric + snapshot in sync
- [ ] No `Co-Authored-By` trailers in the commits
- [ ] If the change touches anything in `src/Shield.Core/Domain/Enums.cs`, the SPA mirror in `src/Shield.Web/src/types/api.ts` matches
