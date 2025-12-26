namespace OceanVMSClient.HttpRepoInterface.POModule
{
    public interface IInvoiceApproverRepository
    {
        Task<(bool IsAssigned, string AssignedType)> IsInvoiceApproverAsync(Guid invoiceId, Guid employeeId);
    }
}
