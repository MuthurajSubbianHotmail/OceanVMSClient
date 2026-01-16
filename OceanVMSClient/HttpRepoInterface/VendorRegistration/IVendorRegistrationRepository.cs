using Entities.Models.VendorReg;
using OceanVMSClient.Features;
using Shared.DTO.VendorReg;

namespace OceanVMSClient.HttpRepoInterface.VendorRegistration
{
    public interface IVendorRegistrationRepository
    {
        Task CreateVendorRegServiceAsync(VendRegService vendorRegService);
        Task DeleteVendorRegServiceAsync(VendRegService vendorRegService);
        Task UpdateVendorRegServiceAsync(VendRegService vendorRegService);
        Task<VendorRegistrationFormDto> CreateNewVendorRegistration(VendorRegistrationFormCreateDto vendorRegistrationform);
        Task<VendorRegistrationFormDto> UpdateVendorRegistration(Guid vendorRegistrationFormId, VendorRegistrationFormUpdateDto vendorRegistrationFormUpdateDto);
        Task<PagingResponse<VendorRegistrationFormDto>> GetAllVendorRegistration(VendorRegistrationFormParameters vendorRegistrationFormParameters);
        Task<VendRegService> GetVendorRegServiceByIdAsync(Guid id);
        Task<IEnumerable<VendRegService>> GetAllVendRegServicesForARegAsync(Guid vendorRegistrationId);
        Task<string> UploadOrgRegDocImage(string DocType, MultipartFormDataContent content);
        Task<VendorRegistrationFormDto> GetVendorRegistrationFormByIdAsync(Guid id);
        Task<string> ApproveVendorRegistrationFormAsync(Guid vendorRegistrationFormId, VendorRegistrationFormApprovalDto approvalDto);
        Task<string> ReviewVendorRegistrationFormAsync(Guid vendorRegistrationFormId, VendorRegistrationFormReviewDto reviewDto);
        Task<bool> OrganizationNameExistsAsync(string organizationName);
        Task<Guid> GetVendorRegistrationByVendorIdAsync(Guid vendorId);

        Task<(bool Exists, string? VendorName)> SAPVendorCodeExistsAsync(string sapVendorCode, Guid? excludeId = null);
    }
}
