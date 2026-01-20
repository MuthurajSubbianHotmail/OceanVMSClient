using Microsoft.AspNetCore.Components;
using OceanVMSClient.HttpRepoInterface.Authentication;

namespace OceanVMSClient.Pages.Authentication
{
    public partial class Logout
    {
        [Inject]
        public IAuthenticationService AuthenticationService { get; set; }
        [Inject]
        public NavigationManager NavigationManager { get; set; }
        protected override async Task OnInitializedAsync()
        {
            await AuthenticationService.Logout();
            NavigationManager.NavigateTo("/login", forceLoad: true);
            //NavigationManager.NavigateTo("/");
        }
    }
}
