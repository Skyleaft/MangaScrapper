using Hangfire.Dashboard;
using MangaScrapper.Core.Aggregates;

namespace MangaScrapper.Core.Security;

/// <summary>
/// Hangfire dashboard authorization filter — only authenticated users with administrative privileges can access the dashboard.
/// </summary>
public class HangfireAuthFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        if (httpContext?.User?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        return httpContext.User.IsInRole(User.UserRoles.SuperUser) ||
               httpContext.User.IsInRole(User.UserRoles.Admin);
    }
}
