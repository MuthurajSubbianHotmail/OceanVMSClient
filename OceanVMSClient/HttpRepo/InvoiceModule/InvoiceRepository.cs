using Entities.Models.VendorReg;
using OceanVMSClient.Features;
using OceanVMSClient.HttpRepoInterface.InvoiceModule;
using Shared.DTO.POModule;
using Shared.RequestFeatures;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;
using static Shared.DTO.POModule.InvAPApproverReviewCompleteDto;

namespace OceanVMSClient.HttpRepo.InvoiceModule
{
    public class InvoiceRepository : IInvoiceRepository
    {
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _options = new JsonSerializerOptions();
        public InvoiceRepository(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
        }

        public async Task<PagingResponse<InvoiceDto>> GetAllInvoices(InvoiceParameters invoiceParameters)
        {
            var response = await _httpClient.GetAsync($"invoices?{invoiceParameters.ToQueryString()}");
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(content);
            }
            var pagingResponse = new PagingResponse<InvoiceDto>
            {
                Items = JsonSerializer.Deserialize<List<InvoiceDto>>(content, _options),
                MetaData = JsonSerializer.Deserialize<MetaData>(response.Headers.GetValues("X-Pagination").First(), _options)
            };
            return pagingResponse;
        }

        public async Task<PagingResponse<InvoiceDto>> GetInvoicesByVendorId(Guid vendorContactId, InvoiceParameters invoiceParameters)
        {
            var response = await _httpClient.GetAsync($"invoices/vendor/{vendorContactId}?{invoiceParameters.ToQueryString()}");
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(content);
            }
            var pagingResponse = new PagingResponse<InvoiceDto>
            {
                Items = JsonSerializer.Deserialize<List<InvoiceDto>>(content, _options),
                MetaData = JsonSerializer.Deserialize<MetaData>(response.Headers.GetValues("X-Pagination").First(), _options)
            };
            return pagingResponse;
        }

        public async Task<PagingResponse<InvoiceDto>> GetInvoicesByApproverEmployeeId(Guid employeeId, InvoiceParameters invoiceParameters)
        {
            var response = await _httpClient.GetAsync($"invoices/by-approver-employee/{employeeId}?{invoiceParameters.ToQueryString()}");
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(content);
            }
            var pagingResponse = new PagingResponse<InvoiceDto>
            {
                Items = JsonSerializer.Deserialize<List<InvoiceDto>>(content, _options),
                MetaData = JsonSerializer.Deserialize<MetaData>(response.Headers.GetValues("X-Pagination").First(), _options)
            };
            return pagingResponse;
        }

        public async Task<InvoiceDto> GetInvoiceById(Guid invoiceId)
        {
            var response = await _httpClient.GetAsync($"invoices/{invoiceId}");
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(content);
            }
            var invoice = JsonSerializer.Deserialize<InvoiceDto>(content, _options);
            return invoice!;
        }

        public async Task<PagingResponse<InvoiceDto>> GetInvoicesByPurchaseOrderId(Guid purchaseOrderId, InvoiceParameters invoiceParameters)
        {
            var response = await _httpClient.GetAsync($"invoices/purchaseorder/{purchaseOrderId}?{invoiceParameters.ToQueryString()}");
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(content);
            }
            var pagingResponse = new PagingResponse<InvoiceDto>
            {
                Items = JsonSerializer.Deserialize<List<InvoiceDto>>(content, _options),
                MetaData = JsonSerializer.Deserialize<MetaData>(response.Headers.GetValues("X-Pagination").First(), _options)
            };
            return pagingResponse;
        }

        public async Task<InvoiceDto> CreateInvoice(InvoiceForCreationDto invoiceForCreation)
        {
            var invoiceJson = new StringContent(JsonSerializer.Serialize(invoiceForCreation), System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("invoices", invoiceJson);
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(content);
            }
            var createdInvoice = JsonSerializer.Deserialize<InvoiceDto>(content, _options);
            return createdInvoice!;
        }

        public async Task<string> UploadInvoiceFile(MultipartFormDataContent content)
        {
            var postResult = await _httpClient.PostAsync("upload/Invoice", content);
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

        public async Task<InvoiceDto> UpdateInvoiceInitiatorReview(InvInitiatorReviewCompleteDto invInitiatorReviewCompleteDto)
        {
            var invoiceJson = new StringContent(JsonSerializer.Serialize(invInitiatorReviewCompleteDto), System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("invoices/initiator-review-complete", invoiceJson);
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(content);
            }
            var updatedInvoice = JsonSerializer.Deserialize<InvoiceDto>(content, _options);
            return updatedInvoice!;
        }
        public async Task<InvoiceDto> UpdateInvoiceCheckerReview(InvCheckerReviewCompleteDto invCheckerReviewCompleteDto)
        {
            var invoiceJson = new StringContent(JsonSerializer.Serialize(invCheckerReviewCompleteDto), System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("invoices/checker-review-complete", invoiceJson);
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(content);
            }
            var updatedInvoice = JsonSerializer.Deserialize<InvoiceDto>(content, _options);
            return updatedInvoice!;
        }

        public async Task<InvoiceDto> UpdateInvoiceValidatorApproval(InvValidatorReviewCompleteDto invValidatorReviewCompleteDto)
        {
            var invoiceJson = new StringContent(JsonSerializer.Serialize(invValidatorReviewCompleteDto), System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("invoices/validator-review-complete", invoiceJson);
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(content);
            }
            var updatedInvoice = JsonSerializer.Deserialize<InvoiceDto>(content, _options);
            return updatedInvoice!;
        }

        public async Task<InvoiceDto> UpdateInvoiceApproverApproval(InvApproverReviewCompleteDto invApproverReviewCompleteDto)
        {
            var invoiceJson = new StringContent(JsonSerializer.Serialize(invApproverReviewCompleteDto), System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("invoices/approver-review-complete", invoiceJson);
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(content);
            }
            var updatedInvoice = JsonSerializer.Deserialize<InvoiceDto>(content, _options);
            return updatedInvoice!;
        }
        public async Task<InvoiceDto> UpdateInvoiceAPReview(InvAPApproverReviewCompleteDto invAPReviewCompleteDto)
        {
            var invoiceJson = new StringContent(JsonSerializer.Serialize(invAPReviewCompleteDto), System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("invoices/ap-approver-review-complete", invoiceJson);
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(content);
            }
            var updatedInvoice = JsonSerializer.Deserialize<InvoiceDto>(content, _options);
            return updatedInvoice!;
        }


        public async Task<string> UploadInvoiceAttachmentDocImage(string DocType, MultipartFormDataContent content)
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

            // Trim and attempt to extract URL from JSON string/object responses
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
                // Not JSON — leave candidate as-is
            }

            candidate = candidate.Trim();

            // If the server returned a value that starts with a scheme, treat it as absolute
            // even if it contains spaces or other characters that make IsWellFormedUriString false.
            if (candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                // Ensure unsafe characters (spaces, etc.) are percent-encoded instead of trying to combine with base address.
                // Prefer returning the original if already well-formed.
                if (Uri.IsWellFormedUriString(candidate, UriKind.Absolute))
                    return candidate;

                // Escape URI (encodes spaces and other characters)
                try
                {
                    return Uri.EscapeUriString(candidate);
                }
                catch
                {
                    // Fallback to a conservative encode for spaces
                    return candidate.Replace(" ", "%20");
                }
            }

            // Not absolute: combine with base address if available.
            var baseAddr = _httpClient.BaseAddress?.ToString().TrimEnd('/');
            if (!string.IsNullOrEmpty(baseAddr))
            {
                // Ensure candidate starts with a slash for correct Uri combination
                if (!candidate.StartsWith("/"))
                    candidate = "/" + candidate;

                if (Uri.TryCreate(baseAddr, UriKind.Absolute, out var baseUri))
                {
                    // Use Uri to correctly combine and normalize paths
                    var combined = new Uri(baseUri, candidate).ToString();
                    return combined;
                }

                return $"{baseAddr}/{candidate.TrimStart('/')}";
            }

            // Fallback: return raw candidate
            return candidate;
        }

        public async Task<PagingResponse<InvoiceDto>> GetInvoicesWithAPReviewNotNAAsync(InvoiceParameters invoiceParameters)
        {
            var response = await _httpClient.GetAsync($"invoices/ap-review-not-na?{invoiceParameters.ToQueryString()}");
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(content);
            }
            var pagingResponse = new PagingResponse<InvoiceDto>
            {
                Items = JsonSerializer.Deserialize<List<InvoiceDto>>(content, _options),
                MetaData = JsonSerializer.Deserialize<MetaData>(response.Headers.GetValues("X-Pagination").First(), _options)
            };
            return pagingResponse;
        }

        public async Task<InvoiceStatusCountsDto> GetInvoiceStatusCountsByVendorAsync(Guid vendorId)
        {
            var response = await _httpClient.GetAsync($"invoices/vendor/status-counts/{vendorId}");
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(content);
            }
            var counts = JsonSerializer.Deserialize<InvoiceStatusCountsDto>(content, _options);
            return counts!;
        }

        public async Task<InvoiceStatusCountsDto> GetInvoiceStatusCountsForApproverAsync(Guid employeeId)
        {
            var response = await _httpClient.GetAsync($"invoices/approver/status-counts/{employeeId}");
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(content);
            }
            var counts = JsonSerializer.Deserialize<InvoiceStatusCountsDto>(content, _options);
            return counts!;
        }

        public async Task<InvoiceStatusSlaCountsDto> GetInvoiceStatusSlaCountsForApproverAsync(Guid employeeId)
        {
            var response = await _httpClient.GetAsync($"invoices/approver/sla-counts/{employeeId}");
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(content);
            }
            var counts = JsonSerializer.Deserialize<InvoiceStatusSlaCountsDto>(content, _options);
            return counts!;
        }
    }

    public static class InvoiceParametersExtensions
    {
        public static string ToQueryString(this InvoiceParameters parameters)
        {
            var query = HttpUtility.ParseQueryString(string.Empty);

            if (parameters.PageNumber > 0)
                query["PageNumber"] = parameters.PageNumber.ToString();

            if (parameters.PageSize > 0)
                query["PageSize"] = parameters.PageSize.ToString();

            if (!string.IsNullOrEmpty(parameters.OrderBy))
                query["OrderBy"] = parameters.OrderBy;

            if (!string.IsNullOrEmpty(parameters.SearchTerm))
                query["SearchTerm"] = parameters.SearchTerm;
            if (parameters.VendorId.HasValue && parameters.VendorId.Value != Guid.Empty)
                query["VendorId"] = parameters.VendorId.Value.ToString();
            if (!string.IsNullOrEmpty(parameters.VendorName))
                query["VendorName"] = parameters.VendorName;
            if (!string.IsNullOrEmpty(parameters.InvoiceRefNo))
                query["InvoiceRefNo"] = parameters.InvoiceRefNo;
            if (parameters.InvStartDate != default(DateTime))
                query["InvStartDate"] = parameters.InvStartDate.ToString("o");
            if (parameters.InvEndDate != default(DateTime))
                query["InvEndDate"] = parameters.InvEndDate.ToString("o");
            if (parameters.MinTotalValue.HasValue)
                query["MinTotalValue"] = parameters.MinTotalValue.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (parameters.MaxTotalValue.HasValue)
                query["MaxTotalValue"] = parameters.MaxTotalValue.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(parameters.SAPPONumber))
                query["SAPPONumber"] = parameters.SAPPONumber;
            if (!string.IsNullOrEmpty(parameters.ProjectManagerName))
                query["ProjectManagerName"] = parameters.ProjectManagerName;
            if (!string.IsNullOrEmpty(parameters.SiteSupervisorName))
                query["SiteSupervisorName"] = parameters.SiteSupervisorName;
            if (!string.IsNullOrEmpty(parameters.InvoiceStatus))
                query["InvoiceStatus"] = parameters.InvoiceStatus;
            if (!string.IsNullOrEmpty(parameters.PaymentStatus))
                query["PaymentStatus"] = parameters.PaymentStatus;
            if (parameters.ProjectId.HasValue && parameters.ProjectId.Value != Guid.Empty)
                query["ProjectId"] = parameters.ProjectId.Value.ToString();
            if (!string.IsNullOrEmpty(parameters.ProjectCode))
                query["ProjectCode"] = parameters.ProjectCode;


            return query.ToString();
        }

    }
}
