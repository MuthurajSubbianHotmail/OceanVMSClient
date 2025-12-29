using Entities.Models.Setup;

namespace OceanVMSClient.HttpRepoInterface.VendorRegistration
{
    public interface IVendorServiceRepository
    {
        Task<List<VendorService>> GetAllVendorServices();
    }
}
