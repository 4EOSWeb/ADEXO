using ADEXO.Contracts.Directory;
using ADEXO.Contracts.Identity;
using ADEXO.Contracts.Launch;
using ADEXO.ActiveDirectory.Ldap;

namespace ADEXO.ActiveDirectory;

public static class UserMapper
{
    public static VerifiedDirectoryUser? Map(DirectoryAttributeBag bag, Guid? managerGuid, string? managerDisplayName)
    {
        var guid = bag.Binary.TryGetValue("objectGUID", out var guidBytes) ? AttributeConverter.ReadGuid(guidBytes) : null;
        var distinguishedName = First(bag, "distinguishedName");
        var objectClass = bag.Text.TryGetValue("objectClass", out var classes) ? string.Join(",", classes) : null;
        if (guid is null || string.IsNullOrWhiteSpace(distinguishedName) || string.IsNullOrWhiteSpace(objectClass))
        {
            return null;
        }

        var enabled = AttributeConverter.ReadEnabled(First(bag, "userAccountControl"));
        var expiration = AttributeConverter.ReadAccountExpiration(First(bag, "accountExpires"));
        var password = AttributeConverter.ReadPasswordLastSet(First(bag, "pwdLastSet"));
        var extensions = new Dictionary<string, string?>();
        for (var index = 1; index <= 15; index++)
        {
            var name = "extensionAttribute" + index;
            extensions[name] = First(bag, name);
        }

        return new VerifiedDirectoryUser
        {
            ObjectGuid = guid.Value,
            DistinguishedName = distinguishedName,
            ObjectClass = objectClass,
            DisplayName = First(bag, "displayName"),
            SamAccountName = First(bag, "sAMAccountName"),
            UserPrincipalName = First(bag, "userPrincipalName"),
            ObjectSid = AttributeConverter.ReadSid(bag.Binary.TryGetValue("objectSid", out var sid) ? sid : null),
            CanonicalName = First(bag, "canonicalName"),
            Name = First(bag, "name"),
            GivenName = First(bag, "givenName"),
            Surname = First(bag, "sn"),
            Initials = First(bag, "initials"),
            Enabled = enabled,
            ExpirationKind = expiration.Kind,
            ExpirationUtc = expiration.Date,
            PasswordLastSetKind = password.Kind,
            PasswordLastSetUtc = password.Date,
            Lockout = AttributeConverter.ReadLockout(First(bag, "lockoutTime")),
            WhenCreatedUtc = AttributeConverter.ReadGeneralizedTime(First(bag, "whenCreated")),
            WhenChangedUtc = AttributeConverter.ReadGeneralizedTime(First(bag, "whenChanged")),
            Mail = First(bag, "mail"),
            TelephoneNumber = First(bag, "telephoneNumber"),
            Mobile = First(bag, "mobile"),
            IpPhone = First(bag, "ipPhone"),
            StreetAddress = First(bag, "streetAddress"),
            City = First(bag, "l"),
            State = First(bag, "st"),
            PostalCode = First(bag, "postalCode"),
            Country = First(bag, "co"),
            Title = First(bag, "title"),
            Department = First(bag, "department"),
            Company = First(bag, "company"),
            Office = First(bag, "physicalDeliveryOfficeName"),
            ManagerDistinguishedName = First(bag, "manager"),
            ManagerObjectGuid = managerGuid,
            ManagerDisplayName = managerDisplayName,
            MailNickname = First(bag, "mailNickname"),
            ProxyAddresses = bag.Text.TryGetValue("proxyAddresses", out var proxies) ? proxies : [],
            TargetAddress = First(bag, "targetAddress"),
            HiddenFromAddressLists = First(bag, "msExchHideFromAddressLists"),
            ExtensionAttributes = extensions,
            Stage = IdentityStage.DisplayDataLoaded,
        };
    }

    public static bool IncludesUser(DirectoryAttributeBag bag)
    {
        if (!bag.Text.TryGetValue("objectClass", out var classes))
        {
            return false;
        }

        return classes.Any(value => value.Equals("user", StringComparison.OrdinalIgnoreCase));
    }

    private static string? First(DirectoryAttributeBag bag, string name) =>
        bag.Text.TryGetValue(name, out var values) && values.Count > 0 ? values[0] : null;
}
