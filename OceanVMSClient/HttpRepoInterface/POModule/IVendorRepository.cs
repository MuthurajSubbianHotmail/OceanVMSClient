using OceanVMSClient.Features;
using Shared.DTO.POModule;

namespace OceanVMSClient.HttpRepoInterface.POModule
{
    public interface IVendorRepository
    {
        Task<PagingResponse<VendorDto>> GetAllVendors(VendorParameters vendorParameters);
        Task<VendorDto> GetVendorById(Guid vendorContactId);
        Task<VendorDto> GetVendorByName(string vendorName);
    }
}
