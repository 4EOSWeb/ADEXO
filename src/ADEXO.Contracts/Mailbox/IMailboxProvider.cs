using ADEXO.Contracts.Mailbox;
using ADEXO.Contracts.Results;

namespace ADEXO.Contracts.Mailbox;

/// <summary>
/// Read-only Exchange Online access for Phase 3B.
/// <para>
/// Every member is a typed read keyed by the verified Entra object identifier. There is no member that
/// accepts a script, a command name, a UPN, an SMTP address, or an alias, so a caller cannot resolve a
/// mailbox by a mutable name or run arbitrary PowerShell.
/// </para>
/// </summary>
public interface IMailboxProvider
{
    Task<Contracts.Capabilities.ProviderCapability> GetCapabilityAsync(
        Contracts.Features.KnownFeatureId feature,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads the mailbox for a verified Entra object. The identifier is the identity; the returned
    /// object must report the same identifier or the result is a conflict.
    /// </summary>
    Task<ProviderResult<MailboxDetails>> GetMailboxAsync(
        Guid entraObjectId,
        CancellationToken cancellationToken);

    /// <summary>Reads mailbox operation statistics. Missing statistics are not a missing mailbox.</summary>
    Task<ProviderResult<MailboxUsage>> GetUsageAsync(
        MailboxDetails mailbox,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads the delegation kinds whose Exchange role requirement has been verified. Kinds that were
    /// not verified are reported as deferred rather than guessed at.
    /// </summary>
    Task<ProviderResult<DelegationReadResult>> GetDelegationsAsync(
        MailboxDetails mailbox,
        CancellationToken cancellationToken);
}
