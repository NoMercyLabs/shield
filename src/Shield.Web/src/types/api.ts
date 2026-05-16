// Hand-written API types, aligned with the live server contract on 2026-05-16.
// Re-run `npm run types:gen` once OpenAPI exposes enum metadata.
//
// Server enums serialize as INTEGERS (System.Text.Json default) and the API
// accepts integers in request bodies. UI surfaces use the string names below.

// ---------- enums (numeric — wire format) ----------

export const Severity = {
  Low: 0,
  Medium: 1,
  High: 2,
  Critical: 3,
} as const
export type Severity = (typeof Severity)[keyof typeof Severity]
export type SeverityName = keyof typeof Severity
export const SeverityNames: Record<Severity, SeverityName> = {
  0: 'Low',
  1: 'Medium',
  2: 'High',
  3: 'Critical',
}

export const Ecosystem = {
  Npm: 0,
  Nuget: 1,
  Composer: 2,
  Gradle: 3,
  Os: 4,
  Python: 5,
  Go: 6,
  Rust: 7,
} as const
export type Ecosystem = (typeof Ecosystem)[keyof typeof Ecosystem]
export type EcosystemName = keyof typeof Ecosystem
export const EcosystemNames: Record<Ecosystem, EcosystemName> = {
  0: 'Npm',
  1: 'Nuget',
  2: 'Composer',
  3: 'Gradle',
  4: 'Os',
  5: 'Python',
  6: 'Go',
  7: 'Rust',
}

export const SourceType = {
  GithubRepo: 0,
  LocalFolder: 1,
  LinuxHost: 2,
} as const
export type SourceType = (typeof SourceType)[keyof typeof SourceType]
export type SourceTypeName = keyof typeof SourceType
export const SourceTypeNames: Record<SourceType, SourceTypeName> = {
  0: 'GithubRepo',
  1: 'LocalFolder',
  2: 'LinuxHost',
}

export const FindingState = {
  Open: 0,
  Acked: 1,
  Resolved: 2,
  Suppressed: 3,
} as const
export type FindingState = (typeof FindingState)[keyof typeof FindingState]
export type FindingStateName = keyof typeof FindingState
export const FindingStateNames: Record<FindingState, FindingStateName> = {
  0: 'Open',
  1: 'Acked',
  2: 'Resolved',
  3: 'Suppressed',
}

export const ChannelType = {
  Discord: 0,
  Ntfy: 1,
  Smtp: 2,
  Inbox: 3,
} as const
export type ChannelType = (typeof ChannelType)[keyof typeof ChannelType]
export type ChannelTypeName = keyof typeof ChannelType
export const ChannelTypeNames: Record<ChannelType, ChannelTypeName> = {
  0: 'Discord',
  1: 'Ntfy',
  2: 'Smtp',
  3: 'Inbox',
}

export const Feed = {
  Osv: 0,
  Ghsa: 1,
  NpmRegistry: 2,
  DepsDev: 3,
  Socket: 4,
  TrivyDb: 5,
} as const
export type Feed = (typeof Feed)[keyof typeof Feed]
export type FeedName = keyof typeof Feed
export const FeedNames: Record<Feed, FeedName> = {
  0: 'Osv',
  1: 'Ghsa',
  2: 'NpmRegistry',
  3: 'DepsDev',
  4: 'Socket',
  5: 'TrivyDb',
}

// ---------- auth ----------

export interface Me {
  userId: string | null
  username: string | null
  roles: string[]
  singleUserMode: boolean
}

export interface LoginRequest {
  username: string
  password: string
  twoFactorCode?: string
  rememberMe?: boolean
}

export interface LoginResponse {
  succeeded: boolean
  requiresTwoFactor: boolean
  error: string | null
}

// ---------- sources ----------

export interface DetectedRemote {
  host: string
  owner: string
  repo: string
  remoteUrl: string
  branch: string | null
}

export interface Source {
  id: number
  type: SourceType
  name: string
  configJson: string
  scanInterval: string
  lastScannedAt: string | null
  lastError: string | null
  enabled: boolean
  createdAt: string
  updatedAt: string
  detectedRemote: DetectedRemote | null
}

