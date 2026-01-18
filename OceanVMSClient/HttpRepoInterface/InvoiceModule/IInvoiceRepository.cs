using OceanVMSClient.Features;
using Shared.DTO.POModule;
using static Shared.DTO.POModule.InvAPApproverReviewCompleteDto;

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
        Task<string> UploadInvoiceAttachmentDocImage(string DocType, MultipartFormDataContent content);


        Task<InvoiceDto> UpdateInvoiceInitiatorReview(InvInitiatorReviewCompleteDto invInitiatorReviewCompleteDto);

        Task<InvoiceDto> UpdateInvoiceCheckerReview(InvCheckerReviewCompleteDto invCheckerReviewCompleteDto);

        Task<InvoiceDto> UpdateInvoiceValidatorApproval(InvValidatorReviewCompleteDto invValidatorReviewCompleteDto);

        Task<InvoiceDto> UpdateInvoiceApproverApproval(InvApproverReviewCompleteDto invApproverReviewCompleteDto);
        Task<InvoiceDto> UpdateInvoiceAPReview(InvAPApproverReviewCompleteDto invAPReviewCompleteDto);
        Task<string?> UploadInvoiceFile(MultipartFormDataContent content);
        Task<PagingResponse<InvoiceDto>> GetInvoicesWithAPReviewNotNAAsync(InvoiceParameters invoiceParameters);

        Task<InvoiceStatusCountsDto> GetInvoiceStatusCountsByVendorAsync(Guid vendorId);
        Task<InvoiceStatusCountsDto> GetInvoiceStatusCountsForApproverAsync(Guid employeeId);

        Task<InvoiceStatusSlaCountsDto> GetInvoiceStatusSlaCountsForApproverAsync(Guid employeeId);
    }
}
