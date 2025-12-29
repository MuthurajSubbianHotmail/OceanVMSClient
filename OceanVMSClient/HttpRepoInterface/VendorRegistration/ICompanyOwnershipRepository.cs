using Entities.Models.Setup;

namespace OceanVMSClient.HttpRepoInterface.VendorRegistration
{
    public interface ICompanyOwnershipRepository
    {
        Task<List<CompanyOwnership>> GetAllCompanyOwnershipsAsync();
    }
}
