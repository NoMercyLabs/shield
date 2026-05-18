namespace Shield.Api.Services.Findings;

public interface ITyposquatDetector
{
    bool IsTyposquat(Ecosystem ecosystem, string name);
    bool IsScopeMismatch(string name);
}
