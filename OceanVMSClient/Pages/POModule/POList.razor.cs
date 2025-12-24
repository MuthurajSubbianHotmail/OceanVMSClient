using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using OceanVMSClient.HttpRepo.Authentication;
using OceanVMSClient.HttpRepoInterface.PoModule;
using Shared.DTO.POModule;
using Shared.RequestFeatures;
using System.Reflection.Metadata.Ecma335;

namespace OceanVMSClient.Pages.POModule
{
    public partial class POList : IDisposable
    {
        [CascadingParameter]
        public Task<AuthenticationState> AuthState { get; set; } = default!;    
        public List<PurchaseOrderDto>? _purchaseOrders { get; set; } = new();
        [Inject]
        public IPurchaseOrderRepository PurchaseOrderRepository { get; set; } = default!;
        [Inject]
        public HttpInterceptorService Interceptor {  get; set; }
        public MetaData MetaData { get; set; } = new MetaData();
        private PurchaseOrderParameters _purchaseOrderParameters = new PurchaseOrderParameters();


        [Inject]
        private NavigationManager NavigationManager { get; set; } = null!;

        protected override async Task OnInitializedAsync()
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
                return;
            }
            _purchaseOrderParameters.PageNumber = 1;
            _purchaseOrderParameters.PageSize = 20;
            await LoadPurchaseOrders(_purchaseOrderParameters.PageNumber);
        }

        private async Task LoadPurchaseOrders(int page)
        {
            _purchaseOrderParameters.PageNumber = page;
            var pagingResponse = await PurchaseOrderRepository.GetAllPurchaseOrders(_purchaseOrderParameters);
            _purchaseOrders = pagingResponse.Items ?? [];
            MetaData = pagingResponse.MetaData!;

            foreach (var po in _purchaseOrders)
            {
                Console.WriteLine($"PO Number: {po.SAPPONumber}, Total Value: {po.TotalValue}");
            }

        }

        public void Dispose() => Interceptor.DisposeEvent();
    }
}
