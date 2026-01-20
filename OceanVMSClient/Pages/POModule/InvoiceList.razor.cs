using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;
using OceanVMSClient.Features;
using OceanVMSClient.Helpers;
using OceanVMSClient.HttpRepo.Authentication;
using OceanVMSClient.HttpRepoInterface.InvoiceModule;
using Shared.DTO.POModule;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OceanVMSClient.Pages.POModule
{
    public partial class InvoiceList
    {
        [CascadingParameter]
        public Task<AuthenticationState> AuthState { get; set; } = default!;

        [Inject]
        public ILocalStorageService LocalStorage { get; set; } = default!;

        [Inject] public IInvoiceRepository InvoiceRepository { get; set; } = default!;
        [Inject] public ISnackbar Snackbar { get; set; } = default!;
        [Inject] public ILogger<InvoiceList> Logger { get; set; } = default!;
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
        [Inject] public NavigationManager NavigationManager { get; set; } = default!;

        private InvoiceParameters _invoiceParameters = new InvoiceParameters();

        // Add this field to the class to fix CS0103
        private MudDateRangePicker? _date_range_picker;
        private readonly HashSet<Guid> _expandedRows = new();
        private bool IsRowExpanded(Guid id) => _expandedRows.Contains(id);
        private string invoiceViewPage = string.Empty;
        // user context
        private string? _userType;
        private string? _role;
        private Guid? _vendorId;
        private Guid? _vendorContactId;
        private Guid? _employeeId;

        private MudTable<InvoiceDto>? _table;
        private MudDateRangePicker? _dateRange_picker;
        private DateRange? _invoice_date_range;

        // filter fields
        private string? invoiceRefNo;
        private string? sapPoNo;
        private decimal? minInvoiceTotal;
        private decimal? maxInvoiceTotal;
        private string? vendorName;
        private string? ProjectCode;

        private readonly int[] _pageSize_option = { 15, 25, 50 };
        private bool _filtersOpen = true;
        private void ToggleRow(Guid id)
        {
            if (!_expandedRows.Add(id))
                _expandedRows.Remove(id);

            StateHasChanged();
        }

        #region Lifecycle

        protected override async Task OnInitializedAsync()
        {
            try
            {
                var authState = await AuthState;
                var user = authState.User;

                if (user?.Identity?.IsAuthenticated != true)
                {
                    NavigationManager.NavigateTo("/");
                    return;
                }

                // Use the shared ClaimsHelper extension to load the user context (claims + local storage fallback)
                var ctx = await user.LoadUserContextAsync(LocalStorage);
                _userType = ctx.UserType;
                _role = ctx.Role;
                _vendorId = ctx.VendorId;
                _vendorContactId = ctx.VendorContactId;
                _employeeId = ctx.EmployeeId;

                if (string.Equals(_userType, "VENDOR", StringComparison.OrdinalIgnoreCase))
                {
                    invoiceViewPage = "invoiceviewvendor";
                }
                else
                {
                    invoiceViewPage = "invoiceview";
                }
            }
            catch
            {
                _userType = null;
                Console.WriteLine("Failed to retrieve user type from claims/local storage.");
            }
        }

        #endregion
        // ServerData provider for MudTable
        private async Task<TableData<InvoiceDto>> GetServerData(TableState state, CancellationToken cancellationToken)
        {
            try
            {
                // prepare query parameters from UI state
                _invoiceParameters.PageSize = state.PageSize;
                _invoiceParameters.PageNumber = state.Page + 1;
                _invoiceParameters.SAPPONumber = string.IsNullOrWhiteSpace(sapPoNo) ? null : sapPoNo;
                _invoiceParameters.InvoiceRefNo = string.IsNullOrWhiteSpace(invoiceRefNo) ? null : invoiceRefNo;
                _invoiceParameters.VendorName = string.IsNullOrWhiteSpace(vendorName) ? null : vendorName;
                _invoiceParameters.ProjectCode = string.IsNullOrWhiteSpace(ProjectCode) ? null : ProjectCode;
                if (minInvoiceTotal.HasValue || maxInvoiceTotal.HasValue)
                {
                    _invoiceParameters.MinTotalValue = minInvoiceTotal;
                    _invoiceParameters.MaxTotalValue = maxInvoiceTotal;
                }
                else
                {
                    _invoiceParameters.MinTotalValue = null;
                    _invoiceParameters.MaxTotalValue = null;
                }
                _invoiceParameters.InvStartDate = _invoice_date_range?.Start ?? default;
                _invoiceParameters.InvEndDate = _invoice_date_range?.End ?? default;

                PagingResponse<InvoiceDto> response;

                // Vendor users always get vendor-specific invoices
                if (string.Equals(_userType, "VENDOR", StringComparison.OrdinalIgnoreCase))
                {
                    response = await InvoiceRepository.GetInvoicesByVendorId(_vendorId ?? Guid.Empty, _invoiceParameters);
                }
                else
                {
                    // Choose repository call based on role for non-vendor users:
                    // - Account Payable role => GetInvoicesWithAPReviewNotNAAsync
                    // - Admin role => GetAllInvoices
                    // - Other employee roles => GetInvoicesByApproverEmployeeId
                    if (RoleHelper.IsAccountPayableRole(_role))
                    {
                        response = await InvoiceRepository.GetInvoicesWithAPReviewNotNAAsync(_invoiceParameters);
                    }
                    else if (RoleHelper.IsAdminRole(_role))
                    {
                        response = await InvoiceRepository.GetAllInvoices(_invoiceParameters);
                    }
                    else if (_employeeId.HasValue)
                    {
                        response = await InvoiceRepository.GetInvoicesByApproverEmployeeId(_employeeId.Value, _invoiceParameters);
                    }
                    else
                    {
                        // Fallback - safe default to all invoices
                        response = await InvoiceRepository.GetAllInvoices(_invoiceParameters);
                    }
                }

                var items = response.Items?.ToList() ?? new List<InvoiceDto>();
                StoreCurrentPageItems(items);
                return new TableData<InvoiceDto>
                {
                    Items = items,
                    TotalItems = response.MetaData?.TotalCount ?? 0
                };
            }
            catch (OperationCanceledException)
            {
                return new TableData<InvoiceDto> { Items = new List<InvoiceDto>(), TotalItems = 0 };
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error loading invoices");
                Snackbar.Add("Failed to load invoices.", Severity.Error);
                return new TableData<InvoiceDto> { Items = new List<InvoiceDto>(), TotalItems = 0 };
            }
        }

        private Task OnSearch()
        {
            if (_table != null)
                return _table.ReloadServerData();
            return Task.CompletedTask;
        }

        private async Task OnDateRangeSearch(DateRange dateRange)
        {
            _invoice_date_range = dateRange;
            if (_date_range_picker != null)
                await _date_range_picker.CloseAsync();
            await OnSearch();
        }

        private async Task ClearDateRange()
        {
            _invoice_date_range = null;
            if (_date_range_picker != null)
                await _date_range_picker.CloseAsync();
            await OnSearch();
        }
        private static string FormatDate(DateTime? date) => date.HasValue ? date.Value.ToString("dd-MMM-yy") : string.Empty;

        private string GetDateRangeTooltip()
        {
            if (_invoice_date_range == null || (_invoice_date_range.Start == null && _invoice_date_range.End == null))
                return "No date range selected";

            var start = _invoice_date_range.Start?.ToString("dd-MMM-yy") ?? "—";
            var end = _invoice_date_range.End?.ToString("dd-MMM-yy") ?? "—";
            return $"{start} To {end}";
        }
        private async Task DownloadInvoiceAsync(string? fileUrl)
        {
            if (string.IsNullOrWhiteSpace(fileUrl))
                return;

            await JS.InvokeVoidAsync("open", fileUrl, "_blank");
        }

        private async Task OpenInvoice(Guid invoiceId)
        {
            if (string.IsNullOrWhiteSpace(invoiceViewPage))
                return;
            var url = $"{invoiceViewPage}/{invoiceId}";
            NavigationManager.NavigateTo(url);
            await Task.CompletedTask;
        }
        // map invoice status to chip color
        private Color GetInvoiceChipColor(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return Color.Default;

            var s = status.Trim().ToLowerInvariant();
            return s switch
            {
                "approved" or "paid" or "fully invoiced" => Color.Success,
                "rejected" or "declined" or "overdue" => Color.Error,
                "submitted" or "awaiting approval" or "awaiting" => Color.Default,
                "with initiator" or "with checker" or "with validator" or "with approver" or "under review" => Color.Warning,
                "part invoiced" or "partially paid" or "partial" => Color.Info,
                "cancelled" or "void" => Color.Secondary,
                _ => Color.Secondary
            };
        }

        // Return index for MudStepper ActiveIndex (0-based)
        private int GetWorkflowStepIndex(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return 0;

            var s = status.Trim().ToLowerInvariant();

            // Map known workflow labels to step index
            return s switch
            {
                "submitted" => 0,
                "initiator" or "initiator review" or "initiator review required" => 1,
                "checker" or "checker review" or "checker review required" => 2,
                "validator" or "validator review" or "validator review required" => 3,
                "approver" or "approver review" or "awaiting approval" => 4,
                "accounts payable" or "accounts" or "ap" => 5,
                "paid" or "approved" or "fully invoiced" => 6,
                _ => 0
            };
        }

        public class InvoiceQueryParameters
        {
            public int PageNumber { get; set; } = 1;
            public int PageSize { get; set; } = 15;
            public string? InvoiceRefNo { get; set; }
            public string? ProjectCode { get; set; }
            public DateTime? InvoiceFromDate { get; set; }
            public DateTime? InvoiceToDate { get; set; }
            public string? SAPPoNo { get; set; }
            public decimal? MinInvoiceTotal { get; set; }
            public decimal? MaxInvoiceTotal { get; set; }
            public string? VendorName { get; set; }
        }


        #region Export to excel
        private List<InvoiceDto> _lastPageItems = new();
        private void StoreCurrentPageItems(IEnumerable<InvoiceDto>? items)
        {
            _lastPageItems = items?.ToList() ?? new();
        }
        private static string EscapeCsv(string? s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;
            var escaped = s.Replace("\"", "\"\"");
            return $"\"{escaped}\"";
        }
        private async Task ExportVisibleToCsv()
        {
            if (_lastPageItems == null || !_lastPageItems.Any())
            {
                // Optional: show a notification to user that there is nothing to export.
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("Invoice Ref No,Invoice Date,Vendor Name,SAP PO No,SAP PO Date,PO Total,Invoice Total,Invoice Status,Payment Status");

            foreach (var item in _lastPageItems)
            {
                var invoiceRefNo = EscapeCsv(item.InvoiceRefNo);
                var invDate = EscapeCsv(item.InvoiceDate == default ? string.Empty : item.InvoiceDate.ToString("dd-MMM-yy"));
                var vendor = EscapeCsv(item.VendorName);
                var SapPoNo = EscapeCsv(item.SAPPONumber);
                var SapPoDate = item.SAPPODate == default ? string.Empty : item.SAPPODate.ToString("dd-MMM-yy");
                var PoTotal = item.POTotalValue;
                var invTotal = item.InvoiceTotalValue;
                var invoicestatus = EscapeCsv(item.InvoiceStatus);
                var PaymentStatus = EscapeCsv(item.PaymentStatus);

                sb.AppendLine($"{invoiceRefNo},{invDate},{vendor},{SapPoNo},{SapPoDate},{PoTotal},{invTotal},{invoicestatus},{PaymentStatus}");
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var base64 = Convert.ToBase64String(bytes);

            // Calls the JS helper to trigger download
            await JSRuntime.InvokeVoidAsync("downloadFileFromBase64", "InvoiceList.csv", base64);
        }
        #endregion
    }
}