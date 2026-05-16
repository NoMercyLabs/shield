namespace Shield.Core.Domain;

public enum SourceType
{
    GithubRepo = 0,
    LocalFolder = 1,
    LinuxHost = 2,
}

public enum Ecosystem
{
    Npm = 0,
    Nuget = 1,
    Composer = 2,
    Gradle = 3,
    Os = 4,
}

public enum Severity
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3,
}

public enum FindingState
{
    Open = 0,
    Acked = 1,
    Resolved = 2,
    Suppressed = 3,
}

public enum ChannelType
{
    Discord = 0,
    Ntfy = 1,
    Smtp = 2,
    Inbox = 3,
}

public enum Feed
{
    Osv = 0,
    Ghsa = 1,
    NpmRegistry = 2,
    DepsDev = 3,
    Socket = 4,
    TrivyDb = 5,
}

public enum AlertStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2,
}
