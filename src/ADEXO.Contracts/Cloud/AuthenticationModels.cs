namespace ADEXO.Contracts.Cloud;

/// <summary>
/// Lifecycle of the Microsoft 365 sign-in for the current session. This never carries token material.
/// </summary>
public enum AuthenticationState
{
    /// <summary>No tenant or client identifier is configured, so sign-in is not offered.</summary>
    NotConfigured,
    SignedOut,
    Authenticating,
    Authenticated,

    /// <summary>The token or the directory reported a tenant other than the approved one.</summary>
    TenantMismatch,

    /// <summary>The operator dismissed the sign-in dialog.</summary>
    Canceled,
    Failed,
}

/// <summary>
/// The signed-in administrator, reduced to display and lock data. There is deliberately no token member,
/// so no caller can obtain an access token from this type.
/// </summary>
public sealed class AuthenticatedAccount
{
    private AuthenticatedAccount(string? accountObjectId, Guid tenantId, string? username, bool brokerUsed)
    {
        AccountObjectId = accountObjectId;
        TenantId = tenantId;
        Username = username;
        BrokerUsed = brokerUsed;
    }

    /// <summary>Entra object identifier of the signed-in administrator, when the broker reports one.</summary>
    public string? AccountObjectId { get; }

    /// <summary>Tenant the token was issued for. This is the T1 lock value.</summary>
    public Guid TenantId { get; }

    /// <summary>Sign-in name, for display only. It is never used as an identity key.</summary>
    public string? Username { get; }

    /// <summary>True when the interactive experience ran through the Windows broker.</summary>
    public bool BrokerUsed { get; }

    public static AuthenticatedAccount Create(string? accountObjectId, Guid tenantId, string? username, bool brokerUsed)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("The token tenant identifier is required.", nameof(tenantId));
        }

        return new AuthenticatedAccount(
            string.IsNullOrWhiteSpace(accountObjectId) ? null : accountObjectId.Trim(),
            tenantId,
            string.IsNullOrWhiteSpace(username) ? null : username.Trim(),
            brokerUsed);
    }
}

/// <summary>
/// The outcome of the two tenant locks. T1 compares the token tenant. T2 compares the directory tenant
/// returned by the organization read. Both must hold before any selected-user query is allowed.
/// </summary>
public sealed class TenantVerificationResult
{
    private TenantVerificationResult(
        bool tokenTenantMatched,
        bool directoryTenantMatched,
        Guid? observedTokenTenantId,
        Guid? observedDirectoryTenantId,
        Guid approvedTenantId,
        string? summary)
    {
        TokenTenantMatched = tokenTenantMatched;
        DirectoryTenantMatched = directoryTenantMatched;
        ObservedTokenTenantId = observedTokenTenantId;
        ObservedDirectoryTenantId = observedDirectoryTenantId;
        ApprovedTenantId = approvedTenantId;
        Summary = summary;
    }

    /// <summary>T1. The token was issued for the approved tenant.</summary>
    public bool TokenTenantMatched { get; }

    /// <summary>T2. The directory reported the approved tenant.</summary>
    public bool DirectoryTenantMatched { get; }

    public Guid? ObservedTokenTenantId { get; }

    public Guid? ObservedDirectoryTenantId { get; }

    public Guid ApprovedTenantId { get; }

    /// <summary>
    /// True only when both locks hold. Every cloud read of a selected user requires this to be true,
    /// so a partial or failed lock cannot produce a user query.
    /// </summary>
    public bool IsVerified => TokenTenantMatched && DirectoryTenantMatched;

    /// <summary>A sanitized explanation. It never contains a token, and never contains user data.</summary>
    public string? Summary { get; }

    public static TenantVerificationResult Verified(Guid tokenTenantId, Guid directoryTenantId, Guid approvedTenantId) =>
        new(true, true, tokenTenantId, directoryTenantId, approvedTenantId, null);

    public static TenantVerificationResult TokenMismatch(Guid? tokenTenantId, Guid approvedTenantId) =>
        new(false, false, tokenTenantId, null, approvedTenantId, "The signed-in session belongs to a different tenant than the configured one.");

    /// <summary>
    /// T1 held but the directory reported something else. This is an identity failure rather than a
    /// configuration mistake, so it is surfaced distinctly instead of looking like a wrong sign-in.
    /// </summary>
    public static TenantVerificationResult DirectoryMismatch(Guid tokenTenantId, Guid? directoryTenantId, Guid approvedTenantId) =>
        new(true, false, tokenTenantId, directoryTenantId, approvedTenantId, "The directory reported a different tenant than the approved one. The cloud reads were stopped.");

    public static TenantVerificationResult Unavailable(Guid? tokenTenantId, Guid approvedTenantId, string reason) =>
        new(false, false, tokenTenantId, null, approvedTenantId, reason);
}
