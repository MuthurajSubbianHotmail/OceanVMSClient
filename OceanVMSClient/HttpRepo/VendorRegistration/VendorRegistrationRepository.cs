using OceanVMSClient.Features;
using OceanVMSClient.HttpRepoInterface.VendorRegistration;
using Shared.DTO.VendorReg;
using Shared.RequestFeatures;
using System.Text.Json;

namespace OceanVMSClient.HttpRepo.VendorRegistration
{
    public class VendorRegistrationRepository : IVendorRegistrationRepository
    {
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        public VendorRegistrationRepository(HttpClient httpClient)
        {
                _httpClient = httpClient;
        }
        public async Task CreateVendorRegServiceAsync(Entities.Models.VendorReg.VendRegService vendorRegService)
        {
            var vendorRegServiceJson = new StringContent(JsonSerializer.Serialize(vendorRegService), System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("vendorregservices", vendorRegServiceJson);
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(content);
            }
        }
        public async Task DeleteVendorRegServiceAsync(Entities.Models.VendorReg.VendRegService vendorRegService)
        {
            var response = await _httpClient.DeleteAsync($"vendorregservices/{vendorRegService.Id}");
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(content);
            }
        }

        public async Task<IEnumerable<Entities.Models.VendorReg.VendRegService>> GetAllVendRegServicesForARegAsync(Guid vendorRegistrationId)
        {
            var response = await _httpClient.GetAsync($"vendorregservices/vendorregistration/{vendorRegistrationId}");
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(content);
            }
            var vendRegServices = JsonSerializer.Deserialize<List<Entities.Models.VendorReg.VendRegService>>(content, _options);
            return vendRegServices;
        }

        public async Task<Entities.Models.VendorReg.VendRegService> GetVendorRegServiceByIdAsync(Guid id)
        {
            var response = await _httpClient.GetAsync($"vendorregservices/{id}");
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(content);
            }
            var vendRegService = JsonSerializer.Deserialize<Entities.Models.VendorReg.VendRegService>(content, _options);
            return vendRegService;
        }

        public async Task UpdateVendorRegServiceAsync(Entities.Models.VendorReg.VendRegService vendorRegService)
        {
            var vendorRegServiceJson = new StringContent(JsonSerializer.Serialize(vendorRegService), System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PutAsync($"vendorregservices/{vendorRegService.Id}", vendorRegServiceJson);
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(content);
            }
        }

        public async Task<PagingResponse<VendorRegistrationFormDto>> GetAllVendorRegistration(VendorRegistrationFormParameters vendorRegistrationFormParameters)
        {
            var queryParams = new List<string>();

            if (vendorRegistrationFormParameters is not null)
            {
                string add(string key, string? value)
                {
                    return $"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value ?? string.Empty)}";
                }

                if (!string.IsNullOrWhiteSpace(vendorRegistrationFormParameters.SearchTerm))
                    queryParams.Add(add("SearchTerm", vendorRegistrationFormParameters.SearchTerm));
                if (!string.IsNullOrWhiteSpace(vendorRegistrationFormParameters.OrganizationName))
                    queryParams.Add(add("OrganizationName", vendorRegistrationFormParameters.OrganizationName));
                if (vendorRegistrationFormParameters.ReviewStatus.HasValue)
                    queryParams.Add(add("ReviewStatus", vendorRegistrationFormParameters.ReviewStatus.Value.ToString()));
                if (!string.IsNullOrWhiteSpace(vendorRegistrationFormParameters.GSTNO))
                    queryParams.Add(add("GSTNO", vendorRegistrationFormParameters.GSTNO));
                if (!string.IsNullOrWhiteSpace(vendorRegistrationFormParameters.PANNo))
                    queryParams.Add(add("PANNo", vendorRegistrationFormParameters.PANNo));
                if (!string.IsNullOrWhiteSpace(vendorRegistrationFormParameters.AadharNo))
                    queryParams.Add(add("AadharNo", vendorRegistrationFormParameters.AadharNo));
                if (!string.IsNullOrWhiteSpace(vendorRegistrationFormParameters.UDYAMRegNo))
                    queryParams.Add(add("UDYAMRegNo", vendorRegistrationFormParameters.UDYAMRegNo));
                if (!string.IsNullOrWhiteSpace(vendorRegistrationFormParameters.CIN))
                    queryParams.Add(add("CIN", vendorRegistrationFormParameters.CIN));
                if (!string.IsNullOrWhiteSpace(vendorRegistrationFormParameters.PFNo))
                    queryParams.Add(add("PFNo", vendorRegistrationFormParameters.PFNo));
                if (!string.IsNullOrWhiteSpace(vendorRegistrationFormParameters.ESIRegNo))
                    queryParams.Add(add("ESIRegNo", vendorRegistrationFormParameters.ESIRegNo));
                if (!string.IsNullOrWhiteSpace(vendorRegistrationFormParameters.OrderBy))
                    queryParams.Add(add("OrderBy", vendorRegistrationFormParameters.OrderBy));
            }

            var qs = queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : string.Empty;
            var response = await _httpClient.GetAsync($"vendorregistrationform{qs}");
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(content);
            }

            var items = JsonSerializer.Deserialize<List<VendorRegistrationFormDto>>(content, _options) ?? new List<VendorRegistrationFormDto>();

            MetaData metaData = null;
            if (response.Headers.TryGetValues("X-Pagination", out var headerValues))
            {
                string pagination = null;
                foreach (var v in headerValues)
                {
                    pagination = v;
                    break;
                }
                if (!string.IsNullOrEmpty(pagination))
                {
                    metaData = JsonSerializer.Deserialize<MetaData>(pagination, _options);
                }
            }

            return new PagingResponse<VendorRegistrationFormDto>
            {
                Items = items,
                MetaData = metaData
            };
        }
    }
}
