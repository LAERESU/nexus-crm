using NexUS.User.Domain.Users;

namespace NexUS.User.Application.Users;

/// <summary>Nama status di API (huruf kecil) dan kolom STATUS (huruf besar).</summary>
public static class UserStatusNames
{
    public static bool TryParse(string value, out UserStatus status)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "active": status = UserStatus.Active; return true;
            case "inactive": status = UserStatus.Inactive; return true;
            default: status = default; return false;
        }
    }

    public static string ToApi(UserStatus status) => status == UserStatus.Active ? "active" : "inactive";
}
