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
  RubyGems: 8,
  SwiftPM: 9,
  Pub: 10,
  Maven: 11,
  Hex: 12,
  Vcpkg: 13,
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
  8: 'RubyGems',
  9: 'SwiftPM',
  10: 'Pub',
  11: 'Maven',
  12: 'Hex',
  13: 'Vcpkg',
}

export const SourceType = {
  GithubRepo: 0,
  LocalFolder: 1,
  LinuxHost: 2,
  GitlabRepo: 3,
  BitbucketRepo: 4,
  ForgejoRepo: 5,
  GiteaRepo: 6,
  CodebergRepo: 7,
} as const
export type SourceType = (typeof SourceType)[keyof typeof SourceType]
export type SourceTypeName = keyof typeof SourceType
export const SourceTypeNames: Record<SourceType, SourceTypeName> = {
  0: 'GithubRepo',
  1: 'LocalFolder',
  2: 'LinuxHost',
  3: 'GitlabRepo',
  4: 'BitbucketRepo',
  5: 'ForgejoRepo',
  6: 'GiteaRepo',
  7: 'CodebergRepo',
}

export const AutoFixMode = {
  Off: 0,
  WeeklyDigest: 1,
  OnEveryScan: 2,
} as const
export type AutoFixMode = (typeof AutoFixMode)[keyof typeof AutoFixMode]
export type AutoFixModeName = keyof typeof AutoFixMode
export const AutoFixModeNames: Record<AutoFixMode, AutoFixModeName> = {
  0: 'Off',
  1: 'WeeklyDigest',
  2: 'OnEveryScan',
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
  Slack: 4,
  Webhook: 5,
} as const
export type ChannelType = (typeof ChannelType)[keyof typeof ChannelType]
export type ChannelTypeName = keyof typeof ChannelType
export const ChannelTypeNames: Record<ChannelType, ChannelTypeName> = {
  0: 'Discord',
  1: 'Ntfy',
  2: 'Smtp',
  3: 'Inbox',
  4: 'Slack',
  5: 'Webhook',
}

export const Feed = {
  Osv: 0,
  Ghsa: 1,
  NpmRegistry: 2,
  DepsDev: 3,
  Socket: 4,
  TrivyDb: 5,
  Kev: 6,
  Epss: 7,
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
  6: 'Kev',
  7: 'Epss',
}

// ---------- auth ----------

export interface Me {
  userId: string | null
  username: string | null
  roles: string[]
  singleUserMode: boolean
  displayName?: string | null
  avatarUrl?: string | null
  profileUrl?: string | null
  providerLogin?: string | null
  providerKey?: string | null
  // Populated when this `/me` response is being returned through an active
  // "Admin viewing as X" impersonation override. `impersonatedBy` is the admin's user id;
  // `impersonatorLogin` is their display name for the banner.
  impersonatedBy?: string | null
  impersonatorLogin?: string | null
}

export interface ImpersonationStartResponse {
  userId: string
  username: string
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
  lastBulkApplyAt: string | null
  autoFixMode: AutoFixMode
  isProduction: boolean
  lastManualBulkApplyAt: string | null
  manualCooldownUntil: string | null
  minPackageAgeHours: number
}

export interface BulkApplyRequest {
  dryRun?: boolean
  maxPackages?: number | null
  force?: boolean
  allowMajorBumps?: boolean
  confirmProduction?: boolean
}

export interface BulkApplyEntry {
  packageName: string
  currentVersion: string
  suggestedVersion: string
  manifestPath: string
  advisoryIds: string[]
}

export interface BulkApplyError {
  packageName: string
  reason: string
}

export interface BulkApplyWarning {
  packageName: string
  message: string
}

export interface BulkApplyResponse {
  dryRun: boolean
  pullRequestUrl: string | null
  entries: BulkApplyEntry[]
  errors: BulkApplyError[]
  reusedBranch: string | null
  majorBumps: BulkApplyEntry[] | null
  warnings: BulkApplyWarning[] | null
}

export interface SetIsProductionRequest {
  isProduction: boolean
}

export interface SourceCreate {
  type: SourceType
  name: string
  configJson: string
  scanInterval: string
  enabled?: boolean
}

