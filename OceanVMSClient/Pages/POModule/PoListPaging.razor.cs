using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using OceanVMSClient.HttpRepo.Authentication;
using OceanVMSClient.HttpRepoInterface.PoModule;
using Shared.DTO.POModule;
using System.Threading;

namespace OceanVMSClient.Pages.POModule
{
    public partial class PoListPaging
    {
        private MudTable<PurchaseOrderDto>? _table;
        private PurchaseOrderParameters _productParameters = new PurchaseOrderParameters();
        private readonly int[] _pageSizeOption = { 2, 4, 6 };
        [CascadingParameter]
        public Task<AuthenticationState> AuthState { get; set; } = default!;

        [Inject]
        public IPurchaseOrderRepository Repository { get; set; } = default!;
        [Inject]
        public HttpInterceptorService Interceptor { get; set; }
        [Inject]
        private NavigationManager NavigationManager { get; set; } = null!;

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

            // pass cancellationToken to repository if supported, otherwise ignore it
            var response = await Repository.GetAllPurchaseOrders(_productParameters);

            return new TableData<PurchaseOrderDto>
            {
                Items = response.Items?.ToList() ?? new List<PurchaseOrderDto>(),
                TotalItems = response.MetaData?.TotalCount ?? 0
            };
        }
    }
}
