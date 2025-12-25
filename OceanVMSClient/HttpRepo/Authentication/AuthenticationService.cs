using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;
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

        public AuthenticationService(HttpClient httpClient, 
            AuthenticationStateProvider authenticationStateProvider, 
            ILocalStorageService localStorageService)
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

            // Notify AuthStateProvider including first/last/vendor name so UI updates immediately
            ((AuthStateProvider)_authStateProvider).NotifyUserAuthentication(result.Token.AccessToken, result.FirstName, result.LastName, result.VendorName, result.UserType);

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("bearer", result.Token.AccessToken);

            result.IsAuthSuccessful = true;
            return new AuthenticationResponseDto { IsAuthSuccessful = true };
            //return result;
        }
       

        public async Task Logout()
        {
            await _localStorage.RemoveItemAsync("authToken");
            await _localStorage.RemoveItemAsync("refreshToken");
            await _localStorage.RemoveItemAsync("userRoles");
            await _localStorage.RemoveItemAsync("firstName");
            await _localStorage.RemoveItemAsync("lastName");
            await _localStorage.RemoveItemAsync("userType");
            await _localStorage.RemoveItemAsync("fullName");
            await _localStorage.RemoveItemAsync("empPK");
            await _localStorage.RemoveItemAsync("vendorPK");
            await _localStorage.RemoveItemAsync("vendorContactPK");
            await _localStorage.RemoveItemAsync("vendorName");

            ((AuthStateProvider)_authStateProvider).NotifyUserLogout();
            _httpClient.DefaultRequestHeaders.Authorization = null;
            Console.WriteLine("User logged out successfully.");
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
    }
}
