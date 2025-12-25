using Microsoft.AspNetCore.Components;
using OceanVMSClient.HttpRepoInterface.PoModule;
using Shared.DTO.POModule;

namespace OceanVMSClient.Pages.POModule
{
    public partial class PurchaseOrderView
    {
        private bool isDetailsLoading = true;
        public PurchaseOrderDto PurchaseOrderDetails { get; set; } = new PurchaseOrderDto();
        [Inject]
        public IPurchaseOrderRepository purchaseOrderRepository { get; set; }
        [Parameter]
        public Guid PurchaseOrderId { get; set; }
        protected override async Task OnInitializedAsync()
        {
            isDetailsLoading = true;
            await LoadPurchaseOrderDetails();
            isDetailsLoading = false;
        }

        private async Task LoadPurchaseOrderDetails()
        {
            PurchaseOrderDetails = await purchaseOrderRepository.GetPurchaseOrderById(PurchaseOrderId);
        }
    }
}
