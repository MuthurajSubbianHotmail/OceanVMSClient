using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using MudBlazor;
using OceanVMSClient.HttpRepo.Authentication;
using OceanVMSClient.HttpRepoInterface.InvoiceModule;
using Shared.DTO.POModule;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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
        private InvoiceParameters _invoiceParameters = new InvoiceParameters();

        // Add this field to the class to fix CS0103
        private MudDateRangePicker? _date_range_picker;
        private readonly HashSet<Guid> _expandedRows = new();
        private bool IsRowExpanded(Guid id) => _expandedRows.Contains(id);
        private string invoiceViewPage = string.Empty;
        // user context
        private string? _userType;
        private Guid? _vendorId;
        private Guid? _vendorContactId;
        private Guid? _employeeId;
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
                await LoadUserContextAsync();
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

                var response = await InvoiceRepository.GetAllInvoices(_invoiceParameters);


                // choose repository call based on user type
                if (string.Equals(_userType, "VENDOR", StringComparison.OrdinalIgnoreCase))
                {
                    response = await InvoiceRepository.GetInvoicesByVendorId(_vendorId ?? Guid.Empty, _invoiceParameters);
                }
                else
                {
                    response = await InvoiceRepository.GetInvoicesByApproverEmployeeId(_employeeId ?? Guid.Empty, _invoiceParameters);
                }

                return new TableData<InvoiceDto>
                {
                    Items = response.Items?.ToList() ?? new List<InvoiceDto>(),
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
            public DateTime? InvoiceFromDate { get; set; }
            public DateTime? InvoiceToDate { get; set; }
            public string? SAPPoNo { get; set; }
            public decimal? MinInvoiceTotal { get; set; }
            public decimal? MaxInvoiceTotal { get; set; }
            public string? VendorName { get; set; }
        }


        #region User context helpers

        private async Task LoadUserContextAsync()
        {
            var authState = await AuthState;
            var user = authState.User;

            if (user?.Identity?.IsAuthenticated == true)
            {
                _userType = GetClaimValue(user, "userType");
                _vendorId = ParseGuid(GetClaimValue(user, "vendorPK") ?? GetClaimValue(user, "vendorId"));
                _vendorContactId = ParseGuid(GetClaimValue(user, "vendorContactId") ?? GetClaimValue(user, "vendorContact"));
                _employeeId = ParseGuid(GetClaimValue(user, "empPK") ?? GetClaimValue(user, "EmployeeId"));
            }
            else
            {
                NavigationManager.NavigateTo("/");
                return;
            }

            if (string.IsNullOrWhiteSpace(_userType))
            {
                _userType = await LocalStorage.GetItemAsync<string>("userType");
            }

            if (string.Equals(_userType, "VENDOR", StringComparison.OrdinalIgnoreCase))
            {
                _vendorId ??= ParseGuid(await LocalStorage.GetItemAsync<string>("vendorPK"));
                _vendorContactId ??= ParseGuid(await LocalStorage.GetItemAsync<string>("vendorContactId"));
            }
            else
            {
                _employeeId ??= ParseGuid(await LocalStorage.GetItemAsync<string>("empPK"));
            }
        }

        #endregion

        #region Helpers

        private static string? GetClaimValue(ClaimsPrincipal? user, string claimType)
        {
            if (user == null) return null;
            var claim = user.Claims.FirstOrDefault(c => string.Equals(c.Type, claimType, StringComparison.OrdinalIgnoreCase))
                        ?? user.Claims.FirstOrDefault(c => c.Type.EndsWith($"/{claimType}", StringComparison.OrdinalIgnoreCase))
                        ?? user.Claims.FirstOrDefault(c => c.Type.EndsWith(claimType, StringComparison.OrdinalIgnoreCase));
            return claim?.Value;
        }

        private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var g) ? g : (Guid?)null;

        #endregion
    }
}