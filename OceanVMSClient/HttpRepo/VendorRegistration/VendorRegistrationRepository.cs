using Entities.Models.VendorReg;
using OceanVMSClient.Features;
using OceanVMSClient.HttpRepoInterface.VendorRegistration;
using Shared.DTO.VendorReg;
using Shared.RequestFeatures;
using System.Text.Json;
using System.Text.Json.Nodes;

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

        public async Task<VendorRegistrationFormDto> CreateNewVendorRegistration(VendorRegistrationFormCreateDto vendorRegistrationFormCreateDto)
        {
            var vendorRegistrationJson = new StringContent(JsonSerializer.Serialize(vendorRegistrationFormCreateDto), System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("vendorregistrationform", vendorRegistrationJson);
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(content);
            }
            var createdVendorRegistration = JsonSerializer.Deserialize<VendorRegistrationFormDto>(content, _options);
            return createdVendorRegistration;
        }

        public async Task<VendorRegistrationFormDto> UpdateVendorRegistration(Guid vendorRegistrationFormId, VendorRegistrationFormUpdateDto vendorRegistrationFormUpdateDto)
        {
            var vendorRegistrationJson = new StringContent(JsonSerializer.Serialize(vendorRegistrationFormUpdateDto), System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PutAsync($"vendorregistrationform/{vendorRegistrationFormId}", vendorRegistrationJson);
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(content);
            }
            var updatedVendorRegistration = JsonSerializer.Deserialize<VendorRegistrationFormDto>(content, _options);
            return updatedVendorRegistration;
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
                if (!string.IsNullOrWhiteSpace(vendorRegistrationFormParameters.ResponderFullName))
                    queryParams.Add(add("ResponderFullName", vendorRegistrationFormParameters.ResponderFullName));
                if (!string.IsNullOrWhiteSpace(vendorRegistrationFormParameters.ReviewerStatus))
                    queryParams.Add(add("ReviewerStatus", vendorRegistrationFormParameters.ReviewerStatus));
                if (!string.IsNullOrWhiteSpace(vendorRegistrationFormParameters.ApproverStatus))
                    queryParams.Add(add("ApproverStatus", vendorRegistrationFormParameters.ApproverStatus));
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

        public async Task<string> UploadCencelChequeImage(MultipartFormDataContent content)
        {
            var postResult = await _httpClient.PostAsync("upload/CancelCheq", content);
            var postContent = await postResult.Content.ReadAsStringAsync();
            if (!postResult.IsSuccessStatusCode)
            {
                throw new Exception(postContent);
            }
            else
            {
                //var imageUrl = Path.Combine("https://myoceanapp.azurewebsites.net/", postContent);
                var imageUrl = postContent;
                return imageUrl;
            }
        }

        public async Task<string> UploadOrgRegDocImage(string DocType, MultipartFormDataContent content)
        {
            var postResult = await _httpClient.PostAsync($"upload/{DocType}", content);
            var postContent = await postResult.Content.ReadAsStringAsync();
            if (!postResult.IsSuccessStatusCode)
            {
                throw new Exception(postContent);
            }
            else
            {
                // normalize server response into an absolute URL
                return NormalizeReturnedUrl(postContent);
            }
        }

        private string NormalizeReturnedUrl(string postContent)
        {
            if (string.IsNullOrWhiteSpace(postContent))
                return postContent;

            // If server returned JSON string or object, try to extract URL
            string candidate = postContent.Trim();
            try
            {
                using var doc = JsonDocument.Parse(candidate);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.String)
                {
                    candidate = root.GetString() ?? candidate;
                }
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String)
                        candidate = urlProp.GetString() ?? candidate;
                    else if (root.TryGetProperty("path", out var pathProp) && pathProp.ValueKind == JsonValueKind.String)
                        candidate = pathProp.GetString() ?? candidate;
                }
            }
            catch
            {
                // not JSON — use raw string
            }

            // If the returned value is already absolute, return it
            if (Uri.IsWellFormedUriString(candidate, UriKind.Absolute))
                return candidate;

            // Otherwise, combine with HttpClient base address if available
            var baseAddr = _httpClient.BaseAddress?.ToString().TrimEnd('/');
            if (!string.IsNullOrEmpty(baseAddr))
            {
                var combined = $"{baseAddr}/{candidate.TrimStart('/')}";
                return combined;
            }

            // fallback: return raw candidate
            return candidate;
        }

        public async Task<VendorRegistrationFormDto> GetVendorRegistrationFormByIdAsync(Guid id)
        {
            var response = await _httpClient.GetAsync($"vendorregistrationform/{id}");
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(content);
            }

            // Ensure required nested object `CompanyOwnership` exists in the JSON payload before deserializing.
            // Some server responses omit the nested object (only send CompanyOwnershipId/CompanyOwnershipType),
            // which causes System.Text.Json to throw for `required` properties on the client-side model.
            try
            {
                var node = JsonNode.Parse(content);
                if (node is JsonObject obj)
                {
                    if (obj["CompanyOwnership"] == null)
                    {
                        // Try to seed CompanyOwnership from available fields (if present)
                        Guid ownerId = Guid.Empty;
                        var ownerIdNode = obj["CompanyOwnershipId"];
                        if (ownerIdNode != null && Guid.TryParse(ownerIdNode.ToString(), out var parsedId))
                            ownerId = parsedId;

                        var ownerType = obj["CompanyOwnershipType"]?.ToString() ?? string.Empty;

                        var newOwner = new JsonObject
                        {
                            ["Id"] = JsonValue.Create(ownerId),
                            ["CompanyOwnershipType"] = JsonValue.Create(ownerType)
                        };

                        obj["CompanyOwnership"] = newOwner;
                        content = obj.ToJsonString();
                    }
                }
            }
            catch
            {
                // If JSON parsing/manipulation fails, fall back to original content and let the deserializer handle it.
            }

            var vendorRegistrationForm = JsonSerializer.Deserialize<VendorRegistrationFormDto>(content, _options);
            return vendorRegistrationForm!;
        }

        public async Task<string> ApproveVendorRegistrationFormAsync(Guid vendorRegistrationFormId, VendorRegistrationFormApprovalDto approvalDto)
        {
            var approvalJson = new StringContent(JsonSerializer.Serialize(approvalDto), System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PutAsync($"vendorregistrationform/approve/{vendorRegistrationFormId}", approvalJson);
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(content);
            }
            return response.IsSuccessStatusCode.ToString();
        }

        public async Task<string> ReviewVendorRegistrationFormAsync(Guid vendorRegistrationFormId, VendorRegistrationFormReviewDto reviewDto)
        {
            var reviewJson = new StringContent(JsonSerializer.Serialize(reviewDto), System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PutAsync($"vendorregistrationform/review/{vendorRegistrationFormId}", reviewJson);
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(content);
            }
            return response.IsSuccessStatusCode.ToString();
        }
    }
}
