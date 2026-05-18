using Shield.Core.Domain;
using Shield.Data.Identity;

namespace Shield.Api.Services.Auth;

public enum SigninMethod
{
    Password,
    GithubOAuth,
    GoogleOAuth,
    SlackOAuth,
    InviteAcceptance,
    RecoveryCode,
}

public static class SigninMethodExtensions
{
    public static string ToHumanLabel(this SigninMethod method) =>
        method switch
        {
            SigninMethod.Password => "password",
            SigninMethod.GithubOAuth => "GitHub",
            SigninMethod.GoogleOAuth => "Google",
            SigninMethod.SlackOAuth => "Slack",
            SigninMethod.InviteAcceptance => "invite",
            SigninMethod.RecoveryCode => "recovery code",
            _ => throw new ArgumentOutOfRangeException(nameof(method)),
        };

    public static string ToAuditKey(this SigninMethod method) =>
        method switch
        {
            SigninMethod.Password => "password",
            SigninMethod.GithubOAuth => "oauth.github",
            SigninMethod.GoogleOAuth => "oauth.google",
            SigninMethod.SlackOAuth => "oauth.slack",
            SigninMethod.InviteAcceptance => "invite",
            SigninMethod.RecoveryCode => "recovery",
            _ => throw new ArgumentOutOfRangeException(nameof(method)),
        };
}

public interface ISessionAuditor
{
    Task RecordSigninAsync(
        ShieldUser user,
        UserSession session,
        SigninMethod method,
        CancellationToken ct = default
    );
}
