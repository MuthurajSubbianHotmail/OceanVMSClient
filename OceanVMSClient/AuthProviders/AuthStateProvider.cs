using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;
using OceanVMSClient.Features;
using System.Net.Http.Headers;
using System.Security.Claims;

namespace OceanVMSClient.AuthProviders
{
    public class AuthStateProvider : AuthenticationStateProvider
    {
        private readonly HttpClient _httpClient;
        private readonly ILocalStorageService _localStorage;
        private readonly AuthenticationState _anonymous;

        public AuthStateProvider(HttpClient httpClient, ILocalStorageService localStorageService)
        {
            _httpClient = httpClient;
            _localStorage = localStorageService;
            _anonymous = new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }

        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var token = await _localStorage.GetItemAsync<string>("authToken");
            if (string.IsNullOrWhiteSpace(token))
                return _anonymous;
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("bearer", token);
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(JwtParser.ParseClaimsFromJwt(token), "jwtAuthType")));

        }

        //public void NotifyUserAuthentication(string email)
        //{
        //    var authenticatedUser = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, email) }, "jwtAuthType"));
        //    var authState = Task.FromResult(new AuthenticationState(authenticatedUser));
        //    NotifyAuthenticationStateChanged(authState);
        //}

        // Now accept email and fullName (first/last optional kept in claims)
        public void NotifyUserAuthentication(string email, string fullName, string? firstName = null, string? lastName = null, string? UserType = null)
        {
            var claims = new List<Claim>();

            if (!string.IsNullOrWhiteSpace(fullName))
                claims.Add(new Claim(ClaimTypes.Name, fullName));

            if (!string.IsNullOrWhiteSpace(email))
                claims.Add(new Claim(ClaimTypes.Email, email));

            if (!string.IsNullOrWhiteSpace(firstName))
                claims.Add(new Claim(ClaimTypes.GivenName, firstName));

            if (!string.IsNullOrWhiteSpace(lastName))
                claims.Add(new Claim(ClaimTypes.Surname, lastName));

            // Fallback: if no name claim at all, add email as Name
            if (!claims.Any(c => c.Type == ClaimTypes.Name) && !string.IsNullOrWhiteSpace(email))
                claims.Add(new Claim(ClaimTypes.Name, email));

            if(!claims.Any(claims => claims.Type == "UserType") && !string.IsNullOrWhiteSpace(UserType))
                claims.Add(new Claim("UserType", UserType));

            var authenticatedUser = new ClaimsPrincipal(new ClaimsIdentity(claims, "jwtAuthType"));
            var authState = Task.FromResult(new AuthenticationState(authenticatedUser));
            NotifyAuthenticationStateChanged(authState);
        }
        public void NotifyUserLogout()
        {
            var authState = Task.FromResult(_anonymous);
            NotifyAuthenticationStateChanged(authState);
        }
    }
}
