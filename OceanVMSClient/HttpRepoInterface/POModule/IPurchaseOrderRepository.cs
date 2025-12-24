using Entities.Models.POModule;
using OceanVMSClient.Features;
using Shared.DTO.POModule;

namespace OceanVMSClient.HttpRepoInterface.PoModule
{
    public interface IPurchaseOrderRepository
    {
        Task<PagingResponse<PurchaseOrderDto>> GetAllPurchaseOrders(PurchaseOrderParameters purchaseOrderParameters);
    }
}
