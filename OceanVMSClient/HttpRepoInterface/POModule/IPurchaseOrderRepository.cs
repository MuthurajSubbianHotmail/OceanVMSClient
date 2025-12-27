using Entities.Models.POModule;
using OceanVMSClient.Features;
using Shared.DTO.POModule;

namespace OceanVMSClient.HttpRepoInterface.PoModule
{
    public interface IPurchaseOrderRepository
    {
        Task<PagingResponse<PurchaseOrderDto>> GetAllPurchaseOrders(PurchaseOrderParameters purchaseOrderParameters);
        Task<PurchaseOrderDto> GetPurchaseOrderById(Guid PurchaseOrderId);
        Task<PagingResponse<PurchaseOrderDto>> GetAllPurchaseOrdersOfVendorAsync(Guid vendorId, PurchaseOrderParameters purchaseOrderParameters);
        Task<PagingResponse<PurchaseOrderDto>> GetAllPurchaseOrdersOfApproversAsync(Guid employeeId, PurchaseOrderParameters purchaseOrderParameters);
        Task<bool> IsEmployeeAssignedforRoleAsync(Guid purchaseOrderId, Guid employeeId, string roleName);
    }
}
