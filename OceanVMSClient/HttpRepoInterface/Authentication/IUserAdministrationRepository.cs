namespace OceanVMSClient.HttpRepoInterface.Authentication
{
    public interface IUserAdministrationRepository
    {
        /// <summary>
        /// Set user lock state on the server (true = locked, false = unlocked).
        /// Server should enforce lock at authentication & revoke refresh tokens.
        /// </summary>
        Task<bool> LockUserAsync(string userName, string? reason = null);
        Task<bool> UnlockUserAsync(string userName);
        Task<bool> SetUserLockStateAsync(string userName, bool isLocked, string? reason = null);
    }
}
