using ADEXO.Contracts.Authority;
using ADEXO.Contracts.Features;

namespace ADEXO.Contracts.Capabilities;

public enum ProviderKind
{
    ActiveDirectory,
    MicrosoftGraph,
    ExchangeOnline,
    EntraConnect,
}

public sealed class ProviderCapability
{
    private ProviderCapability(
        ProviderKind provider,
        KnownFeatureId feature,
        bool available,
        bool authenticated,
        bool authorized,
        bool canRead,
        bool canWrite,
        SourceOfAuthority source,
        string? missingPermission,
        string? unsupportedReason,
        DateTimeOffset lastCheckedUtc)
    {
        Provider = provider;
        Feature = feature;
        Available = available;
        Authenticated = authenticated;
        Authorized = authorized;
        CanRead = canRead;
        CanWrite = canWrite;
        Source = source;
        MissingPermission = missingPermission;
        UnsupportedReason = unsupportedReason;
        LastCheckedUtc = lastCheckedUtc;
    }

    public ProviderKind Provider { get; }

    public KnownFeatureId Feature { get; }

    public bool Available { get; }

    public bool Authenticated { get; }

    public bool Authorized { get; }

    public bool CanRead { get; }

    public bool CanWrite { get; }

    public SourceOfAuthority Source { get; }

    public string? MissingPermission { get; }

    public string? UnsupportedReason { get; }

    public DateTimeOffset LastCheckedUtc { get; }

    public static ProviderCapability Unavailable(
        ProviderKind provider,
        KnownFeatureId feature,
        SourceOfAuthority source,
        string reason,
        DateTimeOffset lastCheckedUtc) =>
        Create(provider, feature, false, false, false, false, false, source, null, reason, lastCheckedUtc);

    public static ProviderCapability Unauthenticated(
        ProviderKind provider,
        KnownFeatureId feature,
        SourceOfAuthority source,
        string reason,
        DateTimeOffset lastCheckedUtc) =>
        Create(provider, feature, true, false, false, false, false, source, null, reason, lastCheckedUtc);

    public static ProviderCapability Unauthorized(
        ProviderKind provider,
        KnownFeatureId feature,
        SourceOfAuthority source,
        string missingPermission,
        bool canRead,
        DateTimeOffset lastCheckedUtc) =>
        Create(provider, feature, true, true, false, canRead, false, source, missingPermission, null, lastCheckedUtc);

    public static ProviderCapability ReadOnly(
        ProviderKind provider,
        KnownFeatureId feature,
        SourceOfAuthority source,
        string reason,
        DateTimeOffset lastCheckedUtc) =>
        Create(provider, feature, true, true, true, true, false, source, null, reason, lastCheckedUtc);

    public static ProviderCapability ReadWrite(
        ProviderKind provider,
        KnownFeatureId feature,
        SourceOfAuthority source,
        DateTimeOffset lastCheckedUtc) =>
        Create(provider, feature, true, true, true, true, true, source, null, null, lastCheckedUtc);

    public static ProviderCapability Unsupported(
        ProviderKind provider,
        KnownFeatureId feature,
        SourceOfAuthority source,
        string reason,
        DateTimeOffset lastCheckedUtc) =>
        Create(provider, feature, true, true, true, false, false, source, null, reason, lastCheckedUtc);

    internal static bool TryCreate(
        ProviderKind provider,
        KnownFeatureId feature,
        bool available,
        bool authenticated,
        bool authorized,
        bool canRead,
        bool canWrite,
        SourceOfAuthority source,
        string? missingPermission,
        string? unsupportedReason,
        DateTimeOffset lastCheckedUtc,
        out ProviderCapability? capability)
    {
        if (!IsValid(available, authenticated, authorized, canRead, canWrite, missingPermission, unsupportedReason))
        {
            capability = null;
            return false;
        }

        capability = new ProviderCapability(
            provider,
            feature,
            available,
            authenticated,
            authorized,
            canRead,
            canWrite,
            source,
            Normalize(missingPermission),
            Normalize(unsupportedReason),
            lastCheckedUtc);
        return true;
    }

    private static ProviderCapability Create(
        ProviderKind provider,
        KnownFeatureId feature,
        bool available,
        bool authenticated,
        bool authorized,
        bool canRead,
        bool canWrite,
        SourceOfAuthority source,
        string? missingPermission,
        string? unsupportedReason,
        DateTimeOffset lastCheckedUtc)
    {
        if (!TryCreate(
                provider,
                feature,
                available,
                authenticated,
                authorized,
                canRead,
                canWrite,
                source,
                missingPermission,
                unsupportedReason,
                lastCheckedUtc,
                out var capability)
            || capability is null)
        {
            throw new ArgumentException("The capability combination is contradictory or unexplained.");
        }

        return capability;
    }

    private static bool IsValid(
        bool available,
        bool authenticated,
        bool authorized,
        bool canRead,
        bool canWrite,
        string? missingPermission,
        string? unsupportedReason)
    {
        var missing = Normalize(missingPermission);
        var reason = Normalize(unsupportedReason);
        if (!available && (canRead || canWrite))
        {
            return false;
        }

        if (!authenticated && authorized)
        {
            return false;
        }

        if (!authorized && canWrite)
        {
            return false;
        }

        if (reason is not null && canWrite)
        {
            return false;
        }

        if (missing is not null && authorized)
        {
            return false;
        }

        var disabled = !available || !authenticated || !authorized || !canRead || !canWrite;
        if (disabled && missing is null && reason is null)
        {
            return false;
        }

        return true;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
