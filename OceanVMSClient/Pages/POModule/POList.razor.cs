using Blazored.LocalStorage;
using Entities.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;
using OceanVMSClient.Features;
using OceanVMSClient.HttpRepo.Authentication;
using OceanVMSClient.HttpRepo.POModule;
using OceanVMSClient.HttpRepoInterface.PoModule;
using OceanVMSClient.HttpRepoInterface.POModule;
using Shared.DTO;
using Shared.DTO.POModule;
using Shared.DTO.VendorReg;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Threading;

namespace OceanVMSClient.Pages.POModule
{
    public partial class POList
    {
        #region Injected services & cascading parameters

        [CascadingParameter]
        public Task<AuthenticationState> AuthState { get; set; } = default!;

        [Inject]
        public ILocalStorageService LocalStorage { get; set; } = default!;

        [Inject]
        public IPurchaseOrderRepository Repository { get; set; } = default!;

        [Inject]
        public IInvoiceApproverRepository invoiceApproverRepository { get; set; } = default!; // new

        [Inject]
        public ISnackbar Snackbar { get; set; } = default!; // new

        [Inject]
        private NavigationManager NavigationManager { get; set; } = null!;

        [Inject]
        private ILogger<POList> Logger { get; set; } = default!; // new

        [Inject]
        public HttpInterceptorService Interceptor { get; set; } = default!;
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
        #endregion

        #region Private fields / state

        // UI state
        private MudTable<PurchaseOrderDto>? _table;
        private DateRange? _poDateRange;
        private MudDateRangePicker? _dateRangePicker;

        // data / parameters
        private PurchaseOrderParameters _purchaseOrderParameters = new PurchaseOrderParameters();
        private readonly int[] _pageSizeOption = { 15, 25, 50 };

        // expansion state
        private readonly HashSet<Guid> _expandedRows = new();

        // user context
        private string? _userType;
        private Guid? _vendorId;
        private Guid? _vendorContactId;
        private Guid? _employeeId;

        // search/filter fields
        private string? sapPONo = string.Empty;
        private string? vendorName = string.Empty;
        private decimal? minTotalValue = null;
        private decimal? maxTotalValue = null;

        // lookup lists
        private List<string> invoiceStatuses { get; } = new()
        {
            "All",
            "Not Invoiced",
            "Part Invoiced",
            "Fully Invoiced",
            "Over Invoiced"
        };

        private string? selectedInvoiceStatus = "All";

        // permission cache per PurchaseOrder Id
        private readonly ConcurrentDictionary<Guid, bool> _canCreateInvoiceMap = new();
        #endregion

        private async Task EvaluateCreatePermissionsForItemsAsync(IEnumerable<PurchaseOrderDto> items)
        {
            if (items == null) return;

            var tasks = items.Select(item => EvaluateCreatePermissionForAsync(item));
            try
            {
                await Task.WhenAll(tasks);
            }
            catch
            {
                // individual failures are handled inside EvaluateCreatePermissionForAsync
            }
        }

