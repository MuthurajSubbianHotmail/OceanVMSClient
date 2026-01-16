using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using OceanVMSClient.Helpers;
using OceanVMSClient.HttpRepoInterface.VendorRegistration;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace OceanVMSClient.Pages.RegisterVendors
{
    public partial class VendorProfile
    {
        [CascadingParameter] public Task<AuthenticationState> AuthState { get; set; } = default!;
        [Inject] public ILocalStorageService LocalStorage { get; set; } = default!;
        [Inject] private IVendorRegistrationRepository VendorRegistrationRepository { get; set; } = default!;
        [Inject] public NavigationManager NavigationManager { get; set; } = default!;
        [Inject] private ILogger<VendorProfile> Logger { get; set; } = default!;

        protected override async Task OnInitializedAsync()
        {
            await base.OnInitializedAsync();

            try
            {
                var authState = await AuthState;
                var user = authState?.User;

                if (user?.Identity?.IsAuthenticated != true)
                {
                    NavigationManager.NavigateTo("/");
                    return;
                }

                // Use project ClaimsHelper to read claims + localStorage fallback
                var ctx = await user.LoadUserContextAsync(LocalStorage);
                var vendorId = ctx.VendorId;

                if (vendorId.HasValue && vendorId.Value != Guid.Empty)
                {
                    // Get the vendor registration id for this vendor and navigate if found
                    var vendorRegistrationId = await VendorRegistrationRepository.GetVendorRegistrationByVendorIdAsync(vendorId.Value);
                    if (vendorRegistrationId != Guid.Empty)
                    {
                        NavigationManager.NavigateTo($"/register-vendor/{vendorRegistrationId}");
                        return;
                    }
                    else
                    {
                        Logger.LogInformation("No VendorRegistration found for VendorPK {VendorPK}", vendorId);
                    }
                }
                else
                {
                    Logger.LogWarning("VendorPK not available in claims/local storage for current user.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error while resolving VendorRegistrationId and navigating to register-vendor page.");
            }
        }

        private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var g) ? g : (Guid?)null;
    }
}