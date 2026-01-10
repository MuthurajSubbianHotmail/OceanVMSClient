using OceanVMSClient.HttpRepoInterface.VendorRegistration;
using Shared.DTO.VendorReg;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OceanVMSClient.HttpRepo.VendorRegistration
{
    public class VendorContactRepository : IVendorContactRepository
    {
        public readonly HttpClient _httpClient;
        public readonly JsonSerializerOptions _options;

        public VendorContactRepository(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
        }

        public async Task<bool> ResponderEmailExistsAsync(string responderEmail, Guid? excludeId = null)
        {
            if (string.IsNullOrWhiteSpace(responderEmail))
                return false;

            var url = $"vendorcontacts/by-email?email={Uri.EscapeDataString(responderEmail)}";
            var response = await _httpClient.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(content);
            }

            try
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                List<VendorRegistrationFormDto> items = new();

                if (root.ValueKind == JsonValueKind.Array)
                {
                    items = JsonSerializer.Deserialize<List<VendorRegistrationFormDto>>(content, _options) ?? new List<VendorRegistrationFormDto>();
                }
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    // Paging response { items: [...] }
                    if (root.TryGetProperty("items", out var itemsProp) && itemsProp.ValueKind == JsonValueKind.Array)
                    {
                        items = JsonSerializer.Deserialize<List<VendorRegistrationFormDto>>(itemsProp.GetRawText(), _options) ?? new List<VendorRegistrationFormDto>();
                    }
                    else
                    {
                        // Single DTO object
                        var single = JsonSerializer.Deserialize<VendorRegistrationFormDto>(content, _options);
                        if (single != null) items.Add(single);
                    }
                }

                if (excludeId.HasValue)
                    return items.Any(i => i.Id != Guid.Empty && i.Id != excludeId.Value);

                return items.Any();
            }
            catch (JsonException)
            {
                // fallback
                var fallback = JsonSerializer.Deserialize<List<VendorRegistrationFormDto>>(content, _options);
                if (fallback != null)
                {
                    if (excludeId.HasValue)
                        return fallback.Any(i => i.Id != Guid.Empty && i.Id != excludeId.Value);
                    return fallback.Any();
                }
                return false;
            }
        }
    }
}