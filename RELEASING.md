# Releasing Shield

Shield ships as a single self-hosted container image plus a tagged GitHub release.
This document is the canonical procedure. If you change it, change the workflow too.

## Versioning

Shield follows **Semantic Versioning 2.0.0** (`MAJOR.MINOR.PATCH`).

- `MAJOR` — breaking API, DB schema with no migration path, or removed feature
- `MINOR` — new feature, new endpoint, additive DB migration
- `PATCH` — bug fix, dependency bump, doc/config-only change

Pre-release identifiers — append `-<id>.<N>` to the next planned version:

| Identifier  | Use when                                                     |
| ----------- | ------------------------------------------------------------ |
| `-alpha.N`  | Internal cuts. Schema may still move. Not for outside users. |
| `-beta.N`   | Feature-complete, asking outsiders to break it.              |
| `-rc.N`     | Release-candidate. Only blocker-level fixes between RC and final. |

Examples: `v0.1.0-alpha.1`, `v0.1.0-beta.3`, `v1.0.0-rc.2`, `v1.0.0`.

## Branch

Releases are always cut from `master`. No release branches, no cherry-pick trains.
If `master` is not green, fix it first — never tag around a known-broken build.

## Steps

1. **Verify the suite is green locally.**

   ```bash
   dotnet test Shield.sln
   ```

   If `dotnet test` is not clean, stop here. Fix the failure (or the test) and rerun.

2. **Update `CHANGELOG.md`.**

   Add a new top section with the version, the date, and a bulleted summary grouped
   under `Added` / `Changed` / `Fixed` / `Security`. Pull the bullets from the commit
   log since the previous tag (`git log <prev>..HEAD --oneline`).

3. **Commit the changelog.**

   ```bash
   git commit -am "chore: release vX.Y.Z"
   ```

4. **Tag the release.** Annotated tag only — lightweight tags do not trigger the
   release workflow reliably.

   ```bash
   git tag vX.Y.Z -m "Shield vX.Y.Z"
   ```

5. **Push commit + tag.**

   ```bash
   git push origin master --tags
   ```

6. **CI takes it from here.** `.github/workflows/release.yml` runs on the `v*` tag:

   - Builds the multi-arch image (`linux/amd64` + `linux/arm64`).
   - Pushes to `ghcr.io/nomercylabs/shield:X.Y.Z` and (for non-prereleases) `:latest`.
   - Generates an SBOM via `anchore/sbom-action` and attaches it to the GH release.

   Watch the run:

   ```bash
   gh run watch --exit-status
   ```

   If the workflow fails, do **not** delete the tag and re-push. Tag a `vX.Y.Z+1`
   patch with the fix instead. Force-pushing release tags breaks pulls for anyone
   who already grabbed the broken image.

## Testing a release locally

Before cutting the tag — or after, to confirm the published image — build the
container against the same Dockerfile CI uses:

```bash
docker build -f docker/Dockerfile -t shield:test .
docker run --rm -p 8080:8080 \
  -e Shield__Auth__JwtSigningKey="$(openssl rand -base64 48)" \
  shield:test
```

Then in a second terminal:

```bash
curl -sf http://localhost:8080/healthz
```

Should return `{"status":"ok"}`. For a fuller walk, run the smoke script against
the container:

```bash
SHIELD_SMOKE_PORT=8080 scripts/smoke-test.sh
```

(Note: when targeting a container, the smoke script's `dotnet run` startup step
is redundant — comment it out or run only the curl checks in steps 3–9.)

## Hotfixes

For an urgent fix on the latest release:

1. Branch from the release tag: `git switch -c hotfix/X.Y.Z+1 vX.Y.Z`.
2. Land the fix, write the test, run `dotnet test`.
3. Fast-forward `master` to include the fix commit (merge or rebase, your call).
4. Tag `vX.Y.(Z+1)` on `master` per the normal steps.

Never tag a hotfix on a branch other than `master` — the workflow only watches `master`.
