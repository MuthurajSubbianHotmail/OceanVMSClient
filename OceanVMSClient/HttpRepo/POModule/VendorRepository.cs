using OceanVMSClient.Features;
using OceanVMSClient.HttpRepoInterface.POModule;
using Shared.DTO.POModule;
using Shared.RequestFeatures;
using System.Text.Json;
using System.Web;

namespace OceanVMSClient.HttpRepo.POModule
{
    public class VendorRepository : IVendorRepository
    {
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _options = new JsonSerializerOptions();
        public VendorRepository(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
        }

        public async Task<PagingResponse<VendorDto>> GetAllVendors(VendorParameters vendorParameters)
        {
            var response = await _httpClient.GetAsync($"vendors?{vendorParameters.ToQueryString()}");
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(content);
            }
            var pagingResponse = new PagingResponse<VendorDto>
            {
                Items = JsonSerializer.Deserialize<List<VendorDto>>(content, _options),
                MetaData = JsonSerializer.Deserialize<MetaData>(response.Headers.GetValues("X-Pagination").First(), _options)
            };
            return pagingResponse;
        }

        public async Task<VendorDto> GetVendorById(Guid vendorContactId)
        {
            var response = await _httpClient.GetAsync($"vendors/{vendorContactId}");
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(content);
            }
            var vendor = JsonSerializer.Deserialize<VendorDto>(content, _options);
            return vendor!;
        }

        public async Task<VendorDto> GetVendorByName(string vendorName)
        {
            var response = await _httpClient.GetAsync($"vendors/by-name/{HttpUtility.UrlEncode(vendorName)}");
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(content);
            }
            var vendor = JsonSerializer.Deserialize<VendorDto>(content, _options);
            return vendor!;
        }
    }
}

public static class VendorParametersExtensions
{
    public static string ToQueryString(this VendorParameters vendorParameters)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["SearchTerm"] = vendorParameters.SearchTerm;
        query["PageNumber"] = vendorParameters.PageNumber.ToString();
        query["PageSize"] = vendorParameters.PageSize.ToString();
        return query.ToString();
    }
}
