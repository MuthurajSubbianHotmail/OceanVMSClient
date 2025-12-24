using Microsoft.AspNetCore.Components;
using OceanVMSClient.HttpRepoInterface.Authentication;
using Shared.DTO;

namespace OceanVMSClient.Pages.Authentication
{
    public partial class Registration
    {
        private UserForRegistrationDto _userForRegistration = new UserForRegistrationDto();
        [Inject]
        public IAuthenticationService _authenticationService { get; set; }
        [Inject]
        public NavigationManager _navigationManager { get; set; }
        public bool ShowRegistrationErrors { get; set; } = false;
        public IEnumerable<string> Errors { get; set; } = Enumerable.Empty<string>();
        private async Task RegisterUser()
        {
            ShowRegistrationErrors = false;
            var result = await _authenticationService.RegisterUser(_userForRegistration);
            if (result.IsSuccessfulRegistration)
            {
                _navigationManager.NavigateTo("/login");
            }
            else
            {
                Errors = result.Errors;
                ShowRegistrationErrors = true;
            }
        }
    }
}
