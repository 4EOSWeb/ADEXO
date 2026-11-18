using System.Text;

namespace ADEXO.Core.Correlation;

/// <summary>Which on-premises attribute an immutable identifier was derived from.</summary>
public enum ImmutableIdSource
{
    /// <summary>The object's <c>objectGUID</c>. This is the default source for directory synchronization.</summary>
    ObjectGuid,

    /// <summary>
    /// The <c>mS-DS-ConsistencyGuid</c> attribute. Deployments configured to source the immutable
    /// identifier from it use this value instead of the object GUID.
    /// </summary>
    ConsistencyGuid,
}

/// <summary>
/// One candidate synchronized immutable identifier, with the evidence needed to explain it later.
/// The raw bytes are retained so a mismatch can be diagnosed without re-reading Active Directory.
/// </summary>
public sealed class ImmutableIdCandidate
{
    private ImmutableIdCandidate(ImmutableIdSource source, byte[] rawBytes, string value)
    {
        Source = source;
        RawBytes = rawBytes;
        Value = value;
    }

    public ImmutableIdSource Source { get; }

    /// <summary>The 16 bytes as Active Directory stored them, in the order the transform consumed.</summary>
    public byte[] RawBytes { get; }

    /// <summary>The Base64 representation that a synchronized directory would carry.</summary>
    public string Value { get; }

    /// <summary>
    /// Builds a candidate from <c>objectGUID</c>. The byte layout comes from the GUID's own little-endian
    /// representation, which is what directory synchronization encodes.
    /// </summary>
    public static ImmutableIdCandidate FromObjectGuid(Guid objectGuid)
    {
        if (objectGuid == Guid.Empty)
        {
            throw new ArgumentException("An objectGUID is required to derive an immutable identifier.", nameof(objectGuid));
        }

        var bytes = objectGuid.ToByteArray();
        return new ImmutableIdCandidate(ImmutableIdSource.ObjectGuid, bytes, Convert.ToBase64String(bytes));
    }

    /// <summary>
    /// Builds a candidate from <c>mS-DS-ConsistencyGuid</c>. Returns null when the attribute is absent
    /// or is not exactly the 16 bytes the transform requires, rather than throwing, because a missing
    /// consistency GUID is an ordinary state and not a failure.
    /// </summary>
    public static ImmutableIdCandidate? FromConsistencyGuid(byte[]? consistencyGuid)
    {
        if (consistencyGuid is null || consistencyGuid.Length != 16)
        {
            return null;
        }

        if (consistencyGuid.All(b => b == 0))
        {
            return null;
        }

        return new ImmutableIdCandidate(
            ImmutableIdSource.ConsistencyGuid,
            consistencyGuid,
            Convert.ToBase64String(consistencyGuid));
    }

    /// <summary>
    /// Escapes a value for use inside an OData string literal. Both the single quote and the
    /// backslash are escaped, so a value can never terminate the literal early.
    /// </summary>
    public static string EscapeODataLiteral(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var builder = new StringBuilder(value.Length + 8);
        foreach (var character in value)
        {
            switch (character)
            {
                case '\'':
                    builder.Append("''");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        return builder.ToString();
    }
}

/// <summary>
/// Derives the candidate immutable identifiers for one Active Directory object. The object GUID is
/// always a candidate. The consistency GUID is a candidate only when the read supplied a valid value.
/// </summary>
public static class ImmutableIdCandidateFactory
{
    public static IReadOnlyList<ImmutableIdCandidate> Create(Guid objectGuid, byte[]? consistencyGuid)
    {
        var candidates = new List<ImmutableIdCandidate> { ImmutableIdCandidate.FromObjectGuid(objectGuid) };

        var consistency = ImmutableIdCandidate.FromConsistencyGuid(consistencyGuid);
        if (consistency is not null
            && !string.Equals(consistency.Value, candidates[0].Value, StringComparison.Ordinal))
        {
            candidates.Add(consistency);
        }

        return candidates;
    }
}
