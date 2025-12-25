using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using OceanVMSClient.HttpRepo.Authentication;
using OceanVMSClient.HttpRepoInterface.PoModule;
using Shared.DTO.POModule;
using System.Threading;
using Blazored.LocalStorage;
using OceanVMSClient.Features;
namespace OceanVMSClient.Pages.POModule
{
    public partial class PoList
    {
        private bool _ShowChildContent = false;
        private readonly HashSet<Guid> _expandedRows = new();
        private MudTable<PurchaseOrderDto>? _table;
        private PurchaseOrderParameters _productParameters = new PurchaseOrderParameters();
        private readonly int[] _pageSizeOption = { 15, 25, 50 };
        [CascadingParameter]
        public Task<AuthenticationState> AuthState { get; set; } = default!;

        // Inject Blazored local storage to read userType
        [Inject]
        public ILocalStorageService LocalStorage { get; set; } = default!;

        // Cached user type loaded from local storage before fetching data
        private string? _userTypeFromLocalStorage;

        [Inject]
        public IPurchaseOrderRepository Repository { get; set; } = default!;
        [Inject]
        public HttpInterceptorService Interceptor { get; set; }
        [Inject]
        private NavigationManager NavigationManager { get; set; } = null!;

        protected override async Task OnInitializedAsync()
        {
            try
            {
                _userTypeFromLocalStorage = await LocalStorage.GetItemAsync<string>("userType");
            }
            catch
            {
                _userTypeFromLocalStorage = null;
            }
        }

        // signature must accept CancellationToken to match MudBlazor ServerData delegate
        private async Task<TableData<PurchaseOrderDto>> GetServerData(TableState state, CancellationToken cancellationToken)
        {
            Interceptor.RegisterEvent();
            var authState = await AuthState;
            var user = authState.User;
            if (user.Identity.IsAuthenticated)
            {
                Console.WriteLine($"User {user.Identity.Name} is authenticated.");
            }
            else
            {
                Console.WriteLine("User is not authenticated. Redirecting to login page.");
                NavigationManager.NavigateTo("/");
                return null;
            }
            _productParameters.PageSize = state.PageSize;
            _productParameters.PageNumber = state.Page + 1;

            PagingResponse<PurchaseOrderDto> response; // <-- Declare the variable

            // pass cancellationToken to repository if supported, otherwise ignore it
            if (_userTypeFromLocalStorage == "VENDOR")
            {
                response = await Repository.GetAllPurchaseOrdersOfVendorAsync(
                    Guid.Parse(await LocalStorage.GetItemAsync<string>("vendorPK") ?? Guid.Empty.ToString()),
                    _productParameters);
            }
            else {
                response = await Repository.GetAllPurchaseOrders(_productParameters);
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
    }
}

