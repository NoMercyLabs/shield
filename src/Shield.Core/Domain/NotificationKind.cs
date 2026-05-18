namespace Shield.Core.Domain;

public enum NotificationKind
{
    ScanFailed = 0,
    OauthExpiring = 1,
    FeedDown = 2,
    MaintainerChange = 3,
    NewAnomaly = 4,
    SystemMessage = 5,
    Alert = 6,
}
