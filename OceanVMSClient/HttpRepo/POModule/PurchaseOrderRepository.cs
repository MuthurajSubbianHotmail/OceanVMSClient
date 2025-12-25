using Entities.Models.POModule;
using Microsoft.AspNetCore.WebUtilities;
using OceanVMSClient.Features;
using OceanVMSClient.HttpRepoInterface.PoModule;
using Shared.DTO.POModule;
using Shared.DTO.VendorReg;
using Shared.RequestFeatures;
using System.Text.Json;
using System.Globalization;

namespace OceanVMSClient.HttpRepo.POModule
{
    public class PurchaseOrderRepository : IPurchaseOrderRepository
    {
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _options = new JsonSerializerOptions();
        public PurchaseOrderRepository(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
        }

        public async Task<PagingResponse<PurchaseOrderDto>> GetAllPurchaseOrders(PurchaseOrderParameters purchaseOrderParameters)
        {
            var queryStringParam = new Dictionary<string, string>
            {
                ["pageNumber"] = purchaseOrderParameters.PageNumber.ToString(),
                ["pageSize"] = purchaseOrderParameters.PageSize.ToString(),
                ["searchTerm"] = purchaseOrderParameters.SearchTerm ?? string.Empty,
                ["orderBy"] = purchaseOrderParameters.OrderBy ?? string.Empty,
                ["invoicestatus"] = purchaseOrderParameters.InvoiceStatus ?? string.Empty,
                ["sapponumber"] = purchaseOrderParameters.SAPPONumber ?? string.Empty,
                ["postartdate"] = purchaseOrderParameters.POStartDate == DateTime.MinValue ? string.Empty : purchaseOrderParameters.POStartDate.ToString("o", CultureInfo.InvariantCulture),
                ["poenddate"] = purchaseOrderParameters.POEndDate == DateTime.MinValue ? string.Empty : purchaseOrderParameters.POEndDate.ToString("o", CultureInfo.InvariantCulture),
                ["vendorname"] = purchaseOrderParameters.VendorName ?? string.Empty,
            };

            // add decimals only when valid range provided (no quotes; formatted invariantly)
            if (purchaseOrderParameters.ValidTotalValueRange)
            {
                queryStringParam["mintotalvalue"] = purchaseOrderParameters.MinTotalValue.ToString(CultureInfo.InvariantCulture);
                queryStringParam["maxtotalvalue"] = purchaseOrderParameters.MaxTotalValue.ToString(CultureInfo.InvariantCulture);
            }

            var response = await _httpClient.GetAsync(QueryHelpers.AddQueryString("purchaseorders", queryStringParam));
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(content);
            }
            var pagingResponse = new PagingResponse<PurchaseOrderDto>
            {
                Items = JsonSerializer.Deserialize<List<PurchaseOrderDto>>(content, _options),
                MetaData = JsonSerializer.Deserialize<MetaData>(response.Headers.GetValues("X-Pagination").First(), _options)
            };
            return pagingResponse;

        }

