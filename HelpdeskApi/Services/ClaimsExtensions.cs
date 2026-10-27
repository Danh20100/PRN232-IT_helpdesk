using System.Security.Claims;

namespace HelpdeskApi.Services;

public static class ClaimsExtensions
{
    public static int GetId(this ClaimsPrincipal u) => int.Parse(u.FindFirst("sub")!.Value);
    public static Role GetRole(this ClaimsPrincipal u) => Enum.Parse<Role>(u.FindFirst("role")!.Value);
}
