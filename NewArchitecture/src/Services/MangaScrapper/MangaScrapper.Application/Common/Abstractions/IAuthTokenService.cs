using MangaScrapper.Domain.Aggregates;

namespace MangaScrapper.Application.Common.Abstractions;

/// <summary>
/// Generates JWT tokens for authenticated users.
/// Implemented in Infrastructure to keep System.IdentityModel out of Application.
/// </summary>
public interface IAuthTokenService
{
    (string Token, DateTime Expiry) GenerateToken(User user, int expiryDays = 7);
}
