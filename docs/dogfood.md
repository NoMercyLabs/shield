# Dogfooding — Shield scans itself

Shield monitors its own supply chain. Source 77 (`NoMercyLabs/shield`) is registered on the live `shield-dev.nomercy.tv` dashboard and scanned every 6 hours. Every vulnerability found in Shield's own dependencies — NuGet packages, SPA `package-lock.json`, `Directory.Packages.props` transitive pins — appears as a finding alongside every other monitored project.

A supply-chain tool that doesn't scan its own supply chain is a meme. Findings against Shield itself are prioritised and resolved before release.
