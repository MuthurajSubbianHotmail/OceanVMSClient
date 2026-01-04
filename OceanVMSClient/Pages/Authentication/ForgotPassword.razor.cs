using System;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using OceanVMSClient.HttpRepoInterface.Authentication;

namespace OceanVMSClient.Pages.Authentication
{
    public partial class ForgotPassword
    {
        private ForgotModel _model = new();
        private bool isSubmitting;
        private string? StatusMessage;
        private bool IsSuccess;

        [Inject] private NavigationManager NavigationManager { get; set; } = default!;
        // Inject the interface registered in DI instead of the concrete type
        [Inject] private IAuthenticationService AuthService { get; set; } = default!;

        private async Task HandleSubmit()
        {
            isSubmitting = true;
            StatusMessage = null;
            IsSuccess = false;

            try
            {
                // Call the service with the user id / username
                await AuthService.SendPasswordResetTokenAsync(_model.UserName ?? string.Empty);

                IsSuccess = true;
                StatusMessage = "If that user id is registered, password reset instructions have been sent.";
                // optional: redirect to login after short delay
                await Task.Delay(1800);
                NavigationManager.NavigateTo("/login");
            }
            catch (Exception ex)
            {
                // Show API message when available, otherwise friendly fallback
                StatusMessage = string.IsNullOrWhiteSpace(ex.Message)
                    ? "Unable to request password reset. Please try again."
                    : ex.Message;
            }
            finally
            {
                isSubmitting = false;
            }
        }

        private class ForgotModel
        {
            [Required(ErrorMessage = "User ID is required")]
            public string? UserName { get; set; }
        }
    }
}
