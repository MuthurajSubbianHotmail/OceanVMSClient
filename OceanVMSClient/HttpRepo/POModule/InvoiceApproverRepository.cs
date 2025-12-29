using MudBlazor;
using OceanVMSClient.Features;
using OceanVMSClient.HttpRepoInterface.POModule;
using Shared.DTO.POModule;
using Shared.RequestFeatures;
using System.Text.Json;
using System.Net;
using System.Linq;

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
                // Try the simple DTO first
                var result = JsonSerializer.Deserialize<InvoiceApproverResponse>(content, _options);
                if (result != null && !string.IsNullOrWhiteSpace(result.assignedApproverTypes))
                    return (result.isAssigned, result.assignedApproverTypes);

                // Fallback: parse JSON to extract array or object values and join as comma-separated string
                try
                {
                    var root = JsonSerializer.Deserialize<JsonElement>(content, _options);

                    // Case: top-level array of strings or objects
                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        var items = new List<string>();
                        foreach (var el in root.EnumerateArray())
                        {
                            if (el.ValueKind == JsonValueKind.String)
                            {
                                items.Add(el.GetString()!);
                            }
                            else if (el.ValueKind == JsonValueKind.Object)
                            {
                                // try a few common property names
                                if (el.TryGetProperty("approverType", out var p) && p.ValueKind == JsonValueKind.String)
                                    items.Add(p.GetString()!);
                                else if (el.TryGetProperty("assignedApproverType", out var p2) && p2.ValueKind == JsonValueKind.String)
                                    items.Add(p2.GetString()!);
                                else if (el.TryGetProperty("type", out var p3) && p3.ValueKind == JsonValueKind.String)
                                    items.Add(p3.GetString()!);
                            }
                        }
                        var joined = string.Join(", ", items.Distinct());
                        return (result?.isAssigned ?? (items.Count > 0), joined);
                    }

                    // Case: object with an array property like assignedApproverTypes
                    if (root.ValueKind == JsonValueKind.Object)
                    {
                        if (root.TryGetProperty("assignedApproverTypes", out var apts) && apts.ValueKind == JsonValueKind.Array)
                        {
                            var items = new List<string>();
                            foreach (var el in apts.EnumerateArray())
                                if (el.ValueKind == JsonValueKind.String)
                                    items.Add(el.GetString()!);
                            var joined = string.Join(", ", items.Distinct());
                            return (result?.isAssigned ?? items.Count > 0, joined);
                        }

                        // sometimes API returns object with properties keyed by role names
                        var extracted = new List<string>();
                        foreach (var prop in root.EnumerateObject())
                        {
                            if (prop.Value.ValueKind == JsonValueKind.String)
                            {
                                extracted.Add(prop.Value.GetString()!);
                            }
                            else if (prop.Value.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var el in prop.Value.EnumerateArray())
                                    if (el.ValueKind == JsonValueKind.String)
                                        extracted.Add(el.GetString()!);
                            }
                        }
                        if (extracted.Count > 0)
                        {
                            var joined = string.Join(", ", extracted.Distinct());
                            return (result?.isAssigned ?? true, joined);
                        }
                    }
                }
                catch
                {
                    // ignore parsing fallback errors and return what's available below
                }

                // final fallback: return empty string when no types found
                return (result?.isAssigned ?? false, result?.assignedApproverTypes ?? string.Empty);
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return (false, "Not Assigned");
            }
            else
            {
                return (false, "Unknown Error");
            }
        }

        public async Task<PagingResponse<InvoiceApproverDTO>> GetInvoiceApproverByProjectIdAndType(Guid projectID, string assignedType)
        {
            var url = $"invoiceapprovers/project/{projectID}/type/{assignedType}";
            const int maxAttempts = 3;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    var response = await _httpClient.GetAsync(url);
                    var content = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        // If not found return empty result (caller can treat as no approvers)
                        if (response.StatusCode == HttpStatusCode.NotFound)
                        {
                            return new PagingResponse<InvoiceApproverDTO>
                            {
                                Items = new List<InvoiceApproverDTO>(),
                                MetaData = null
                            };
                        }

                        // For other non-success statuses, throw so upstream can decide
                        throw new Exception($"Request to '{url}' failed: {(int)response.StatusCode} {response.ReasonPhrase} - {content}");
                    }

                    var items = JsonSerializer.Deserialize<List<InvoiceApproverDTO>>(content, _options) ?? new List<InvoiceApproverDTO>();

                    MetaData? meta = null;
                    if (response.Headers.TryGetValues("X-Pagination", out var hdrs))
                    {
                        var hdr = hdrs.FirstOrDefault();
                        if (!string.IsNullOrWhiteSpace(hdr))
                        {
                            try
                            {
                                meta = JsonSerializer.Deserialize<MetaData>(hdr, _options);
                            }
                            catch
                            {
                                // ignore header parse errors
                                meta = null;
                            }
                        }
                    }

                    return new PagingResponse<InvoiceApproverDTO>
                    {
                        Items = items,
                        MetaData = meta
                    };
                }
                catch (HttpRequestException)
                {
                    // transient network/browser fetch failure — retry with backoff
                    if (attempt == maxAttempts)
                    {
                        // return empty result instead of letting the exception bubble to the renderer
                        return new PagingResponse<InvoiceApproverDTO>
                        {
                            Items = new List<InvoiceApproverDTO>(),
                            MetaData = null
                        };
                    }
                    // jittered backoff
                    await Task.Delay(200 * attempt);
                }
                catch (Exception)
                {
                    // non-transient error — do not retry
                    return new PagingResponse<InvoiceApproverDTO>
                    {
                        Items = new List<InvoiceApproverDTO>(),
                        MetaData = null
                    };
                }
            }

            // fallback (shouldn't reach)
            return new PagingResponse<InvoiceApproverDTO>
            {
                Items = new List<InvoiceApproverDTO>(),
                MetaData = null
            };
        }
        private class InvoiceApproverResponse
        {
            public bool isAssigned { get; set; }
            public string assignedApproverTypes { get; set; } = string.Empty;
        }

    }
}
