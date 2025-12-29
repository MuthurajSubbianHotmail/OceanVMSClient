using Entities.Models.Setup;
using OceanVMSClient.HttpRepoInterface.VendorRegistration;
using System.Text.Json;

namespace OceanVMSClient.HttpRepo.VendorRegistration
{
    public class VendorServiceRepository : IVendorServiceRepository
    {
        public readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        public VendorServiceRepository(HttpClient httpClient)
        {
                _httpClient = httpClient;
        }

        public async Task<List<VendorService>> GetAllVendorServices()
        {
            var response = await _httpClient.GetAsync("vendorservices");
            var content = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
            {
                var vendorServices = JsonSerializer.Deserialize<List<VendorService>>(content, _options);
                return vendorServices;
            }
            else
            {
                throw new Exception($"Failed to retrieve vendor services: {response.ReasonPhrase}");
            }
        }

    }
}
