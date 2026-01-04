using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;
using OceanVMSClient.HttpRepoInterface.Authentication;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components.Forms;
using System.Linq;
using System.Threading.Tasks;

namespace OceanVMSClient.Pages.Authentication
{
    public partial class ResetPassword
    {
        private ResetModel _model = new();
        private EditContext? _editContext;
        private bool isSubmitting;
        private string? StatusMessage;
        private bool IsSuccess;

        [Inject] private NavigationManager NavigationManager { get; set; } = default!;
        [Inject] private IAuthenticationService AuthService { get; set; } = default!;

        private string? UserName { get; set; }
        private string? Token { get; set; }

        protected override void OnInitialized()
        {
            _editContext = new EditContext(_model);

            // Parse query string for userName and token
            var uri = NavigationManager.ToAbsoluteUri(NavigationManager.Uri);
            if (!string.IsNullOrEmpty(uri.Query))
            {
                var qs = QueryHelpers.ParseQuery(uri.Query);
                if (qs.TryGetValue("userName", out var u))
                    UserName = u.ToString();
                if (qs.TryGetValue("token", out var t))
                    Token = t.ToString();
            }
        }

        // Called from MudButton.OnClick — guarantees a handler runs on click.
        public async Task SubmitClicked()
        {
            // Immediate feedback so user sees something happened.
            StatusMessage = "Submitting...";
            StateHasChanged();

            // Validate client-side before calling API
            if (_editContext is not null && !_editContext.Validate())
            {
                HandleInvalidSubmit(_editContext);
                return;
            }

            await HandleSubmit();
        }

        private async Task HandleSubmit()
        {
            // Defensive validation if someone calls directly
            if (_editContext is not null && !_editContext.Validate())
            {
                HandleInvalidSubmit(_editContext);
                return;
            }

            isSubmitting = true;
            StatusMessage = null;
            IsSuccess = false;
            StateHasChanged();

            try
            {
                if (string.IsNullOrWhiteSpace(UserName) || string.IsNullOrWhiteSpace(Token))
                    throw new InvalidOperationException("Missing reset token or user identification. Make sure you used the link from the email.");

                await AuthService.ResetPasswordAsync(UserName, Token, _model.Password ?? string.Empty);

                // Do not auto-navigate — show success and wait for user's action
                IsSuccess = true;
                StatusMessage = "Your password has been reset successfully. Click 'Back to sign in' when you're ready.";
            }
            catch (Exception ex)
            {
                StatusMessage = string.IsNullOrWhiteSpace(ex.Message)
                    ? "Unable to reset password. Please try again or request a new reset link."
                    : ex.Message;
                IsSuccess = false;
            }
            finally
            {
                isSubmitting = false;
                StateHasChanged();
            }
        }

        private void HandleInvalidSubmit(EditContext editContext)
        {
            isSubmitting = false;
            IsSuccess = false;

            var first = editContext.GetValidationMessages().FirstOrDefault();
            StatusMessage = first ?? "Please correct the highlighted errors and try again.";
            StateHasChanged();
        }

        // Called by the "Back to sign in" button after successful reset.
        private void NavigateToLogin()
        {
            NavigationManager.NavigateTo("/login");
        }

        private class ResetModel
        {
            [Required]
            [MinLength(8, ErrorMessage = "Password must be at least 8 characters.")]
            public string? Password { get; set; }

            [Required]
            [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
            public string? ConfirmPassword { get; set; }
        }

        // Temporary debug helper can remain if you still need it.
        public async Task DebugSubmit()
        {
            StatusMessage = "Debug: clicked — validating...";
            StateHasChanged();

            if (_editContext is not null)
            {
                if (!_editContext.Validate())
                {
                    var msgs = string.Join(" | ", _editContext.GetValidationMessages());
                    StatusMessage = "Validation failed: " + (string.IsNullOrWhiteSpace(msgs) ? "unknown" : msgs);
                    IsSuccess = false;
                    StateHasChanged();
                    return;
                }
            }

            StatusMessage = "Validation passed — calling submit...";
            StateHasChanged();

            await HandleSubmit();
        }
    }
}
