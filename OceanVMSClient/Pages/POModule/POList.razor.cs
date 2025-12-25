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
using System.Security.Claims;
using System.Threading;
using System.Reflection;
namespace OceanVMSClient.Pages.POModule
{
    public partial class PoList
    {
        private bool _ShowChildContent = false;
        private readonly HashSet<Guid> _expandedRows = new();

        private MudTable<PurchaseOrderDto>? _table;
        private PurchaseOrderParameters _purchaseOrderParameters = new PurchaseOrderParameters();
        private readonly int[] _pageSizeOption = { 15, 25, 50 };
        [CascadingParameter]
        public Task<AuthenticationState> AuthState { get; set; } = default!;

        // Inject Blazored local storage to read userType
        [Inject]
        public ILocalStorageService LocalStorage { get; set; } = default!;

        // Cached user type loaded from local storage before fetching data
        private string? _userType;
        private Guid? _vendorId;
        private Guid? _vendorContactId;
        private Guid? _employeeId;

        

        [Inject]
        public IPurchaseOrderRepository Repository { get; set; } = default!;
        // injection changed to satisfy definite assignment
        [Inject]
        public HttpInterceptorService Interceptor { get; set; } = default!;

        [Inject]
        private NavigationManager NavigationManager { get; set; } = null!;

        protected override async Task OnInitializedAsync()
        {
            try
            {
                // Try to get values from authenticated user's claims first
                var authState = await AuthState;
                var user = authState.User;

                if (user?.Identity?.IsAuthenticated == true)
                {
                    //_userType = GetClaimValue(user, "userType")
                    //    ?? GetClaimValue(user, ClaimTypes.Role)
                    //    ?? GetClaimValue(user, "role");

                    // Try vendor/employee ids from claims (claim names depend on your auth server)
                    _userType = GetClaimValue(user, "userType");
                    _vendorId = ParseGuid(GetClaimValue(user, "vendorPK") ?? GetClaimValue(user, "vendorId"));
                    _vendorContactId = ParseGuid(GetClaimValue(user, "vendorContactId") ?? GetClaimValue(user, "vendorContact"));
                    _employeeId = ParseGuid(GetClaimValue(user, "empPK") ?? GetClaimValue(user, "EmployeeId"));
                }
                else
                {
                    NavigationManager.NavigateTo("/");
                }


                // Fallback to local storage if claims do not contain the info
                if (string.IsNullOrWhiteSpace(_userType))
                {
                    _userType = await LocalStorage.GetItemAsync<string>("userType");
                }

                if (_userType == "VENDOR")
                {
                    _vendorId ??= ParseGuid(await LocalStorage.GetItemAsync<string>("vendorPK"));
                    _vendorContactId ??= ParseGuid(await LocalStorage.GetItemAsync<string>("vendorContactId"));
                }
                else
                {
                    _employeeId ??= ParseGuid(await LocalStorage.GetItemAsync<string>("empPK"));
                }
            }
            catch
            {
                _userType = null;
                Console.WriteLine("Failed to retrieve user type from claims/local storage.");
            }
        }

        // Helper: safely read a claim value (handles various claim name shapes)
        private static string? GetClaimValue(ClaimsPrincipal? user, string claimType)
        {
            if (user == null) return null;
            var claim = user.Claims.FirstOrDefault(c => string.Equals(c.Type, claimType, StringComparison.OrdinalIgnoreCase))
                        ?? user.Claims.FirstOrDefault(c => c.Type.EndsWith($"/{claimType}", StringComparison.OrdinalIgnoreCase))
                        ?? user.Claims.FirstOrDefault(c => c.Type.EndsWith(claimType, StringComparison.OrdinalIgnoreCase));
            return claim?.Value;
        }

        // Helper: parse GUID safely
        private static Guid? ParseGuid(string? value)
        {
            return Guid.TryParse(value, out var g) ? g : (Guid?)null;
        }

