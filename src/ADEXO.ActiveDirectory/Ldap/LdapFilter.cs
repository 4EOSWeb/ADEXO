using System.Globalization;
using System.Text;

namespace ADEXO.ActiveDirectory.Ldap;

public static class LdapFilter
{
    public static string Escape(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (character is '\\' or '*' or '(' or ')' or '\0')
            {
                builder.Append('\\');
                builder.Append(((byte)character).ToString("X2", CultureInfo.InvariantCulture));
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    public static string ObjectGuidFilter(Guid objectGuid)
    {
        var bytes = objectGuid.ToByteArray();
        var builder = new StringBuilder("(objectGUID=");
        foreach (var item in bytes)
        {
            builder.Append('\\');
            builder.Append(item.ToString("X2", CultureInfo.InvariantCulture));
        }

        builder.Append(')');
        return builder.ToString();
    }
}
