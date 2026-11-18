using System.DirectoryServices.Protocols;

namespace ADEXO.ActiveDirectory.Ldap;

/// <summary>
/// A cancellation token does not interrupt an in-flight System.DirectoryServices.Protocols request.
/// The request uses this timeout. A superseded result is discarded and is not described as a canceled network call.
/// </summary>
public sealed class LdapConnectionSecurity
{
    public LdapConnectionSecurity(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        Timeout = timeout;
    }

    public AuthType Authentication { get; } = AuthType.Negotiate;

    public bool Signing { get; } = true;

    public bool Sealing { get; } = true;

    public ReferralChasingOptions Referrals { get; } = ReferralChasingOptions.None;

    public TimeSpan Timeout { get; }

    public const string CancellationBoundary =
        "An in-flight System.DirectoryServices.Protocols request is bounded by its timeout and is not canceled by CancellationToken.";

    public void Apply(LdapConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (Authentication != AuthType.Negotiate)
        {
            throw new InvalidOperationException("ADEXO refuses an authentication type other than Negotiate.");
        }

        connection.AuthType = AuthType.Negotiate;
        connection.Timeout = Timeout;
        connection.SessionOptions.ProtocolVersion = 3;
        connection.SessionOptions.ReferralChasing = Referrals;
        connection.SessionOptions.Signing = Signing;
        connection.SessionOptions.Sealing = Sealing;
    }
}
