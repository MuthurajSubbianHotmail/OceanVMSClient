using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using OceanVMSClient.Helpers;
using OceanVMSClient.HttpRepoInterface.VendorRegistration;
using System.Runtime.CompilerServices;

namespace OceanVMSClient.Pages.RegisterVendors
{
    public partial class VendorProfile
    {
        [CascadingParameter] public Task<AuthenticationState> AuthState { get; set; } = default!;
        [Inject] public ILocalStorageService LocalStorage { get; set; } = default!;
        [Inject] private IVendorRegistrationRepository VendorRegistrationRepository { get; set; }
        [Inject] public NavigationManager NavigationManager { get; set; }

        // Exposed vendor id for the component to consume
        public Guid? LoggedInVendorId { get; private set; }
        private Guid? VendorRegistrationID { get; set; }
        protected override async Task OnInitializedAsync()
        {
            await base.OnInitializedAsync();
            await LoadLoggedInVendorIdAsync();
            if (LoggedInVendorId.HasValue)
            {
                VendorRegistrationID = await VendorRegistrationRepository.GetVendorRegistrationByVendorIdAsync(LoggedInVendorId.Value);
                NavigationManager.NavigateTo($"/register-vendor/{VendorRegistrationID}");
            }
        }

        /// <summary>
        /// Loads the vendor id for the currently authenticated user.
        /// It tries claims first ("vendorPK", "vendorId", "VendorId") then falls back to local storage key "vendorPK".
        /// Returns the discovered Guid or null.
        /// </summary>
        public async Task<Guid?> LoadLoggedInVendorIdAsync()
        {
            try
            {
                var authState = await AuthState;
                var user = authState?.User;

                if (user?.Identity?.IsAuthenticated == true)
                {
                    // Use the ClaimsHelper extension to look up claim values using multiple matching strategies
                    var value = user.GetClaimValue("vendorPK")
                                ?? user.GetClaimValue("vendorId")
                                ?? user.GetClaimValue("VendorId");

                    var parsed = ParseGuid(value);
                    if (parsed.HasValue)
                    {
                        LoggedInVendorId = parsed.Value;
                        return LoggedInVendorId;
                    }
                }

                // Fallback to local storage (string key commonly used in this project)
                try
                {
                    var stored = await LocalStorage.GetItemAsync<string>("vendorPK");
                    var parsedStored = ParseGuid(stored);
                    if (parsedStored.HasValue)
                    {
                        LoggedInVendorId = parsedStored.Value;
                        return LoggedInVendorId;
                    }
                }
                catch
                {
                    // ignore local storage errors and return null
                }

                LoggedInVendorId = null;
                return null;
            }
            catch
            {
                LoggedInVendorId = null;
                return null;
            }
        }

        private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var g) ? g : (Guid?)null;
    }
}