        public async Task<PurchaseOrderDto> GetPurchaseOrderById(Guid PurchaseOrderId)
        {
            var response = await _httpClient.GetAsync($"purchaseorders/{PurchaseOrderId}");
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(content);
            }
            var purchaseOrder = JsonSerializer.Deserialize<PurchaseOrderDto>(content, _options);
            return purchaseOrder;
        }

        public async Task<PagingResponse<PurchaseOrderDto>> GetAllPurchaseOrdersOfVendorAsync(Guid vendorId, PurchaseOrderParameters purchaseOrderParameters)
        {
            var queryStringParam = new Dictionary<string, string>
            {
                ["pageNumber"] = purchaseOrderParameters.PageNumber.ToString(),
                ["pageSize"] = purchaseOrderParameters.PageSize.ToString(),
                ["searchTerm"] = purchaseOrderParameters.SearchTerm ?? string.Empty,
                ["orderBy"] = purchaseOrderParameters.OrderBy ?? string.Empty,
                ["invoicestatus"] = purchaseOrderParameters.InvoiceStatus ?? string.Empty,
                ["sapponumber"] = purchaseOrderParameters.SAPPONumber ?? string.Empty,
                ["postartdate"] = purchaseOrderParameters.POStartDate == DateTime.MinValue ? string.Empty : purchaseOrderParameters.POStartDate.ToString("o", CultureInfo.InvariantCulture),
                ["poenddate"] = purchaseOrderParameters.POEndDate == DateTime.MinValue ? string.Empty : purchaseOrderParameters.POEndDate.ToString("o", CultureInfo.InvariantCulture),
                ["vendorname"] = purchaseOrderParameters.VendorName ?? string.Empty,
            };

            // add decimals only when valid range provided (no quotes; formatted invariantly)
            if (purchaseOrderParameters.ValidTotalValueRange)
            {
                queryStringParam["mintotalvalue"] = purchaseOrderParameters.MinTotalValue.ToString(CultureInfo.InvariantCulture);
                queryStringParam["maxtotalvalue"] = purchaseOrderParameters.MaxTotalValue.ToString(CultureInfo.InvariantCulture);
            }
            var response = await _httpClient.GetAsync(QueryHelpers.AddQueryString($"purchaseorders/vendor/{vendorId}", queryStringParam));
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(content);
            }
            var pagingResponse = new PagingResponse<PurchaseOrderDto>
            {
                Items = JsonSerializer.Deserialize<List<PurchaseOrderDto>>(content, _options),
                MetaData = JsonSerializer.Deserialize<MetaData>(response.Headers.GetValues("X-Pagination").First(), _options)
            };
            return pagingResponse;
        }

        public async Task<PagingResponse<PurchaseOrderDto>> GetAllPurchaseOrdersOfApproversAsync(Guid employeeId, PurchaseOrderParameters purchaseOrderParameters)
        {
            var queryStringParam = new Dictionary<string, string>
            {
                ["pageNumber"] = purchaseOrderParameters.PageNumber.ToString(),
                ["pageSize"] = purchaseOrderParameters.PageSize.ToString(),
                ["searchTerm"] = purchaseOrderParameters.SearchTerm ?? string.Empty,
                ["orderBy"] = purchaseOrderParameters.OrderBy ?? string.Empty,
                ["invoicestatus"] = purchaseOrderParameters.InvoiceStatus ?? string.Empty,
                ["sapponumber"] = purchaseOrderParameters.SAPPONumber ?? string.Empty,
                ["postartdate"] = purchaseOrderParameters.POStartDate == DateTime.MinValue ? string.Empty : purchaseOrderParameters.POStartDate.ToString("o", CultureInfo.InvariantCulture),
                ["poenddate"] = purchaseOrderParameters.POEndDate == DateTime.MinValue ? string.Empty : purchaseOrderParameters.POEndDate.ToString("o", CultureInfo.InvariantCulture),
                ["vendorname"] = purchaseOrderParameters.VendorName ?? string.Empty,
            };

            // add decimals only when valid range provided (no quotes; formatted invariantly)
            if (purchaseOrderParameters.ValidTotalValueRange)
            {
                queryStringParam["mintotalvalue"] = purchaseOrderParameters.MinTotalValue.ToString(CultureInfo.InvariantCulture);
                queryStringParam["maxtotalvalue"] = purchaseOrderParameters.MaxTotalValue.ToString(CultureInfo.InvariantCulture);
            }
            var response = await _httpClient.GetAsync(QueryHelpers.AddQueryString($"purchaseorders/approver/{employeeId}", queryStringParam));
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(content);
            }
            var pagingResponse = new PagingResponse<PurchaseOrderDto>
            {
                Items = JsonSerializer.Deserialize<List<PurchaseOrderDto>>(content, _options),
                MetaData = JsonSerializer.Deserialize<MetaData>(response.Headers.GetValues("X-Pagination").First(), _options)
            };
            return pagingResponse;
        }
    }
}