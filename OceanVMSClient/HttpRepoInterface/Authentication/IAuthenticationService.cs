using OceanVMSClient.Pages.Authentication;
using Shared.DTO;

namespace OceanVMSClient.HttpRepoInterface.Authentication
{
    public interface IAuthenticationService
    {
        Task<RegistrationResponseDto> RegisterUser(UserForRegistrationDto userForRegistration);
        Task<AuthenticationResponseDto> Login(UserForAuthenticationDto userForAuthentication);
        Task Logout();
        Task<string> RefreshToken();
        /// <summary>
        /// Requests the server to create/send a password reset token for <paramref name="userName"/>.
        /// Returns true when the request completed successfully (2xx). Throws on non-success HTTP response.
        /// </summary>
        Task<bool> SendPasswordResetTokenAsync(string userName);
        /// <summary>
        /// Submits a new password together with the reset token for the given user.
        /// Returns true when reset completed successfully (2xx). Throws/returns false on error.
        /// </summary>
        Task<bool> ResetPasswordAsync(string userName, string token, string newPassword);
        Task<bool> ChangePasswordAsync(ChangePasswordDto changePasswordModel);

    }
}
