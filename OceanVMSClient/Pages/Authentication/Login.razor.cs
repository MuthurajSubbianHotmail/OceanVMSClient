using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using OceanVMSClient.HttpRepoInterface.Authentication;
using Shared.DTO;
using Microsoft.AspNetCore.WebUtilities;
using MudBlazor;

namespace OceanVMSClient.Pages.Authentication
{
    public partial class Login
    {
        private UserForAuthenticationDto _userForAuthentication = new UserForAuthenticationDto();
        [Inject]
        public IAuthenticationService AuthenticationService { get; set; }
        [Inject]
        public NavigationManager NavigationManager { get; set; }
        [Inject]
        public ISnackbar Snackbar { get; set; }
        public bool ShowAuthError { get; set; }
        public string ErrorMessage { get; set; }

        protected override void OnInitialized()
        {
            // Show a friendly message if redirected here after password change
            var uri = new Uri(NavigationManager.Uri);
            if (!string.IsNullOrEmpty(uri.Query))
            {
                var qs = QueryHelpers.ParseQuery(uri.Query);
                if (qs.TryGetValue("passwordChanged", out var val) && val == "true")
                {
                    Snackbar.Add("Your password was changed. Please sign in using your new password.", Severity.Info);
                    // remove query from history (replace) so refresh doesn't re-show the message
                    NavigationManager.NavigateTo("/login", forceLoad: false, replace: true);
                }
            }
        }

        // EditForm OnValidSubmit provides an EditContext — accept it to match the delegate.
        private async Task HandleSubmit(EditContext _)
        {
            isSubmitting = true;
            try
            {
                await ExecuteLogin();
            }
            finally
            {
                isSubmitting = false;
            }
        }

        public async Task ExecuteLogin()
        {
            ShowAuthError = false;
            var result = await AuthenticationService.Login(_userForAuthentication);
            if (!result.IsAuthSuccessful) 
            {
                ErrorMessage = result.ErrorMessage;
                ShowAuthError = true;
            }
            else
            {
                NavigationManager.NavigateTo("/");
            }
        }

        private bool showPassword = false;
        private bool isSubmitting = false;
        private bool rememberMe = false;
        private string PasswordInputType => showPassword ? "text" : "password";

        // Add the missing toggle method referenced from the Razor markup
        private void ToggleShowPassword()
        {
            showPassword = !showPassword;
        }
    }
}
