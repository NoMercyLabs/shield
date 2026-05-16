using System.Text;

namespace Shield.Api.Auth.OAuthProviders;

internal static class UrlForm
{
    public static string Encode(IDictionary<string, string> values)
    {
        StringBuilder sb = new();
        bool first = true;
        foreach (KeyValuePair<string, string> kvp in values)
        {
            if (!first)
                sb.Append('&');
            first = false;
            sb.Append(Uri.EscapeDataString(kvp.Key));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(kvp.Value));
        }
        return sb.ToString();
    }
}
