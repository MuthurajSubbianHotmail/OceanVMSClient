using Blazored.LocalStorage;
using Entities.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using OceanVMSClient.Features;
using OceanVMSClient.HttpRepo.Authentication;
using OceanVMSClient.HttpRepoInterface.PoModule;
using Shared.DTO;
using Shared.DTO.POModule;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
namespace OceanVMSClient.Pages.POModule
{
    public partial class PoList
    {
        #region Injected services & cascading parameters

        [CascadingParameter]
        public Task<AuthenticationState> AuthState { get; set; } = default!;

        [Inject]
        public ILocalStorageService LocalStorage { get; set; } = default!;

        [Inject]
        public IPurchaseOrderRepository Repository { get; set; } = default!;

        [Inject]
        public HttpInterceptorService Interceptor { get; set; } = default!;

        [Inject]
        private NavigationManager NavigationManager { get; set; } = null!;

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

        #endregion

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

            return new TableData<PurchaseOrderDto>
            {
                Items = response.Items?.ToList() ?? new List<PurchaseOrderDto>(),
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
    }
}