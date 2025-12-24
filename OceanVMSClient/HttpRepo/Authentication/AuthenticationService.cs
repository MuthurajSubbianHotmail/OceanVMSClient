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
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
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

        //public async Task<AuthenticationResponseDto> Login(UserForAuthenticationDto userForAuthentication)
        //{
        //    // use configured options for consistent naming
        //    var content = JsonSerializer.Serialize(userForAuthentication, _jsonSerializerOptions);
        //    var bodyContent = new StringContent(content, Encoding.UTF8, "application/json");

        //    var authResult = await _httpClient.PostAsync("authentication/login", bodyContent);
        //    var authContent = await authResult.Content.ReadAsStringAsync();

        //    AuthenticationResponseDto? result = null;
        //    try
        //    {
        //        result = JsonSerializer.Deserialize<AuthenticationResponseDto>(authContent, _jsonSerializerOptions);
        //    }
        //    catch (JsonException)
        //    {
        //        // Fall through - result will be null and handled below
        //    }

        //    // If we couldn't deserialize, return a failure DTO instead of dereferencing null
        //    if (result == null)
        //    {
        //        return new AuthenticationResponseDto
        //        {
        //            IsAuthSuccessful = false,
        //            ErrorMessage = string.IsNullOrWhiteSpace(authContent) ? "Invalid authentication response." : authContent
        //        };
        //    }

        //    // If HTTP status indicates failure, return the server-provided result (with message if present)
        //    if (!authResult.IsSuccessStatusCode)
        //    {
        //        result.IsAuthSuccessful = false;
        //        if (string.IsNullOrWhiteSpace(result.ErrorMessage))
        //            result.ErrorMessage = string.IsNullOrWhiteSpace(authContent) ? "Authentication failed." : authContent;
        //        return result;
        //    }

        //    // Ensure token exists
        //    if (result.Token == null || string.IsNullOrWhiteSpace(result.Token.AccessToken))
        //    {
        //        return new AuthenticationResponseDto
        //        {
        //            IsAuthSuccessful = false,
        //            ErrorMessage = "Authentication succeeded but server did not return a token."
        //        };
        //    }

        //    // Persist token and update auth state
        //    await _localStorage.SetItemAsync("authToken", result.Token);

        //    var username = string.IsNullOrWhiteSpace(result.UserName) ? userForAuthentication.UserName : result.UserName;
        //    ((AuthStateProvider)_authStateProvider).NotifyUserAuthentication(username);

        //    _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result.Token.AccessToken);

        //    result.IsAuthSuccessful = true;
        //    return result;
        //}
        public async Task<AuthenticationResponseDto> Login(UserForAuthenticationDto userForAuthentication)
        {
            var content = JsonSerializer.Serialize(userForAuthentication);
            var bodyContent = new StringContent(content, Encoding.UTF8, "application/json");

            var authResult = await _httpClient.PostAsync("authentication/login", bodyContent);
            var authContent = await authResult.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<AuthenticationResponseDto>(authContent, _jsonSerializerOptions);

            if (!authResult.IsSuccessStatusCode)
                return result;
            var fullName = $"{result.FirstName ?? string.Empty} {result.LastName ?? string.Empty}".Trim();
            var UserType = result.UserType;
            var FirstName = result.FirstName;
            var LastName = result.LastName;
            var AssignedRoles = result.Roles;

            await _localStorage.SetItemAsync("authToken", result.Token.AccessToken);
            await _localStorage.SetItemAsync("refreshToken", result.Token.RefreshToken);
            await _localStorage.SetItemAsync("userRoles", AssignedRoles);
            await _localStorage.SetItemAsync("firstName", FirstName);
            await _localStorage.SetItemAsync("lastName", LastName);
            await _localStorage.SetItemAsync("userType", UserType);
            await _localStorage.SetItemAsync("fullName", fullName);

            ((AuthStateProvider)_authStateProvider).NotifyUserAuthentication(result.UserName ?? userForAuthentication.UserName, fullName, FirstName, LastName, UserType);
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("bearer", result.Token.AccessToken);

            return new AuthenticationResponseDto { IsAuthSuccessful = true };
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
            ((AuthStateProvider)_authStateProvider).NotifyUserLogout();
            _httpClient.DefaultRequestHeaders.Authorization = null;
        }

        public async Task<string> RefreshToken()
        {
            var refreshToken = await _localStorage.GetItemAsync<string>("refreshToken");
            var tokenDto = new TokenDto { RefreshToken = refreshToken };
            var content = JsonSerializer.Serialize(tokenDto, _jsonSerializerOptions);
            var bodyContent = new StringContent(content, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("authentication/refresh-token", bodyContent);
            response.EnsureSuccessStatusCode();
            var responseContent = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<AuthenticationResponseDto>(responseContent, _jsonSerializerOptions);
            if (result == null || result.Token == null || string.IsNullOrWhiteSpace(result.Token.AccessToken))
                throw new Exception("Failed to refresh token.");
            await _localStorage.SetItemAsync("authToken", result.Token.AccessToken);
            await _localStorage.SetItemAsync("refreshToken", result.Token.RefreshToken);
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("bearer", result.Token.AccessToken);
            return result.Token.AccessToken;
        }


    }
}
