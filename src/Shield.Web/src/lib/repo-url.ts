// Render a browser URL for a detected git remote, picking the right tree/blob path
// shape for the host. Covers GitHub, GitLab, Bitbucket, Forgejo, Gitea, Codeberg, and
// falls back to a sensible default for unknown self-hosted instances (treats them like
// Forgejo, which is what most "I run my own Git" deployments end up using these days).

export interface DetectedRemoteLike {
  host: string
  owner: string
  repo: string
  branch?: string | null
}

export function repoUrl(remote: DetectedRemoteLike | null | undefined): string | null {
  if (!remote || !remote.host || !remote.owner || !remote.repo)
    return null
  const host = remote.host.toLowerCase()
  const base = `https://${remote.host}/${remote.owner}/${remote.repo}`
  if (!remote.branch)
    return base
  const enc = encodeURIComponent(remote.branch)
  // Per-host branch URL conventions; switch on suffix so subdomains (gitlab.example.com)
  // still pick up the GitLab shape.
  if (host === 'github.com' || host.endsWith('.github.com'))
    return `${base}/tree/${enc}`
  if (host === 'gitlab.com' || host.endsWith('.gitlab.com') || host.includes('gitlab'))
    return `${base}/-/tree/${enc}`
  if (host === 'bitbucket.org' || host.endsWith('.bitbucket.org'))
    return `${base}/src/${enc}`
  // Forgejo / Gitea / Codeberg all share the same /src/branch/<branch> route.
  return `${base}/src/branch/${enc}`
}

// Per-host CSS class hint so badges can colour by provider without bloating the catalog.
export function repoProviderKey(host: string | null | undefined): string {
  if (!host) return 'unknown'
  const h = host.toLowerCase()
  if (h === 'github.com' || h.endsWith('.github.com')) return 'github'
  if (h === 'gitlab.com' || h.endsWith('.gitlab.com') || h.includes('gitlab')) return 'gitlab'
  if (h === 'bitbucket.org' || h.endsWith('.bitbucket.org')) return 'bitbucket'
  if (h === 'codeberg.org') return 'codeberg'
  if (h.includes('forgejo')) return 'forgejo'
  if (h.includes('gitea')) return 'gitea'
  return 'unknown'
}
