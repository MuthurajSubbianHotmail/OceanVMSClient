using Entities.Models.Setup;
using OceanVMSClient.HttpRepoInterface.VendorRegistration;
using System.Text.Json;

namespace OceanVMSClient.HttpRepo.VendorRegistration
{
    public class CompanyOwnershipRepository : ICompanyOwnershipRepository
    {
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        public CompanyOwnershipRepository(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }
        public async Task<List<CompanyOwnership>> GetAllCompanyOwnershipsAsync()
        {
            var response = await _httpClient.GetAsync("companyownerships");
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(content);
            }
            var companyOwnerships = JsonSerializer.Deserialize<List<CompanyOwnership>>(content, _options);
            return companyOwnerships;
        }
    }
}
