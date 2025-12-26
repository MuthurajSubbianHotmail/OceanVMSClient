using OceanVMSClient.HttpRepoInterface.POModule;
using System.Text.Json;

namespace OceanVMSClient.HttpRepo.POModule
{
    public class InvoiceApproverRepository : IInvoiceApproverRepository
    {
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _options = new JsonSerializerOptions();
        public InvoiceApproverRepository(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
        }
        public async Task<(bool IsAssigned, string AssignedType)> IsInvoiceApproverAsync(Guid projectID, Guid employeeId)
        {
            var response = await _httpClient.GetAsync($"invoiceapprovers/project/{projectID}/{employeeId}");
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(content);
            }
            // Assuming the API returns a JSON object with IsAssigned and AssignedType properties
            var result = System.Text.Json.JsonSerializer.Deserialize<InvoiceApproverResponse>(content);
            return (result?.isAssigned ?? false, result?.assignedApproverTypes ?? string.Empty);
        }
        private class InvoiceApproverResponse
        {
            public bool isAssigned { get; set; }
            public string assignedApproverTypes { get; set; } = string.Empty;
        }

    }
}