        private async Task EvaluateCreatePermissionForAsync(PurchaseOrderDto po)
        {
            if (po == null || po.Id == Guid.Empty)
            {
                return;
            }

            // Default deny
            bool allowed = false;

            // If user is Employee -> check approver assignment asynchronously
            if (string.Equals(_userType, "Employee", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (_employeeId == null || _employeeId == Guid.Empty)
                    {
                        allowed = false;
                    }
                    else
                    {
                        var empInitiatorPermission = await invoiceApproverRepository.IsInvoiceApproverAsync(po.ProjectId, _employeeId.Value);
                        var assignedType = empInitiatorPermission.AssignedType ?? string.Empty;

                        // enforce rule:
                        // Employee can create only when PO allows initiator upload AND the employee is assigned as Initiator
                        if (po.AllowInvUploadByInitiator.GetValueOrDefault(false))
                        {
                            allowed = assignedType.Equals("Initiator", StringComparison.OrdinalIgnoreCase);
                        }
                        else
                        {
                            allowed = false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Error checking invoice approver assignment for PO {PoId}", po.Id);
                    allowed = false;
                }
            }
            else
            {
                // Non-employee users (vendors/admins) are allowed
                allowed = true;
            }

            _canCreateInvoiceMap[po.Id] = allowed;
        }

        private bool GetCanCreateInvoiceForRow(PurchaseOrderDto po)
        {
            if (po == null) return false;

            // if not employee, allow
            if (!string.Equals(_userType, "Employee", StringComparison.OrdinalIgnoreCase))
                return true;

            // employee: consult cache (default false)
            return _canCreateInvoiceMap.TryGetValue(po.Id, out var allowed) && allowed;
        }

        #region Lifecycle

        protected override async Task OnInitializedAsync()
        {
            try
            {
                await LoadUserContextAsync();
            }
            catch
            {
                _userType = null;
                Console.WriteLine("Failed to retrieve user type from claims/local storage.");
            }
        }

        #endregion

        #region Data loading / Server data for table

        /// <summary>
        /// Server data provider for MudTable. Receives paging/sorting state and returns page results.
        /// </summary>
        private async Task<TableData<PurchaseOrderDto>> GetServerData(TableState state, CancellationToken cancellationToken)
        {
            Interceptor.RegisterEvent();

            // prepare parameters from UI state
            _purchaseOrderParameters.PageSize = state.PageSize;
            _purchaseOrderParameters.PageNumber = state.Page + 1;
            _purchaseOrderParameters.InvoiceStatus = selectedInvoiceStatus == "All" ? null : selectedInvoiceStatus;

            PagingResponse<PurchaseOrderDto> response;

            // choose repository call based on user type
            if (string.Equals(_userType, "VENDOR", StringComparison.OrdinalIgnoreCase))
            {
                response = await Repository.GetAllPurchaseOrdersOfVendorAsync(_vendorId ?? Guid.Empty, _purchaseOrderParameters);
            }
            else
            {
                response = await Repository.GetAllPurchaseOrdersOfApproversAsync(_employeeId ?? Guid.Empty, _purchaseOrderParameters);
            }

            var items = response.Items?.ToList() ?? new List<PurchaseOrderDto>();
            StoreCurrentPageItems(items);
            // Evaluate create-invoice permission for each returned PO (populates cache)
            // Await evaluation so buttons render with correct enabled/disabled state.
            await EvaluateCreatePermissionsForItemsAsync(items);
            
            return new TableData<PurchaseOrderDto>
            {
                Items = items,
                TotalItems = response.MetaData?.TotalCount ?? 0
            };
        }

        #endregion

        #region UI helpers (formatting / colors / row expansion)

        private static string FormatDate(DateTime? date) => date.HasValue ? date.Value.ToString("dd-MMM-yy") : string.Empty;

        private void ToggleRow(Guid id)
        {
            if (!_expandedRows.Add(id))
                _expandedRows.Remove(id);

            StateHasChanged();
        }

        private bool IsRowExpanded(Guid id) => _expandedRows.Contains(id);
        private Color GetInvoiceChipColor(string invoiceStatus)
        {
            return invoiceStatus.ToLower() switch
            {
                "part invoiced" => Color.Info,
                "not invoiced" => Color.Default,
                "rejected" => Color.Error,
                "fully invoiced" => Color.Success,
                "over invoiced" => Color.Warning,
                _ => Color.Default
            };
        }
        private Color GetStatusColor(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return Color.Default;

            var s = status.Trim().ToLowerInvariant();

            return s switch
            {
                var x when x.Contains("part invoiced") => Color.Info,
                var x when x.Contains("not invoiced") => Color.Default,
                var x when x.Contains("fully invoiced") => Color.Success,
                var x when x.Contains("over invoiced") => Color.Warning,
                _ => Color.Default
            };
        }

        #endregion

        #region Event handlers / filter actions

        private async Task OnInvoiceStatusChanged(string status)
        {
            selectedInvoiceStatus = status;
            _purchaseOrderParameters.InvoiceStatus = string.IsNullOrWhiteSpace(status) || status == "All" ? null : status;

            if (_table is not null)
            {
                await _table.ReloadServerData();
            }
        }

        private async Task OnSearch()
        {
            _purchaseOrderParameters.SAPPONumber = string.IsNullOrWhiteSpace(sapPONo) ? null : sapPONo;
            _purchaseOrderParameters.VendorName = string.IsNullOrWhiteSpace(vendorName) ? null : vendorName;

            if (minTotalValue.HasValue || maxTotalValue.HasValue)
            {
                _purchaseOrderParameters.MinTotalValue = minTotalValue ?? 0m;
                _purchaseOrderParameters.MaxTotalValue = maxTotalValue ?? 1000000000m;
            }
            else
            {
                _purchaseOrderParameters.MinTotalValue = 0m;
                _purchaseOrderParameters.MaxTotalValue = 1000000000m;
            }

            if (_table is not null)
            {
                await _table.ReloadServerData();
            }
        }

        private async Task OnDateRangeChanged(DateRange? range)
        {
            _poDateRange = range;
            TrySetParameterDate("FromDate", range?.Start);
            TrySetParameterDate("ToDate", range?.End);
            TrySetParameterDate("StartDate", range?.Start);
            TrySetParameterDate("EndDate", range?.End);
            TrySetParameterDate("PODateFrom", range?.Start);
            TrySetParameterDate("PODateTo", range?.End);

            if (_table is not null)
                await _table.ReloadServerData();
        }

        private async Task OnDateRangeSearch(DateRange dateRange)
        {
            _poDateRange = dateRange;

            if (_dateRangePicker is not null)
                await _dateRangePicker.CloseAsync();

            _purchaseOrderParameters.POStartDate = dateRange.Start ?? DefaultPOStartDate;
            _purchaseOrderParameters.POEndDate = dateRange.End ?? DateTime.Now;

            if (_table is not null)
                await _table.ReloadServerData();
        }

        private async Task ClearDateRange()
        {
            _poDateRange = null;

            if (_dateRangePicker is not null)
                await _dateRangePicker.CloseAsync();

            if (_table is not null)
                await _table.ReloadServerData();
        }

        #endregion

        #region Reflection helper for setting various date properties

        private void TrySetParameterDate(string propName, DateTime? value)
        {
            if (_purchaseOrderParameters == null) return;

            var t = _purchaseOrderParameters.GetType();
            var prop = t.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop == null || !prop.CanWrite) return;

            if (prop.PropertyType == typeof(DateTime?) || prop.PropertyType == typeof(DateTime))
            {
                object? setValue = value;
                if (value == null && prop.PropertyType == typeof(DateTime))
                    setValue = default(DateTime);
                prop.SetValue(_purchaseOrderParameters, setValue);
            }
        }

        #endregion

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

        #region Constants

        private static readonly DateTime DefaultPOStartDate = new(2025, 1, 1);

        #endregion

        private void newInvoice(Guid vendorId, Guid purchaseOrderId)
        {
            // Basic validation
            if (vendorId == Guid.Empty || purchaseOrderId == Guid.Empty)
            {
                Console.WriteLine("Invalid vendor or purchase order id for new invoice.");
                return;
            }

            // Navigate to create-invoice page — adjust route as your app expects
            NavigationManager.NavigateTo($"/newinvoice/{vendorId}/{purchaseOrderId}");
        }

        private static string FormatCurrency(decimal? value)
        {
            if (!value.HasValue) return "—";
            return $"₹{value.Value.ToString("N2", CultureInfo.InvariantCulture)}";
        }
        private List<PurchaseOrderDto> _lastPageItems = new();

        // Call this from your existing GetServerData(...) after you retrieve the page items:
        // StoreCurrentPageItems(result.Items);
        private void StoreCurrentPageItems(IEnumerable<PurchaseOrderDto>? items)
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
            sb.AppendLine("PO Type,SAP PoNo,PO Date,Vendor Name,Item Value,GST Total,Total Value,Invoice Status");

            foreach (var item in _lastPageItems)
            {
                var potype = EscapeCsv(item.PoTypeName);
                var ponumber = EscapeCsv(item.SAPPONumber);
                var podate = EscapeCsv(item.SAPPODate == default ? string.Empty : item.SAPPODate.ToString("dd-MMM-yy"));
                var supplinerName = EscapeCsv(item.VendorName);
                var poitemvalue = item.ItemValue;
                var taxvalue = item.GSTTotal;
                var totalvalue = item.TotalValue;
                var invoicestatus = EscapeCsv(item.InvoiceStatus);

                sb.AppendLine($"{potype},{ponumber},{podate},{supplinerName},{poitemvalue},{taxvalue},{totalvalue},{invoicestatus}");
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var base64 = Convert.ToBase64String(bytes);

            // Calls the JS helper to trigger download
            await JSRuntime.InvokeVoidAsync("downloadFileFromBase64", "PurchaseOrderList.csv", base64);
        }
    }
}