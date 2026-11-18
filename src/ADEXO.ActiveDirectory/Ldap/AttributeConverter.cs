using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using ADEXO.Contracts.Directory;

namespace ADEXO.ActiveDirectory.Ldap;

public static class AttributeConverter
{
    public const int AccountDisableFlag = 0x2;

    public static Guid? ReadGuid(byte[]? value)
    {
        if (value is not { Length: 16 })
        {
            return null;
        }

        return new Guid(value);
    }

    public static string? ReadSid(byte[]? value)
    {
        if (value is not { Length: >= 8 })
        {
            return null;
        }

        var revision = value[0];
        var count = value[1];
        if (value.Length < 8 + (count * 4))
        {
            return null;
        }

        long authority = 0;
        for (var i = 2; i < 8; i++)
        {
            authority = (authority << 8) | value[i];
        }

        var builder = new StringBuilder("S-").Append(revision).Append('-').Append(authority);
        for (var i = 0; i < count; i++)
        {
            var offset = 8 + (i * 4);
            var sub = BinaryPrimitives.ReadUInt32LittleEndian(value.AsSpan(offset, 4));
            builder.Append('-').Append(sub);
        }

        return builder.ToString();
    }

    public static AccountEnabledState ReadEnabled(string? userAccountControl)
    {
        if (!int.TryParse(userAccountControl, NumberStyles.Integer, CultureInfo.InvariantCulture, out var flags))
        {
            return AccountEnabledState.Unavailable;
        }

        return (flags & AccountDisableFlag) == AccountDisableFlag
            ? AccountEnabledState.Disabled
            : AccountEnabledState.Enabled;
    }

    public static (AccountExpirationKind Kind, DateTimeOffset? Date) ReadAccountExpiration(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return (AccountExpirationKind.Unavailable, null);
        }

        if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fileTime))
        {
            return (AccountExpirationKind.Invalid, null);
        }

        if (fileTime is 0 or long.MaxValue)
        {
            return (AccountExpirationKind.Never, null);
        }

        try
        {
            return (AccountExpirationKind.Date, new DateTimeOffset(DateTime.FromFileTimeUtc(fileTime), TimeSpan.Zero));
        }
        catch (ArgumentOutOfRangeException)
        {
            return (AccountExpirationKind.Invalid, null);
        }
    }

    public static (PasswordLastSetKind Kind, DateTimeOffset? Date) ReadPasswordLastSet(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return (PasswordLastSetKind.Unavailable, null);
        }

        if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fileTime))
        {
            return (PasswordLastSetKind.Unavailable, null);
        }

        if (fileTime == 0)
        {
            return (PasswordLastSetKind.MustChangeAtNextLogon, null);
        }

        try
        {
            return (PasswordLastSetKind.Date, new DateTimeOffset(DateTime.FromFileTimeUtc(fileTime), TimeSpan.Zero));
        }
        catch (ArgumentOutOfRangeException)
        {
            return (PasswordLastSetKind.Unavailable, null);
        }
    }

    public static LockoutIndication ReadLockout(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return LockoutIndication.Unavailable;
        }

        if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fileTime) || fileTime == 0)
        {
            return LockoutIndication.NotIndicated;
        }

        return LockoutIndication.TimeSetOnThisReplica;
    }

    public static DateTimeOffset? ReadGeneralizedTime(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var text = raw.TrimEnd('Z', 'z');
        var formats = new[] { "yyyyMMddHHmmss.f", "yyyyMMddHHmmss.ff", "yyyyMMddHHmmss.fff", "yyyyMMddHHmmss" };
        if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Utc), TimeSpan.Zero);
        }

        return null;
    }
}
