using MudBlazor;
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
            var response = await _httpClient.GetAsync($"invoiceapprovers/project/{projectID}/employee/{employeeId}/approver-types");
            var content = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
            {
                var result = System.Text.Json.JsonSerializer.Deserialize<InvoiceApproverResponse>(content);
                return (result?.isAssigned ?? false, result?.assignedApproverTypes ?? string.Empty);
                throw new Exception(content);
            } else if(response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return (false, "Not Assigned");
            } else
            {
                return (false, "Unknown Error");
            }
            // Assuming the API returns a JSON object with IsAssigned and AssignedType properties

        }
        private class InvoiceApproverResponse
        {
            public bool isAssigned { get; set; }
            public string assignedApproverTypes { get; set; } = string.Empty;
        }

    }
}
