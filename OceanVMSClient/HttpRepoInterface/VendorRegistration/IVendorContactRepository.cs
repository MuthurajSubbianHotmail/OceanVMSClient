namespace OceanVMSClient.HttpRepoInterface.VendorRegistration
{
    public interface IVendorContactRepository
    {
        Task<bool> ResponderEmailExistsAsync(string email, Guid? excludeId = null);
    }
}
