# Contributing to Shield

Thanks for taking the time. Shield is small enough that the rules are short.

## Branches

- `master` is the default branch. Don't open PRs against any other branch unless asked.
- Feature branches: `feat/<short-description>`
- Bugfix branches: `fix/<short-description>`

## Commits

Conventional Commits, lowercase, imperative, no period, max 72 chars on the subject line.

```
feat(scanner): add gradle build.gradle.kts fallback parser
fix(matcher): handle empty version range in osv feed
docs(readme): clarify single-user defaults
```

Types: `feat`, `fix`, `chore`, `docs`, `refactor`, `test`, `perf`, `ci`.

Don't add `Co-Authored-By` trailers. Don't reference issues with `Closes #N` in commits during development — open a PR and let the merge land that link.

## Before opening a PR

```bash
dotnet test Shield.sln
npm test --prefix src/Shield.Web   # once the test suite lands
```

If you touched C# files, run the formatter:

```bash
dotnet csharpier format .
```

Tests are required for new features. Bug fixes should land with a regression test when one is practical to write. Never weaken an assertion to make a test pass — fix the code or fix the test isolation.

## PR description

Explain **why**, not just what. The diff already shows what. Examples of useful "why":

- "OSV's incremental cursor was using `published` instead of `modified`; advisories that were re-issued never re-synced."
- "Local folder scanner was following symlinks and walking out of the project; users were getting findings for /usr/lib."

Skip checkbox-style test plans in the PR body. If a reviewer needs to know how you tested it, write that in prose.

## Code conventions

- Multi-line object/record literals: 2+ properties one per line, trailing commas.
- `nullable enable` on every project. No nullable-suppression operators (`!`) without an explanatory comment.
- No `as` casts. Type at the declaration site.
- No `// TODO` without a follow-up issue linked.
- Comments earn their place. Default to none. If you write one, explain *why*, not *what*.

## License

By contributing you agree your contribution is licensed under the project's MIT license.
