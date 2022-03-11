using System;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using IdentityModel.AspNetCore.AccessTokenManagement;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Clients.Bff.InMemoryTests
{
    /// <summary>
    /// Token store using the ASP.NET Core authentication session
    /// </summary>
    public class FakeAuthenticationSessionUserAccessTokenStore : IUserAccessTokenStore
    {
        private const string TokenPrefix = ".Token.";

        private readonly IHttpContextAccessor _contextAccessor;
        private readonly ILogger<AuthenticationSessionUserAccessTokenStore> _logger;

        /// <summary>
        /// ctor
        /// </summary>
        /// <param name="contextAccessor"></param>
        /// <param name="logger"></param>
        public FakeAuthenticationSessionUserAccessTokenStore(
            IHttpContextAccessor contextAccessor,
            ILogger<AuthenticationSessionUserAccessTokenStore> logger)
        {
            _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<UserAccessToken> GetTokenAsync(ClaimsPrincipal user, UserAccessTokenParameters parameters = null)
        {
            parameters ??= new UserAccessTokenParameters();
            var result = await _contextAccessor.HttpContext.AuthenticateAsync(parameters.SignInScheme);

            if (!result.Succeeded)
            {
                _logger.LogInformation("Cannot authenticate scheme: {scheme}", parameters.SignInScheme ?? "default signin scheme");

                return null;
            }

            if (result.Properties == null)
            {
                _logger.LogInformation("Authentication result properties are null for scheme: {scheme}",
                    parameters.SignInScheme ?? "default signin scheme");

                return null;
            }

            var tokens = result.Properties.Items.Where(i => i.Key.StartsWith(TokenPrefix)).ToList();
            if (tokens == null || !tokens.Any())
            {
                _logger.LogInformation("No tokens found in cookie properties. SaveTokens must be enabled for automatic token refresh.");

                return null;
            }

            var tokenName = $"{TokenPrefix}{OpenIdConnectParameterNames.AccessToken}";
            if (!string.IsNullOrEmpty(parameters.Resource))
            {
                tokenName += $"::{parameters.Resource}";
            }

            var refreshTokenName = $"{TokenPrefix}{OpenIdConnectParameterNames.RefreshToken}";
            if (!string.IsNullOrEmpty(parameters.ChallengeScheme))
            {
                refreshTokenName += $"::{parameters.ChallengeScheme}";
            }

            var expiresName = $"{TokenPrefix}expires_at";
            if (!string.IsNullOrEmpty(parameters.Resource))
            {
                expiresName += $"::{parameters.Resource}";
            }

            var accessToken = tokens.SingleOrDefault(t => t.Key == tokenName);
            var refreshToken = tokens.SingleOrDefault(t => t.Key == refreshTokenName);
            var expiresAt = tokens.SingleOrDefault(t => t.Key == expiresName);

            DateTimeOffset? dtExpires = null;
            if (expiresAt.Value != null)
            {
                dtExpires = DateTimeOffset.Parse(expiresAt.Value, CultureInfo.InvariantCulture);
            }

            return new UserAccessToken
            {
                AccessToken = accessToken.Value,
                RefreshToken = refreshToken.Value,
                Expiration = dtExpires
            };
        }

        /// <inheritdoc/>
        public async Task StoreTokenAsync(ClaimsPrincipal user, string accessToken, DateTimeOffset expiration,
            string refreshToken = null, UserAccessTokenParameters parameters = null)
        {
            parameters ??= new UserAccessTokenParameters();
            var result = await _contextAccessor.HttpContext.AuthenticateAsync(parameters.SignInScheme);

            if (!result.Succeeded)
            {
                throw new Exception("Can't store tokens. User is anonymous");
            }

            // in case you want to filter certain claims before re-issuing the authentication session
            var transformedPrincipal = await FilterPrincipalAsync(result.Principal);

            var tokenName = OpenIdConnectParameterNames.AccessToken;
            if (!string.IsNullOrEmpty(parameters.Resource))
            {
                tokenName += $"::{parameters.Resource}";
            }

            var refreshTokenName = $"{OpenIdConnectParameterNames.RefreshToken}";
            if (!string.IsNullOrEmpty(parameters.ChallengeScheme))
            {
                refreshTokenName += $"::{parameters.ChallengeScheme}";
            }

            var expiresName = "expires_at";
            if (!string.IsNullOrEmpty(parameters.Resource))
            {
                expiresName += $"::{parameters.Resource}";
            }

            result.Properties.Items[$".Token.{tokenName}"] = accessToken;
            result.Properties.Items[$".Token.{expiresName}"] = expiration.ToString("o", CultureInfo.InvariantCulture);

            if (refreshToken != null)
            {
                if (!result.Properties.UpdateTokenValue(refreshTokenName, refreshToken))
                {
                    result.Properties.Items[$"{TokenPrefix}{refreshTokenName}"] = refreshToken;
                }
            }

            await _contextAccessor.HttpContext.SignInAsync(parameters.SignInScheme, transformedPrincipal, result.Properties);
        }

        /// <inheritdoc/>
        public Task ClearTokenAsync(ClaimsPrincipal user, UserAccessTokenParameters parameters = null)
        {
            // todo
            return Task.CompletedTask;
        }

        /// <summary>
        /// Allows transforming the principal before re-issuing the authentication session
        /// </summary>
        /// <param name="principal"></param>
        /// <returns></returns>
        protected virtual Task<ClaimsPrincipal> FilterPrincipalAsync(ClaimsPrincipal principal)
        {
            return Task.FromResult(principal);
        }
    }
}