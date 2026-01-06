using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using MudBlazor;
using OceanVMSClient.HttpRepoInterface.Authentication;
using Shared.DTO;

namespace OceanVMSClient.Pages.Authentication
{
    public partial class ChangePassword
    {
        private ChangeModel _model = new();
        private EditContext? _editContext;
        private bool isSubmitting;
        private string? StatusMessage;
        private bool IsSuccess;

        // visibility toggles for MudTextField input types
        private bool showCurrent;
        private bool showNew;

        [Inject] private IAuthenticationService AuthService { get; set; } = default!;
        [Inject] private NavigationManager NavigationManager { get; set; } = default!;
        [Inject] private ISnackbar Snackbar { get; set; } = default!;

        protected override void OnInitialized()
        {
            _editContext = new EditContext(_model);
        }

        private async Task HandleSubmit()
        {
            // Basic client-side validation
            if (_editContext is not null && !_editContext.Validate())
            {
                StatusMessage = "Please fix validation errors and try again.";
                IsSuccess = false;
                return;
            }

            isSubmitting = true;
            StatusMessage = null;
            IsSuccess = false;

            try
            {
                // Map client model to DTO expected by the API
                var dto = new ChangePasswordDto(
                    _model.CurrentPassword ?? string.Empty,
                    _model.NewPassword ?? string.Empty,
                    _model.ConfirmPassword ?? string.Empty
                );

                await AuthService.ChangePasswordAsync(dto);

                IsSuccess = true;
                StatusMessage = "Your password has been changed successfully.";
                Snackbar.Add(StatusMessage, Severity.Success);

                await Task.Delay(1000);
                NavigationManager.NavigateTo("/");
            }
            catch (Exception ex)
            {
                StatusMessage = string.IsNullOrWhiteSpace(ex.Message)
                    ? "Unable to change password. Please try again."
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

        private void ToggleShowCurrent() => showCurrent = !showCurrent;
        private void ToggleShowNew() => showNew = !showNew;

        private class ChangeModel
        {
            [Required(ErrorMessage = "Current password is required")]
            public string? CurrentPassword { get; set; }

            [Required(ErrorMessage = "New password is required")]
            [MinLength(8, ErrorMessage = "Password must be at least 8 characters.")]
            public string? NewPassword { get; set; }

            [Required(ErrorMessage = "Please confirm the new password")]
            [Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match.")]
            public string? ConfirmPassword { get; set; }
        }
    }
}