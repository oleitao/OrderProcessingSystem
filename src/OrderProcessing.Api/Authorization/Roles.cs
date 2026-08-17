namespace OrderProcessing.Api.Authorization;

// Role names as issued by OrderProcessing.IdentityService's JWTs. Duplicated here rather than
// shared via a referenced project — Api and IdentityService are deliberately decoupled services
// (see .github/AGENT.md, section 2.1), so this is the accepted cost of that boundary.
public static class Roles
{
    public const string Customer = "Customer";
    public const string Admin = "Admin";
}
