using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;
using OceanVMSClient.Features;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Security.Claims;

namespace OceanVMSClient.AuthProviders
{
    public class AuthStateProvider : AuthenticationStateProvider
    {

        private readonly HttpClient _httpClient;
        private readonly ILocalStorageService _localStorageService;
        private readonly AuthenticationState _anonymous;
        public AuthStateProvider(HttpClient httpClient, ILocalStorageService localStorage)
        {
            _httpClient = httpClient;
            _localStorageService = localStorage;
            _anonymous = new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }

        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var token = await _localStorageService.GetItemAsStringAsync("authToken");
            var identity = new ClaimsIdentity();
            _httpClient.DefaultRequestHeaders.Authorization = null;
            if (!string.IsNullOrEmpty(token))
            {
                try
                {
                    identity = new ClaimsIdentity(JwtParser.ParseClaimsFromJwt(token), "jwt");

                    // add first/last/vendor name from local storage if present (JWT may not contain them)
                    var firstName = await _localStorageService.GetItemAsync<string>("firstName");
                    var lastName = await _localStorageService.GetItemAsync<string>("lastName");
                    var vendorName = await _localStorageService.GetItemAsync<string>("vendorName");
                    var userType = await _localStorageService.GetItemAsync<string>("userType");
                    if (!string.IsNullOrEmpty(userType))
                    {
                        identity.AddClaim(new Claim("UserType", userType));
                    }
                    var vendorId = await _localStorageService.GetItemAsync<string>("vendorPK");
                    if (!string.IsNullOrEmpty(vendorId))
                    {
                        identity.AddClaim(new Claim("VendorId", vendorId));
                    }
                    var vendorContactId = await _localStorageService.GetItemAsync<string>("vendorContactPK");
                    if (!string.IsNullOrEmpty(vendorContactId))
                    {
                        identity.AddClaim(new Claim("VendorContactId", vendorContactId));
                    }
                    var employeeId = await _localStorageService.GetItemAsync<string>("empPK");
                    if (!string.IsNullOrEmpty(employeeId)) {
                        identity.AddClaim(new Claim("EmployeeId", employeeId));
                    }

                    if (!string.IsNullOrEmpty(firstName))
                    {
                        identity.AddClaim(new Claim("FirstName", firstName));
                        identity.AddClaim(new Claim(ClaimTypes.GivenName, firstName));
                    }
                    if (!string.IsNullOrEmpty(lastName))
                    {
                        identity.AddClaim(new Claim("LastName", lastName));
                        identity.AddClaim(new Claim(ClaimTypes.Surname, lastName));
                    }
                    if (!string.IsNullOrEmpty(vendorName))
                    {
                        identity.AddClaim(new Claim("VendorName", vendorName));
                    }

                    _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                }
                catch
                {
                    await _localStorageService.RemoveItemAsync("authToken");
                    identity = new ClaimsIdentity();
                    _httpClient.DefaultRequestHeaders.Authorization = null;
                }
            }
            var user = new ClaimsPrincipal(identity);
            return await Task.FromResult(new AuthenticationState(user));
        }

        // Accept token and optional first/last/vendor name so we can include them as claims immediately after login
        public void NotifyUserAuthentication(string token, string? firstName = null, string? lastName = null, 
            string? vendorName = null, string? userType = null,
            string? fullName = null, Guid? vendorId = null, Guid? vendorContactId = null, Guid? employeeId = null)
        {
            var claims = JwtParser.ParseClaimsFromJwt(token).ToList();

            if (!string.IsNullOrEmpty(firstName))
            {
                claims.Add(new Claim("FirstName", firstName));
                claims.Add(new Claim(ClaimTypes.GivenName, firstName));
            }

            if (!string.IsNullOrEmpty(lastName))
            {
                claims.Add(new Claim("LastName", lastName));
                claims.Add(new Claim(ClaimTypes.Surname, lastName));
            }

            if (!string.IsNullOrEmpty(vendorName))
            {
                claims.Add(new Claim("VendorName", vendorName));
            }

            if (!string.IsNullOrEmpty(userType))
            {
                claims.Add(new Claim("UserType", userType));
            }

            if (!string.IsNullOrEmpty(fullName)) { 
                claims.Add(new Claim("FullName", fullName));
            }
            if (vendorId.HasValue)
            {
                claims.Add(new Claim("VendorId", vendorId.Value.ToString()));
            }
            if (vendorContactId.HasValue)
            {
                claims.Add(new Claim("VendorContactId", vendorContactId.Value.ToString()));
            }
            if (employeeId.HasValue)
            {
                claims.Add(new Claim("EmployeeId", employeeId.Value.ToString()));
            }
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
