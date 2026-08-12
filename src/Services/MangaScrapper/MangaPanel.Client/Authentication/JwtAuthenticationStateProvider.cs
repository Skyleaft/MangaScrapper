using System.Net.Http.Json;
using System.Security.Claims;
using NovaStack.Contracts.Responses;
using Microsoft.AspNetCore.Components.Authorization;

namespace MangaPanel.Client.Authentication;

public class JwtAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly HttpClient _http;

    public JwtAuthenticationStateProvider(HttpClient http)
    {
        _http = http;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            var apiResponse = await _http.GetFromJsonAsync<ApiResponse<UserInfoResponse>>("api/v1/auth/me");
            var userInfo = apiResponse?.Data;

            if (userInfo == null || !userInfo.IsAuthenticated)
            {
                return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
            }

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userInfo.UserId),
                new Claim(ClaimTypes.Name, userInfo.Username),
                new Claim("Username", userInfo.Username),
                new Claim(ClaimTypes.Email,userInfo.Email)
            };
            
            foreach (var role in userInfo.Roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var identity = new ClaimsIdentity(claims, "Cookie");
            return new AuthenticationState(new ClaimsPrincipal(identity));
        }
        catch
        {
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }
    }

    public void NotifyUserAuthentication()
    {
        // When using cookies, we just notify that the state might have changed
        // and GetAuthenticationStateAsync will be called again to fetch the new state.
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public void NotifyUserLogout()
    {
        var anonymousUser = new ClaimsPrincipal(new ClaimsIdentity());
        var authState = Task.FromResult(new AuthenticationState(anonymousUser));
        NotifyAuthenticationStateChanged(authState);
    }
}
