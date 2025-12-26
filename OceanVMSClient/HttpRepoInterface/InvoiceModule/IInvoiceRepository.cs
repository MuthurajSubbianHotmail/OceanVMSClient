using OceanVMSClient.Features;
using Shared.DTO.POModule;

namespace OceanVMSClient.HttpRepoInterface.InvoiceModule
{
    public interface IInvoiceRepository
    {
        Task<PagingResponse<InvoiceDto>> GetAllInvoices(InvoiceParameters invoiceParameters);
        Task<PagingResponse<InvoiceDto>> GetInvoicesByVendorId(Guid vendorContactId, InvoiceParameters invoiceParameters);
        Task<PagingResponse<InvoiceDto>> GetInvoicesByApproverEmployeeId(Guid employeeId, InvoiceParameters invoiceParameters);
        Task<InvoiceDto> GetInvoiceById(Guid invoiceId);
        Task<PagingResponse<InvoiceDto>> GetInvoicesByPurchaseOrderId(Guid purchaseOrderId, InvoiceParameters invoiceParameters);
        Task<InvoiceDto> CreateInvoice(InvoiceForCreationDto invoiceForCreation);

        // Uploads a file and returns the public URL (server should return the URL as plain string or JSON string)
        Task<string> UploadInvoiceFile(MultipartFormDataContent content);
    }
}
