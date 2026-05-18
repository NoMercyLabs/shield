namespace Shield.Api.Contracts;

public sealed record SavedFilterResponse(
    Guid Id,
    string Name,
    string Kind,
    string QueryJson,
    DateTime CreatedAt
)
{
    public static SavedFilterResponse From(SavedFilter filter) =>
        new(filter.Id, filter.Name, filter.Kind, filter.QueryJson, filter.CreatedAt);
}

public sealed record CreateSavedFilterRequest(string Name, string Kind, string QueryJson);
