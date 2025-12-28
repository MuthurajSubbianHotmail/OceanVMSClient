using OceanVMSClient.Features;
using Shared.DTO.POModule;

namespace OceanVMSClient.HttpRepoInterface.POModule
{
    public interface IInvoiceApproverRepository
    {
        Task<(bool IsAssigned, string AssignedType)> IsInvoiceApproverAsync(Guid invoiceId, Guid employeeId);
        Task<PagingResponse<InvoiceApproverDTO>> GetInvoiceApproverByProjectIdAndType(Guid projectID, string assignedType);
    }
}