export interface FsEntry {
  name: string
  path: string
  isDirectory: boolean
  hasLockfiles: boolean
  hasGitRepo: boolean
  lockfileCount: number | null
  size: number | null
}

export interface FsBrowseResponse {
  path: string
  parent: string | null
  entries: FsEntry[]
  roots: string[]
  hasLockfiles: boolean
}

export interface BulkLocalFoldersRequest {
  paths: string[]
  defaultScanInterval?: string
}

export interface BulkLocalFoldersResponse {
  created: number
  skippedExisting: number
  sources: Source[]
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

export interface SnapshotListItem {
  id: string
  takenAt: string
  contentsSha: string
  itemCount: number
  prevSnapshotId: string | null
}

// Mirrors Shield.Api.Contracts.AnomalyFlags ([Flags] enum). Bitmask — combine via |.
export const AnomalyFlags = {
  None: 0,
  BrandNew: 1,
  SingleMaintainer: 2,
  NewMaintainerThisVersion: 4,
  Typosquat: 8,
  Deprecated: 16,
  HighScopeMismatch: 32,
} as const
export type AnomalyFlags = number

export interface InventoryDiffEntry {
  ecosystem: Ecosystem
  name: string
  version: string
  isDirect: boolean
  parentChain: string | null
  anomaly: AnomalyFlags
}

export interface InventoryDiffChange {
  ecosystem: Ecosystem
  name: string
  fromVersion: string
  toVersion: string
  isDirect: boolean
}

export interface SnapshotDiffResponse {
  older: SnapshotSummary
  newer: SnapshotSummary
  added: InventoryDiffEntry[]
  removed: InventoryDiffEntry[]
  versionChanged: InventoryDiffChange[]
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
  isKev?: boolean
  kevAddedAt?: string | null
  kevDueDate?: string | null
  epssScore?: number | null
  epssPercentile?: number | null
}

export interface Finding {
  id: string
  sourceId: number
  sourceName: string | null
  inventoryItemId: number
  advisoryRefId: string
  severity: Severity
  firstSeenAt: string
  lastSeenAt: string
  state: FindingState
  dedupKey: string
  notes: string | null
  packageName: string | null
  packageVersion: string | null
  ecosystem: Ecosystem | null
  advisoryExternalId: string | null
  advisorySummary: string | null
  isKev?: boolean
  kevAddedAt?: string | null
  kevDueDate?: string | null
  epssScore?: number | null
  epssPercentile?: number | null
  // Name fields shipped alongside the numeric enum wire values so token consumers don't have
  // to keep their own lookup table. UI prefers numeric + the local Names map for i18n consistency.
  severityName?: string
  stateName?: string
  ecosystemName?: string | null
}

export interface AdvisoryReference {
  type?: string
  url: string
}

export interface SourceUpdate {
  name: string
  configJson: string
  scanInterval: string
  enabled: boolean
  minPackageAgeHours?: number
}

export interface FindingDetail {
  finding: Finding
  advisory: Advisory | null
  item: InventoryItemResponse | null
  sourceType: SourceType | null
  fixSuggestion: FixSuggestion | null
  // True when the caller has Triage permission on this finding's source. The triage action
  // buttons (Ack / Resolve / Suppress) hide when false instead of letting the server 403.
  canTriage?: boolean
}

export interface FixEligibility {
  eligible: boolean
  reason: string | null
}

export interface FixSuggestion {
  packageName: string
  currentVersion: string
  suggestedVersion: string
  notes: string | null
  prEligibility: FixEligibility
  autoEligibility: FixEligibility
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

export const SortBy = {
  Severity: 'severity',
  DiscoveredAt: 'discoveredAt',
  PackageName: 'packageName',
  SourceName: 'sourceName',
} as const
export type SortBy = (typeof SortBy)[keyof typeof SortBy]

export const SortDir = {
  Asc: 'asc',
  Desc: 'desc',
} as const
export type SortDir = (typeof SortDir)[keyof typeof SortDir]

export interface FindingFilter {
  severity?: Severity[]
  sourceId?: number[]
  ecosystem?: Ecosystem[]
  state?: FindingState[]
  packageName?: string[]
  hasFix?: boolean | null
  kevOnly?: boolean
  epssMin?: number | null
  advisoryQuery?: string | null
  sortBy?: SortBy
  sortDir?: SortDir
  page?: number
  pageSize?: number
}

export interface FindingFilterPreset {
  id: string
  name: string
  kind: string
  queryJson: string
  createdAt: string
}

export interface BulkFindingsRequest {
  findingIds: string[]
}

export interface BulkSuppressRequest {
  findingIds: string[]
  reason: string
}

export interface BulkFindingsResponse {
  updated: number
  notFound: string[]
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
  // Server-side parsed view with secret fields masked as "****".
  // Null when the stored JSON couldn't be parsed (fall back to configJson).
  parsedConfig: Record<string, unknown> | null
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

// ---------- auth + oauth ----------

export interface RegistrationAllowed {
  allowed: boolean
  reason: string | null
}

export interface ChangePasswordRequest {
  currentPassword: string
  newPassword: string
}

export type OAuthProviderName = 'Github' | 'Slack' | 'Google'

export interface OAuthStartResponse {
  authorizationUrl: string
}

export interface OAuthStatus {
  connected: boolean
  scopes: string[]
  accountLogin: string | null
  expiresAt: string | null
  deviceFlowAvailable?: boolean
}

export interface GithubDeviceStartResponse {
  flowId: string
  userCode: string
  verificationUri: string
  expiresIn: number
  interval: number
  verificationUriComplete?: string | null
}

export type GithubDevicePollStatus = 'pending' | 'slow_down' | 'expired' | 'denied' | 'ok'

export interface GithubDevicePollResponse {
  status: GithubDevicePollStatus
  user?: { login: string, id: number, avatarUrl: string | null }
}

export interface SlackChannelInfo {
  id: string
  name: string
  isPrivate: boolean
}

export interface SlackChannelsResponse {
  channels: SlackChannelInfo[]
}

export interface GitHubRepoEntry {
  id: number
  owner: string
  name: string
  fullName: string
  description: string | null
  defaultBranch: string | null
  private: boolean
  archived: boolean
  fork: boolean
  language: string | null
}

export interface GitHubRepoListResponse {
  repos: GitHubRepoEntry[]
  total: number
}

export interface BulkSelection {
  owner: string
  name: string
  branch?: string | null
}

export interface BulkFromGithubRequest {
  selections: BulkSelection[]
  defaultScanInterval?: string | null
}

export interface BulkFromGithubResponse {
  created: number
  skippedExisting: number
  sources: Source[]
}

// ---------- settings ----------

export interface OAuthProviderConfig {
  clientId: string | null
  clientSecretMasked: string | null
  scopes: string | null
  configured: boolean
}

// ClientSecret semantics: null = preserve existing, "" = clear, non-empty = overwrite.
export interface OAuthProviderConfigPatch {
  clientId: string | null
  clientSecret: string | null
  scopes: string | null
}

export interface Settings {
  singleUserMode: boolean
  openApiEnabled: boolean
  oidcEnabled: boolean
  oidcIssuer: string | null
  oidcClientId: string | null
  oidcClientSecretMasked: string | null
  alertSeverityFloor: Severity
  retentionDays: number
  github: OAuthProviderConfig
  slack: OAuthProviderConfig
  google: OAuthProviderConfig
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
  github?: OAuthProviderConfigPatch | null
  slack?: OAuthProviderConfigPatch | null
  google?: OAuthProviderConfigPatch | null
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

// ---------- audit ----------

export interface AuditEntry {
  id: string
  at: string
  actorUserId: string | null
  actorName: string
  // GitHub login + avatar resolved server-side when the actor has a bound GitHub identity.
  // Falls back to actorName when null. Avatar drives the round chip in AuditView.
  actorLogin: string | null
  actorAvatarUrl: string | null
  action: string
  targetType: string
  targetId: string
  // Friendly label for the target (Source.Name, Invite.Email, etc.). Null when the type
  // doesn't have an obvious display string — UI falls back to `{targetType}#{targetId}`.
  targetLabel: string | null
  detailsJson: string | null
  remoteIp: string | null
}

export interface AuditPage {
  items: AuditEntry[]
  total: number
  page: number
  pageSize: number
}

export interface AuditFilter {
  page?: number
  pageSize?: number
  action?: string | null
  targetType?: string | null
}

// ---------- security (fail2ban correlation) ----------

export interface SecurityEvent {
  id: string
  at: string
  source: string
  eventType: string
  severity: Severity
  host: string | null
  jail: string | null
  remoteIp: string | null
  userAgent: string | null
  userName: string | null
  path: string | null
  detailsJson: string | null
}

export interface SecurityEventsPage {
  items: SecurityEvent[]
  total: number
  page: number
  pageSize: number
}

export interface SecurityEventFilter {
  page?: number
  pageSize?: number
  minSeverity?: Severity | null
  source?: string | null
  jail?: string | null
  ip?: string | null
  userName?: string | null
  since?: string | null
  until?: string | null
}

export interface IpReputation {
  id: number
  ip: string
  eventCount: number
  score: number
  firstSeenAt: string
  lastSeenAt: string
  lastJail: string | null
  lastBannedAt: string | null
  lastUnbannedAt: string | null
  currentlyBanned: boolean
  notes: string | null
  country: string | null
}

export interface IpReputationsPage {
  items: IpReputation[]
  total: number
  page: number
  pageSize: number
}

export interface IpDetail {
  reputation: IpReputation
  recentEvents: SecurityEvent[]
}

export interface SecurityHost {
  host: string
  lastSeenAt: string
  eventCount: number
}

export interface SecurityHostsResponse {
  items: SecurityHost[]
}

// ---------- onboarding ----------

export interface OnboardingStatusResponse {
  completed: boolean
  sourceCount: number
  channelCount: number
  githubConnected: boolean
  anyOauthConfigured: boolean
}

// ---------- access (per-source ACL) ----------

export const SourceAccessLevel = {
  Read: 0,
  Triage: 1,
} as const
export type SourceAccessLevel = (typeof SourceAccessLevel)[keyof typeof SourceAccessLevel]
export type SourceAccessLevelName = keyof typeof SourceAccessLevel
export const SourceAccessLevelNames: Record<SourceAccessLevel, SourceAccessLevelName> = {
  0: 'Read',
  1: 'Triage',
}

export type AccessRoleName = 'Admin' | 'Maintainer' | 'Viewer'

export interface AccessUser {
  id: string
  username: string
  email: string | null
  roles: string[]
  createdAt: string
}

export interface GroupMember {
  userId: string
  username: string
  addedAt: string
}

export interface SourceGroup {
  id: number
  name: string
  description: string | null
  createdAt: string
  members: GroupMember[]
}

export interface CreateGroupRequest {
  name: string
  description?: string | null
}

export interface UpdateGroupRequest {
  name: string
  description?: string | null
}

export interface AddGroupMemberRequest {
  username?: string | null
  email?: string | null
}

export interface SourceGrant {
  id: number
  sourceId: number
  userId: string | null
  username: string | null
  groupId: number | null
  groupName: string | null
  level: SourceAccessLevel
  grantedAt: string
  grantedBy: string | null
}

export interface SourceGrants {
  sourceId: number
  grants: SourceGrant[]
}

export interface GrantSourceRequest {
  userId?: string | null
  groupId?: number | null
  level: SourceAccessLevel
}

export interface InviteExternalIdentity {
  provider: 'github'
  subjectId: string
  login: string
  displayName?: string | null
  avatarUrl?: string | null
  email?: string | null
}

export interface InviteUserRequest {
  email?: string | null
  role: AccessRoleName
  sourceGroupIds?: number[] | null
  externalIdentity?: InviteExternalIdentity | null
}

export interface InvitePreBoundIdentity {
  provider: string
  subjectId: string
  login: string
}

export interface InviteUserResponse {
  inviteId: string
  email: string
  role: AccessRoleName
  sourceGroupIds: number[]
  expiresAt: string
  acceptUrl: string
  emailSent: boolean
  emailSkipReason: string | null
  preBound?: InvitePreBoundIdentity | null
}

export interface PendingInvite {
  id: string
  email: string
  role: AccessRoleName
  sourceGroupIds: number[]
  sourceGroupNames: string[]
  createdAt: string
  expiresAt: string
  lastSentAt: string | null
  resendCount: number
  inviterLogin: string | null
  preBound?: InvitePreBoundIdentity | null
  // Raw invite token. Admin-only endpoint exposes it so the Pending Invitations table can
  // build the accept URL for Copy/Share buttons without an extra per-row request.
  token?: string | null
}

export interface GithubOrgSummary {
  login: string
  name?: string | null
  avatarUrl?: string | null
  memberCount?: number | null
}

export interface GithubUserSummary {
  login: string
  name?: string | null
  email?: string | null
  avatarUrl?: string | null
  githubId: string
}

export interface GithubOrgListResponse {
  orgs: GithubOrgSummary[]
}

export interface GithubMemberListResponse {
  members: GithubUserSummary[]
  page: number
  perPage: number
  hasMore: boolean
}

export interface GithubUserSearchResponse {
  users: GithubUserSummary[]
}

export interface PublicInvitePreview {
  role: AccessRoleName
  sourceGroupNames: string[]
  inviterLogin: string
  expiresAt: string
}

export interface AcceptInviteRequest {
  token: string
  // Optional — populated by the device-flow signin path. When omitted, the server uses
  // the caller's authenticated session cookie (post auth-code popup) as identity proof.
  acceptanceTicket?: string
}

export interface AcceptInviteResponse {
  userId: string
  username: string
  role: AccessRoleName
  sourceGroupIds: number[]
}

// ---------- two-factor + sessions ----------

export interface TwoFactorEnrollResponse {
  sharedKey: string
  authenticatorUri: string
  recoveryCodes: string[]
}

export interface TwoFactorStatus {
  enabled: boolean
  requiredByPolicy: boolean
  remainingRecoveryCodes: number
}

export interface SessionInfo {
  id: string
  userId: string
  username: string | null
  userAgent: string | null
  remoteIp: string | null
  createdAt: string
  lastActiveAt: string
  isCurrent: boolean
}

export interface SessionListResponse {
  sessions: SessionInfo[]
}

// ---------- notifications ----------

export const NotificationKind = {
  ScanFailed: 0,
  OauthExpiring: 1,
  FeedDown: 2,
  MaintainerChange: 3,
  NewAnomaly: 4,
  SystemMessage: 5,
} as const
export type NotificationKind = (typeof NotificationKind)[keyof typeof NotificationKind]
export type NotificationKindName = keyof typeof NotificationKind
export const NotificationKindNames: Record<NotificationKind, NotificationKindName> = {
  0: 'ScanFailed',
  1: 'OauthExpiring',
  2: 'FeedDown',
  3: 'MaintainerChange',
  4: 'NewAnomaly',
  5: 'SystemMessage',
}

export interface Notification {
  id: string
  userId: string | null
  kind: NotificationKind
  severity: Severity
  title: string
  body: string
  relatedType: string | null
  relatedId: string | null
  createdAt: string
  readAt: string | null
  archivedAt: string | null
}

export interface NotificationsPage {
  items: Notification[]
  unreadCount: number
}

// ---------- package watch ----------

export interface PackageWatch {
  id: string
  ecosystem: Ecosystem
  packageName: string
  addedAt: string
}

export interface WatchOpenCounts {
  low: number
  medium: number
  high: number
  critical: number
}

export interface WatchSummaryRow {
  ecosystem: Ecosystem
  packageName: string
  sourceCount: number
  openFindings: WatchOpenCounts
}

// ---------- saved filters ----------

export interface SavedFilter {
  id: string
  name: string
  kind: string
  queryJson: string
  createdAt: string
}

// ---------- api tokens (CI personal-access tokens) ----------

export type ApiTokenScope = 'findings:read' | 'findings:write' | 'sources:read' | 'sbom:write'

export interface ApiToken {
  id: string
  userId: string
  name: string
  prefix: string
  scopes: ApiTokenScope[]
  sourceIdFilter: number[]
  createdAt: string
  expiresAt: string | null
  lastUsedAt: string | null
  lastUsedIp: string | null
  revokedAt: string | null
}

export interface CreateTokenRequest {
  name: string
  scopes: ApiTokenScope[]
  expiresInDays?: number | null
  sourceIdFilter?: number[] | null
}

export interface CreateTokenResponse {
  token: ApiToken
  plaintext: string
}
