using Shield.Core.Domain;

namespace Shield.Core.Abstractions;

// Static-abstract marker types that carry an Ecosystem at the type-system level. Used to
// parameterize ecosystem-specific generic classes (e.g. EfPackageNameSource<TTag>) without
// either hiding the ecosystem in a runtime constructor arg or duplicating a class per
// ecosystem.
public interface IEcosystemTag
{
    static abstract Ecosystem Value { get; }
}

public static class EcosystemTag
{
    public readonly struct Npm : IEcosystemTag
    {
        public static Ecosystem Value => Ecosystem.Npm;
    }

    public readonly struct Nuget : IEcosystemTag
    {
        public static Ecosystem Value => Ecosystem.Nuget;
    }

    public readonly struct Rust : IEcosystemTag
    {
        public static Ecosystem Value => Ecosystem.Rust;
    }

    public readonly struct Python : IEcosystemTag
    {
        public static Ecosystem Value => Ecosystem.Python;
    }

    public readonly struct RubyGems : IEcosystemTag
    {
        public static Ecosystem Value => Ecosystem.RubyGems;
    }

    public readonly struct Composer : IEcosystemTag
    {
        public static Ecosystem Value => Ecosystem.Composer;
    }

    public readonly struct Hex : IEcosystemTag
    {
        public static Ecosystem Value => Ecosystem.Hex;
    }

    public readonly struct Go : IEcosystemTag
    {
        public static Ecosystem Value => Ecosystem.Go;
    }

    public readonly struct Maven : IEcosystemTag
    {
        public static Ecosystem Value => Ecosystem.Maven;
    }

    public readonly struct Gradle : IEcosystemTag
    {
        public static Ecosystem Value => Ecosystem.Gradle;
    }

    public readonly struct Pub : IEcosystemTag
    {
        public static Ecosystem Value => Ecosystem.Pub;
    }

    public readonly struct SwiftPM : IEcosystemTag
    {
        public static Ecosystem Value => Ecosystem.SwiftPM;
    }

    public readonly struct Vcpkg : IEcosystemTag
    {
        public static Ecosystem Value => Ecosystem.Vcpkg;
    }
}
