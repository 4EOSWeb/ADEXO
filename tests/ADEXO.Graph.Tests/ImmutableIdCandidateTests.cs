using ADEXO.Core.Correlation;

namespace ADEXO.Graph.Tests;

/// <summary>
/// Known vectors for the immutable identifier. The expected Base64 strings are written out literally
/// rather than produced by the helper under test, so a bug in the helper cannot make its own test pass.
/// </summary>
public sealed class ImmutableIdCandidateTests
{
    // objectGUID with a distinctive byte pattern. The GUID's own little-endian layout is what
    // directory synchronization encodes, so the first four groups appear byte-reversed.
    private static readonly Guid KnownGuid = new("00112233-4455-6677-8899-aabbccddeeff");

    // Base64 of bytes 33 22 11 00 55 44 77 66 88 99 aa bb cc dd ee ff.
    private const string KnownGuidBase64 = "MyIRAFVEd2aImaq7zN3u/w==";

    [Fact]
    public void Object_guid_encodes_using_the_guid_byte_layout()
    {
        var candidate = ImmutableIdCandidate.FromObjectGuid(KnownGuid);

        Assert.Equal(ImmutableIdSource.ObjectGuid, candidate.Source);
        Assert.Equal(KnownGuidBase64, candidate.Value);
        Assert.Equal(16, candidate.RawBytes.Length);
        Assert.Equal(Convert.FromBase64String(KnownGuidBase64), candidate.RawBytes);
    }

    [Fact]
    public void Object_guid_round_trips_through_the_encoded_value()
    {
        var candidate = ImmutableIdCandidate.FromObjectGuid(KnownGuid);

        var restored = new Guid(Convert.FromBase64String(candidate.Value));

        Assert.Equal(KnownGuid, restored);
    }

    [Fact]
    public void Consistency_guid_encodes_when_present()
    {
        var bytes = Convert.FromBase64String(KnownGuidBase64);

        var candidate = ImmutableIdCandidate.FromConsistencyGuid(bytes);

        Assert.NotNull(candidate);
        Assert.Equal(ImmutableIdSource.ConsistencyGuid, candidate.Source);
        Assert.Equal(KnownGuidBase64, candidate.Value);
    }

    [Fact]
    public void Missing_consistency_guid_yields_no_candidate()
    {
        Assert.Null(ImmutableIdCandidate.FromConsistencyGuid(null));
        Assert.Null(ImmutableIdCandidate.FromConsistencyGuid([]));
    }

    [Fact]
    public void Malformed_consistency_guid_is_rejected_rather_than_encoded()
    {
        Assert.Null(ImmutableIdCandidate.FromConsistencyGuid(new byte[15]));
        Assert.Null(ImmutableIdCandidate.FromConsistencyGuid(new byte[17]));
    }

    [Fact]
    public void An_all_zero_consistency_guid_is_treated_as_absent()
    {
        // Active Directory can hold a zeroed attribute. That is "no value", not an identifier.
        Assert.Null(ImmutableIdCandidate.FromConsistencyGuid(new byte[16]));
    }

    [Fact]
    public void Empty_object_guid_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => ImmutableIdCandidate.FromObjectGuid(Guid.Empty));
    }

    [Fact]
    public void Factory_always_offers_the_object_guid()
    {
        var candidates = ImmutableIdCandidateFactory.Create(KnownGuid, consistencyGuid: null);

        Assert.Single(candidates);
        Assert.Equal(ImmutableIdSource.ObjectGuid, candidates[0].Source);
    }

    [Fact]
    public void Factory_adds_a_distinct_consistency_guid_as_a_second_candidate()
    {
        var otherBytes = Convert.FromBase64String("AAAAAAAAAAAAAAAAAAAAAA==");
        otherBytes[0] = 1;

        var candidates = ImmutableIdCandidateFactory.Create(KnownGuid, otherBytes);

        Assert.Equal(2, candidates.Count);
        Assert.Equal(ImmutableIdSource.ObjectGuid, candidates[0].Source);
        Assert.Equal(ImmutableIdSource.ConsistencyGuid, candidates[1].Source);
        Assert.NotEqual(candidates[0].Value, candidates[1].Value);
    }

    [Fact]
    public void Factory_does_not_duplicate_identical_values()
    {
        var sameBytes = Convert.FromBase64String(KnownGuidBase64);

        var candidates = ImmutableIdCandidateFactory.Create(KnownGuid, sameBytes);

        Assert.Single(candidates);
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("o'brien", "o''brien")]
    [InlineData(@"a\b", @"a\\b")]
    [InlineData(@"o'brien\test", @"o''brien\\test")]
    public void Odata_literals_are_escaped(string input, string expected)
    {
        Assert.Equal(expected, ImmutableIdCandidate.EscapeODataLiteral(input));
    }

    [Fact]
    public void Odata_escaping_cannot_terminate_the_literal()
    {
        var escaped = ImmutableIdCandidate.EscapeODataLiteral("x' or onPremisesImmutableId ne 'y");

        // Every quote is doubled, so the literal cannot be closed early. A doubled quote followed by
        // " or " is still literal text, not an injection.
        Assert.Equal("x'' or onPremisesImmutableId ne ''y", escaped);
        Assert.DoesNotContain("x' or", escaped, StringComparison.Ordinal);
        Assert.Equal(4, escaped.Count(c => c == '\''));
    }
}
