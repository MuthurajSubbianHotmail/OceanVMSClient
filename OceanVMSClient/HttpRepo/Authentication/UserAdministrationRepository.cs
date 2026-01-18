using Blazored.LocalStorage;
using OceanVMSClient.HttpRepoInterface.Authentication;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace OceanVMSClient.HttpRepo.Authentication
{
    public class UserAdministrationRepository : IUserAdministrationRepository
    {
        private readonly HttpClient _httpClient;
        private readonly ILocalStorageService _localStorage;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly ILogger<UserAdministrationRepository>? _logger;

        public UserAdministrationRepository(HttpClient httpClient, ILocalStorageService localStorage, ILogger<UserAdministrationRepository>? logger = null)
        {
            _httpClient = httpClient;
            _localStorage = localStorage;
            _logger = logger;
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            };
        }

        private async Task EnsureAuthHeaderAsync()
        {
            try
            {
                var token = await _localStorage.GetItemAsync<string>("authToken");
                if (!string.IsNullOrWhiteSpace(token))
                    _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
            catch
            {
                // ignore — caller will receive HTTP error if unauthenticated
            }
        }

        public Task<bool> LockUserAsync(string userName, string? reason = null) => SetUserLockStateAsync(userName, true, reason);
        public Task<bool> UnlockUserAsync(string userName) => SetUserLockStateAsync(userName, false, null);

        public async Task<bool> SetUserLockStateAsync(string userName, bool isLocked, string? reason = null)
        {
            if (string.IsNullOrWhiteSpace(userName)) throw new ArgumentException("userName is required", nameof(userName));

            await EnsureAuthHeaderAsync();

            var encoded = Uri.EscapeDataString(userName.Trim());

            // Default duration used in your sample URL. Change if backend expects different value or make it a parameter.
            var minutes = 30;

            var endpoint = isLocked
                ? $"authentication/lock/users/{encoded}?minutes={minutes}"
                : $"authentication/unlock/users/{encoded}";

            if (!string.IsNullOrWhiteSpace(reason))
            {
                var sep = endpoint.Contains('?') ? "&" : "?";
                endpoint += $"{sep}reason={Uri.EscapeDataString(reason)}";
            }

            HttpResponseMessage response;
            try
            {
                // backend expects the username in path + query (sample: authentication/lock/users/vendorapprover%40olipl.org?minutes=30&reason=Rejected)
                // send POST with empty body — server reads path/query
                response = await _httpClient.PostAsync(endpoint, new StringContent(string.Empty, Encoding.UTF8, "application/json"));
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "HTTP request to {Endpoint} failed", endpoint);
                throw;
            }

            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning("SetUserLockStateAsync failed for {UserName} -> {Status} : {Body}", userName, response.StatusCode, body);
                throw new Exception($"API Error: {(int)response.StatusCode} {response.ReasonPhrase}. Response: {body}");
            }

            return true;
        }
    }
}