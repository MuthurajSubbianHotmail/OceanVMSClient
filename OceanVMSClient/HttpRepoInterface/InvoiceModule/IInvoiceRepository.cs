using OceanVMSClient.Features;
using Shared.DTO.POModule;

namespace OceanVMSClient.HttpRepoInterface.InvoiceModule
{
    public interface IInvoiceRepository
    {
        Task<PagingResponse<InvoiceDto>> GetAllInvoices(InvoiceParameters invoiceParameters);
    }
}
