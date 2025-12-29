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
        Task<VendorRegistrationFormDto> CreateNewVendorRegistration(VendorRegistrationForm vendorRegistrationform);
        Task<PagingResponse<VendorRegistrationFormDto>> GetAllVendorRegistration(VendorRegistrationFormParameters vendorRegistrationFormParameters);
        Task<VendRegService> GetVendorRegServiceByIdAsync(Guid id);
        Task<IEnumerable<VendRegService>> GetAllVendRegServicesForARegAsync(Guid vendorRegistrationId);
        Task<string> UploadOrgRegDocImage(string DocType, MultipartFormDataContent content);
        Task<VendorRegistrationForm> GetVendorRegistrationFormByIdAsync(Guid id);
    }
}
