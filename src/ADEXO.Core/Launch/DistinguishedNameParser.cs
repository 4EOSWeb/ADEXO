using System.Globalization;
using System.Text;
using ADEXO.Contracts.Launch;

namespace ADEXO.Core.Launch;

public static class DistinguishedNameParser
{
    public static bool TryParse(string value, out DistinguishedName? name, out string? error)
    {
        name = null;
        error = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "The distinguished name is empty.";
            return false;
        }

        var rdns = new List<string>();
        var current = new StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            var character = value[i];
            if (character == '\\')
            {
                if (!TryConsumeEscape(value, ref i, current, out error))
                {
                    return false;
                }

                continue;
            }

            if (character is '&' && i + 1 < value.Length && value[i + 1] == '&')
            {
                error = "Trailing command text is not part of the distinguished name.";
                return false;
            }

            if (character is '|' or '`' or '$')
            {
                error = "The distinguished name contains text that is not an LDAP value.";
                return false;
            }

            if (character == ',')
            {
                if (current.Length == 0)
                {
                    error = "The distinguished name has an empty component.";
                    return false;
                }

                rdns.Add(current.ToString());
                current.Clear();
                continue;
            }

            if (char.IsControl(character))
            {
                error = "The distinguished name contains a control character.";
                return false;
            }

            current.Append(character);
        }

        if (current.Length == 0)
        {
            error = "The distinguished name has an empty component.";
            return false;
        }

        rdns.Add(current.ToString());
        foreach (var rdn in rdns)
        {
            if (!IsValidRdn(rdn))
            {
                error = "The distinguished name has a malformed component.";
                return false;
            }
        }

        name = DistinguishedName.FromValidated(string.Join(",", rdns.Select(static part => part.Trim())));
        return true;
    }

    private static bool TryConsumeEscape(string value, ref int index, StringBuilder current, out string? error)
    {
        error = null;
        if (index + 1 >= value.Length)
        {
            error = "The distinguished name has a dangling escape.";
            return false;
        }

        var next = value[index + 1];
        if (next is ',' or '+' or '"' or '\\' or '<' or '>' or ';' or '=' or '#' or ' ' or '(' or ')' or '\'')
        {
            current.Append(next);
            index++;
            return true;
        }

        if (index + 2 < value.Length && IsHex(value[index + 1]) && IsHex(value[index + 2]))
        {
            var decoded = byte.Parse(value.Substring(index + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (decoded == 0)
            {
                error = "The distinguished name contains a null.";
                return false;
            }

            current.Append((char)decoded);
            index += 2;
            return true;
        }

        error = "The distinguished name has a malformed escape.";
        return false;
    }

    private static bool IsValidRdn(string rdn)
    {
        var separator = rdn.IndexOf('=');
        if (separator <= 0 || separator == rdn.Length - 1)
        {
            return false;
        }

        var type = rdn[..separator].Trim();
        var attributeValue = rdn[(separator + 1)..].Trim();
        if (type.Length == 0 || attributeValue.Length == 0)
        {
            return false;
        }

        var oid = type.All(character => char.IsDigit(character) || character == '.');
        var name = char.IsLetter(type[0]) && type.All(character => char.IsLetterOrDigit(character) || character == '-');
        return (oid || name) && attributeValue.Length > 0;
    }

    private static bool IsHex(char character) =>
        character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
}