export interface SourceCreate {
  type: SourceType
  name: string
  configJson: string
  scanInterval: string
  enabled?: boolean
}

export interface SnapshotSummary {
  id: string
  takenAt: string
  contentsSha: string
  itemCount: number
  ecosystems?: Partial<Record<keyof typeof EcosystemNames, number>>
}

export interface SourceDetail {
  source: Source
  latestSnapshot: SnapshotSummary | null
}

export interface InventoryItemResponse {
  id: number
  ecosystem: Ecosystem
  name: string
  version: string
  isDirect: boolean
  parentChain: string
}

// ---------- advisories + findings ----------

export interface Advisory {
  id: string
  feed: Feed
  externalId: string
  ecosystem: Ecosystem
  packageName: string
  affectedRangesJson: string
  severity: Severity
  cvss: number | null
  summary: string
  referencesJson: string
  publishedAt: string
  modifiedAt: string
  fetchedAt: string
}

export interface Finding {
  id: string
  sourceId: number
  inventoryItemId: number
  advisoryRefId: string
  severity: Severity
  firstSeenAt: string
  lastSeenAt: string
  state: FindingState
  dedupKey: string
  notes: string | null
}

export interface FindingDetail {
  finding: Finding
  advisory: Advisory | null
  item: InventoryItemResponse | null
  sourceType: SourceType | null
  fixSuggestion: FixSuggestion | null
}

export interface FixSuggestion {
  packageName: string
  currentVersion: string
  suggestedVersion: string
  notes: string | null
}

export type ApplyFixStrategy = 'auto' | 'pr'

export interface ApplyFixRequest {
  strategy: ApplyFixStrategy
}

export interface ApplyFixResponse {
  success: boolean
  changedFiles: string[]
  followUpCommand: string | null
  pullRequestUrl: string | null
  reason: string | null
}

export interface FindingFilter {
  severity?: Severity
  sourceId?: number
  ecosystem?: Ecosystem
  state?: FindingState
  page?: number
  pageSize?: number
}

export interface FindingsPage {
  items: Finding[]
  total: number
  page: number
  pageSize: number
}

export interface PagedResponse<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
}

// ---------- channels ----------

export interface AlertChannel {
  id: string
  type: ChannelType
  name: string
  configJson: string
  minSeverity: Severity
  enabled: boolean
}

export interface ChannelCreate {
  type: ChannelType
  name: string
  configJson: string
  minSeverity: Severity
  enabled?: boolean
}

export interface TestSendResponse {
  success: boolean
  delivered: number
  error: string | null
}

// ---------- feeds ----------

export interface FeedStatus {
  feed: Feed
  lastSuccessAt: string | null
  lastError: string | null
  nextRunAt: string | null
  cursor: string | null
  registered: boolean
}

// ---------- dashboard ----------

export interface OpenCounts {
  low: number
  medium: number
  high: number
  critical: number
}

export interface DashboardResponse {
  openCounts: OpenCounts
  sourcesHealthy: number
  sourcesStale: number
  recentFindings: Finding[]
}

// ---------- settings ----------

export interface Settings {
  singleUserMode: boolean
  openApiEnabled: boolean
  oidcEnabled: boolean
  oidcIssuer: string | null
  oidcClientId: string | null
  oidcClientSecretMasked: string | null
  alertSeverityFloor: Severity
  retentionDays: number
}

export interface SettingsUpdate {
  singleUserMode: boolean
  openApiEnabled: boolean
  oidcEnabled: boolean
  oidcIssuer: string | null
  oidcClientId: string | null
  oidcClientSecret: string | null
  alertSeverityFloor: Severity
  retentionDays: number
}

export interface SettingsUpdateResponse {
  settings: Settings
  requiresRestart: boolean
  restartKeys: string[]
}

export interface OidcTestRequest {
  issuer: string
  clientId: string
  clientSecret: string | null
}

export interface OidcTestResponse {
  ok: boolean
  error: string | null
}

export interface RuntimeInfo {
  version: string
  environment: string
  contentRoot: string
  webRoot: string
}
