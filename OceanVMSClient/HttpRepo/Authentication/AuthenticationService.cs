using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using MudBlazor;
using OceanVMSClient.AuthProviders;
using OceanVMSClient.HttpRepoInterface.Authentication;
using Shared.DTO;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace OceanVMSClient.HttpRepo.Authentication
{
    public class AuthenticationService : IAuthenticationService
    {
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _jsonSerializerOptions;
        private readonly AuthenticationStateProvider _authStateProvider;
        private readonly ILocalStorageService _localStorage;
        private readonly NavigationManager _nav;
        private readonly ISnackbar _snackbar;
        private readonly ILogger<AuthenticationService>? _logger;

        public AuthenticationService(HttpClient httpClient, 
            AuthenticationStateProvider authenticationStateProvider,
            NavigationManager nav,
            ILocalStorageService localStorageService,
            ISnackbar snackbar,
            ILogger<AuthenticationService>? logger = null)
        {
            _httpClient = httpClient;
            _jsonSerializerOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
                PropertyNameCaseInsensitive = true,
            };
            _authStateProvider = authenticationStateProvider;
            _localStorage = localStorageService;
            _nav = nav;
            _snackbar = snackbar;
            _logger = logger;
        }

        public async Task<RegistrationResponseDto> RegisterUser(UserForRegistrationDto userForRegistration)
        {
            var content = JsonSerializer.Serialize(userForRegistration, _jsonSerializerOptions);
            var httpContent = new StringContent(content, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("authentication/register", httpContent);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();

            var result = JsonSerializer.Deserialize<RegistrationResponseDto>(responseContent, _jsonSerializerOptions);
            return new RegistrationResponseDto
            {
                IsSuccessfulRegistration = result?.IsSuccessfulRegistration ?? false,
                Errors = result?.Errors
            };
        }

        public async Task<AuthenticationResponseDto?> Login(UserForAuthenticationDto userForAuthenticationDto)
        {
            var content = JsonSerializer.Serialize(userForAuthenticationDto);
            var bodyContent = new StringContent(content, Encoding.UTF8, "application/json");
            var authResult = await _httpClient.PostAsync("authentication/login", bodyContent);
            var authContent = await authResult.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<AuthenticationResponseDto>(authContent, _jsonSerializerOptions);

            if (!authResult.IsSuccessStatusCode || result == null || result.Token == null)
                return new AuthenticationResponseDto
                {
                    IsAuthSuccessful = false,
                    ErrorMessage = result?.ErrorMessage ?? "Login failed or invalid response.",
                    Token = null
                };

            await _localStorage.SetItemAsync("authToken", result.Token.AccessToken);
            await _localStorage.SetItemAsync("refreshToken", result.Token.RefreshToken);
            var fullName = $"{result.FirstName} {result.LastName}".Trim();
            // persist first/last/vendor name so AuthStateProvider can add them on reload
            if (!string.IsNullOrEmpty(result.FirstName))
                await _localStorage.SetItemAsync("firstName", result.FirstName);
            if (!string.IsNullOrEmpty(result.LastName))
                await _localStorage.SetItemAsync("lastName", result.LastName);
            if (!string.IsNullOrEmpty(result.VendorName))
                await _localStorage.SetItemAsync("vendorName", result.VendorName);
            if (!string.IsNullOrEmpty(result.Roles))
                await _localStorage.SetItemAsync("userRoles", result.Roles);
            if (!string.IsNullOrEmpty(result.UserType))
                await _localStorage.SetItemAsync("userType", result.UserType);
            if (!string.IsNullOrEmpty(fullName))
                await _localStorage.SetItemAsync("fullName", fullName);
            if (result.VendorId.HasValue)
                await _localStorage.SetItemAsync("vendorPK", result.VendorId.Value);
            if (result.VendorContactId.HasValue)
                await _localStorage.SetItemAsync("vendorContactPK", result.VendorContactId.Value);
            if (result.EmployeeId.HasValue)
                await _localStorage.SetItemAsync("empPK", result.EmployeeId.Value);


            // Notify AuthStateProvider including first/last/vendor name so UI updates immediately
            ((AuthStateProvider)_authStateProvider).NotifyUserAuthentication(result.Token.AccessToken, 
                result.FirstName, result.LastName, 
                result.VendorName, result.UserType,
                result.FullName, result.VendorId, result.VendorContactId, result.EmployeeId);

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("bearer", result.Token.AccessToken);

            result.IsAuthSuccessful = true;
            return new AuthenticationResponseDto { IsAuthSuccessful = true };
            //return result;
        }
       

        public async Task Logout()
        {
            try
            {
                // Keys written during Login -- ensure we remove all of them
                var keysToRemove = new[]
                {
                    "authToken",
                    "refreshToken",
                    "access_token",
                    "id_token",
                    "userType",
                    "vendorPK",
                    "vendorContactPK",
                    "vendorContactId",
                    "empPK",
                    "employeeId",
                    "vendorId",
                    "firstName",
                    "lastName",
                    "fullName",
                    "vendorName",
                    "userRoles"
                };

                foreach (var key in keysToRemove)
                {
                    await _localStorage.RemoveItemAsync(key);
                }

                // Clear any leftover auth header so HttpClient stops sending the token
                _httpClient.DefaultRequestHeaders.Authorization = null;

                // Notify AuthenticationStateProvider so UI updates immediately
                if (_authStateProvider is AuthStateProvider custom)
                {
                    custom.MarkUserAsLoggedOut();
                }
                else
                {
                    // Best-effort fallback to trigger a state update
                    await _authStateProvider.GetAuthenticationStateAsync();
                }

                // Force a full page reload so any components reading localStorage directly (or cached UI) update
                // Navigate to login and force a full reload of the app from the server.
                _nav.NavigateTo("/login", forceLoad: true);

                _snackbar.Add("You have been logged out.", Severity.Success);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Logout failed");
                _snackbar.Add("Logout failed - please try again.", Severity.Error);
            }
        }

        public async Task<string> RefreshToken()
        {
            var token = await _localStorage.GetItemAsync<string>("authToken");
            var refreshToken = await _localStorage.GetItemAsync<string>("refreshToken");

            var tokenDto = JsonSerializer.Serialize(new TokenDto { AccessToken = token ?? string.Empty, RefreshToken = refreshToken ?? string.Empty });
            var bodyContent = new StringContent(tokenDto, Encoding.UTF8, "application/json");

            var refreshResult = await _httpClient.PostAsync("token/refresh", bodyContent);
            var refreshContent = await refreshResult.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<TokenDto>(refreshContent, _jsonSerializerOptions);

            if (!refreshResult.IsSuccessStatusCode || result == null || string.IsNullOrEmpty(result.AccessToken))
                throw new ApplicationException("Something went wrong during the refresh token action");

            await _localStorage.SetItemAsync("authToken", result.AccessToken);
            await _localStorage.SetItemAsync("refreshToken", result.RefreshToken);

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("bearer", result.AccessToken);

            return result.AccessToken;
        }

        /// <summary>
        /// Calls the API endpoint that triggers sending a password reset token to the user's email.
        /// Adjust the endpoint path if your API exposes a different route.
        /// </summary>
        public async Task<bool> SendPasswordResetTokenAsync(string userName)
        {
            if (string.IsNullOrWhiteSpace(userName))
                throw new ArgumentException("userName is required", nameof(userName));

            var payload = new { userName = userName.Trim() };
            var json = JsonSerializer.Serialize(payload, _jsonSerializerOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Default endpoint - change if your API uses a different path (e.g. "auth/forgotpassword")
            var response = await _httpClient.PostAsync("authentication/forgot-password", content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                // throw with details so caller can log/inspect server message
                throw new Exception($"Password reset request failed: {(int)response.StatusCode} {response.ReasonPhrase}. Response: {responseBody}");
            }

            return true;
        }

        /// <summary>
        /// Submits a new password along with the reset token to the API.
        /// </summary>
        public async Task<bool> ResetPasswordAsync(string userName, string token, string newPassword)
        {
            if (string.IsNullOrWhiteSpace(userName))
                throw new ArgumentException("userName is required", nameof(userName));
            if (string.IsNullOrWhiteSpace(token))
                throw new ArgumentException("token is required", nameof(token));
            if (string.IsNullOrWhiteSpace(newPassword))
                throw new ArgumentException("newPassword is required", nameof(newPassword));

            // The API expects both password and confirmPassword fields (server-side validation requires ConfirmPassword).
            var payload = new
            {
                UserName = userName.Trim(),
                Token = token,
                // Use properties named so camel-casing produces "password" and "confirmPassword"
                NewPassword = newPassword,
                ConfirmPassword = newPassword
            };

            var json = JsonSerializer.Serialize(payload, _jsonSerializerOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("authentication/reset-password", content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                // Provide useful message up the stack
                throw new Exception($"Password reset failed: {(int)response.StatusCode} {response.ReasonPhrase}. Response: {responseBody}");
            }

            return true;
        }
    }
}
