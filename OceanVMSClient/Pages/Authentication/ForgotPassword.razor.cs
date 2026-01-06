using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using MudBlazor;
using OceanVMSClient.HttpRepoInterface.Authentication;

namespace OceanVMSClient.Pages.Authentication
{
    public partial class ForgotPassword
    {
        private ForgotModel _model = new();
        private bool isSubmitting;
        private string? StatusMessage;
        private bool IsSuccess;

        [Inject] private IAuthenticationService AuthService { get; set; } = default!;
        [Inject] private ISnackbar Snackbar { get; set; } = default!;
        [Inject] private NavigationManager NavigationManager { get; set; } = default!;

        private async Task HandleSubmit()
        {
            if (string.IsNullOrWhiteSpace(_model.UserName))
            {
                StatusMessage = "Please enter your user id.";
                IsSuccess = false;
                return;
            }

            isSubmitting = true;
            StatusMessage = null;
            IsSuccess = false;

            try
            {
                // Send request to API (method throws on failure)
                await AuthService.SendPasswordResetTokenAsync(_model.UserName.Trim());

                // Mask the provided user id/email: first 3 chars of local-part and the domain if present
                var provided = _model.UserName.Trim();
                var masked = MaskEmailLike(provided);

                StatusMessage = $"A Password Reset Link has been sent to your registered email {masked} — please click the link to reset your password.";
                IsSuccess = true;
                Snackbar.Add(StatusMessage, Severity.Success);
            }
            catch (Exception ex)
            {
                StatusMessage = string.IsNullOrWhiteSpace(ex.Message)
                    ? "Unable to send reset link. Please try again."
                    : ex.Message;
                IsSuccess = false;
                Snackbar.Add(StatusMessage, Severity.Error);
            }
            finally
            {
                isSubmitting = false;
                StateHasChanged();
            }
        }

        private static string MaskEmailLike(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            var s = input.Trim();

            // If input looks like an email address, split local-part and domain
            var atIndex = s.IndexOf('@');
            if (atIndex > 0 && atIndex < s.Length - 1)
            {
                var local = s.Substring(0, atIndex);
                var domain = s.Substring(atIndex + 1);

                var first3 = local.Length >= 3 ? local.Substring(0, 3) : local;
                // show first3 then obfuscation then @domain
                return $"{first3}***@{domain}";
            }

            // Not an email-like string: just show first 3 chars (if available) and mask rest
            var visible = s.Length >= 3 ? s.Substring(0, 3) : s;
            return s.Length > 3 ? $"{visible}***" : visible;
        }

        private class ForgotModel
        {
            [Required(ErrorMessage = "User id is required")]
            public string? UserName { get; set; }
        }
    }
}