        // signature must accept CancellationToken to match MudBlazor ServerData delegate
        private async Task<TableData<PurchaseOrderDto>> GetServerData(TableState state, CancellationToken cancellationToken)
        {
            Interceptor.RegisterEvent();
            var authState = await AuthState;
            var user = authState.User;
           
            _purchaseOrderParameters.PageSize = state.PageSize;
            _purchaseOrderParameters.PageNumber = state.Page + 1;
            _purchaseOrderParameters.InvoiceStatus = selectedInvoiceStatus == "All" ? null : selectedInvoiceStatus;
            //_purchaseOrderParameters.SAPPONumber = sapPONo;
            PagingResponse<PurchaseOrderDto> response; // <-- Declare the variable

            // pass cancellationToken to repository if supported, otherwise ignore it
            if (_userType.ToUpper() == "VENDOR")
            {
                response = await Repository.GetAllPurchaseOrdersOfVendorAsync(
                    _vendorId ?? Guid.Empty,
                    _purchaseOrderParameters);
            }
            else
            {
                response = await Repository.GetAllPurchaseOrdersOfApproversAsync(
                    _employeeId ?? Guid.Empty,
                    _purchaseOrderParameters);
            }


            return new TableData<PurchaseOrderDto>
            {
                Items = response.Items?.ToList() ?? new List<PurchaseOrderDto>(),
                TotalItems = response.MetaData?.TotalCount ?? 0
            };
        }

        private Color GetStatusColor(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return Color.Default;

            var s = status.Trim().ToLowerInvariant();

            return s switch
            {
                var x when x.Contains("part invoiced") => Color.Warning,   // yellow
                var x when x.Contains("not invoiced") => Color.Success,   // green
                var x when x.Contains("fully invoiced") => Color.Info,    // blue (or Color.Primary)
                _ => Color.Default
            };
        }
        // Helper to format SAPPODate as dd-MMM-yy (e.g. 25-Dec-25). Handles nullable DateTime.
        private static string FormatDate(DateTime? date)
        {
            return date.HasValue ? date.Value.ToString("dd-MMM-yy") : string.Empty;
        }

        private void ToggleRow(Guid id)
        {
            if (!_expandedRows.Add(id))
                _expandedRows.Remove(id);
            StateHasChanged();
        }

        private bool IsRowExpanded(Guid id) => _expandedRows.Contains(id);

        // list of statuses for the select
        private List<string> invoiceStatuses { get; } = new()
        {
            "All",
            "Not Invoiced",
            "Part Invoiced",
            "Fully Invoiced"
        };

        // currently selected status (default "All")
        private string? selectedInvoiceStatus = "All";

        // OnInvoiceStatusChanged now awaits reload and guards _table
        private async Task OnInvoiceStatusChanged(string status)
        {
            selectedInvoiceStatus = status;
            _purchaseOrderParameters.InvoiceStatus = string.IsNullOrWhiteSpace(status) || status == "All" ? null : status;

            if (_table is not null)
            {
                await _table.ReloadServerData();
            }
        }
        // ensure these fields exist (types)
        private string? sapPONo = string.Empty;
        private string? vendorName = string.Empty;
        private decimal? minTotalValue = null;
        private decimal? maxTotalValue = null;

        // replace the previous OnSearch signature with this parameterless handler
        private async Task OnSearch()
        {
            _purchaseOrderParameters.SAPPONumber = string.IsNullOrWhiteSpace(sapPONo) ? null : sapPONo;
            _purchaseOrderParameters.VendorName = string.IsNullOrWhiteSpace(vendorName) ? null : vendorName;

            if (minTotalValue.HasValue || maxTotalValue.HasValue)
            {
                _purchaseOrderParameters.MinTotalValue = minTotalValue ?? 0m;
                _purchaseOrderParameters.MaxTotalValue = maxTotalValue ?? 1000000000m;
                //_purchaseOrderParameters.ValidTotalValueRange = true;
            }
            else
            {
                _purchaseOrderParameters.MinTotalValue = 0m;
                _purchaseOrderParameters.MaxTotalValue = 1000000000m;

                //_purchaseOrderParameters.ValidTotalValueRange = false;
            }

            if (_table is not null)
            {
                await _table.ReloadServerData();
            }
        }

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
        private MudDateRangePicker? _dateRangePicker;
        private DateRange? _poDateRange;

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

        private static readonly DateTime DefaultPOStartDate = new DateTime(2025, 1, 1);

        private async Task OnDateRangeSearch(DateRange dateRange)
        {
            _poDateRange = dateRange;

            // close the picker popup after selection
            if (_dateRangePicker is not null)
                await _dateRangePicker.CloseAsync();
            _purchaseOrderParameters.POStartDate = dateRange.Start ?? DefaultPOStartDate;
            _purchaseOrderParameters.POEndDate = dateRange.End ?? DateTime.Now;
            // reload table with new filter
            if (_table is not null)
                await _table.ReloadServerData();
        }
        private async Task ClearDateRange()
        {
            _poDateRange = null;

            // close picker if open
            if (_dateRangePicker is not null)
                await _dateRangePicker.CloseAsync();

            // clear any date filters you apply in parameters (if implemented) then reload
            if (_table is not null)
                await _table.ReloadServerData();
        }

      
    }
}

