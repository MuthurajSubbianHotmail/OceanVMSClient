using OceanVMSClient.Features;
using OceanVMSClient.HttpRepoInterface.InvoiceModule;
using Shared.DTO.POModule;
using Shared.RequestFeatures;
using System.Text.Json;
using System.Web;
using System.Net.Http.Headers;

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


            return query.ToString();
        }

       
    }
}
