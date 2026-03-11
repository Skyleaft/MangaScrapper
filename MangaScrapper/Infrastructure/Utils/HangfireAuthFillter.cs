using Hangfire.Dashboard;

namespace MangaScrapper.Infrastructure.Utils;

public class HangfireAuthFillter: IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        return httpContext.User.Identity?.IsAuthenticated ?? false;
    }
}