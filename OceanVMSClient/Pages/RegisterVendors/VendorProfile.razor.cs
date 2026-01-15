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

        // Exposed vendor id for the component to consume
        public Guid? LoggedInVendorId { get; set; }
        private Guid? VendorRegistrationID { get; set; }

        // ensure we only run the navigation logic once
        private bool _profileInitialized;

        // Use OnAfterRenderAsync(firstRender) to ensure cascading AuthState is available
        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!firstRender || _profileInitialized)
                return;

            _profileInitialized = true;
            Logger.LogDebug("VendorProfile.OnAfterRenderAsync (firstRender) start");

            try
            {
                // Get the cascading authentication state (may be null if not provided)
                var authState = AuthState != null ? await AuthState : null;
                var user = authState?.User;

                Logger.LogDebug("AuthState user present: {HasUser} IsAuthenticated: {IsAuthenticated}",
                    user != null, user?.Identity?.IsAuthenticated ?? false);

                // Debug: enumerate available claims (helps diagnose why IsAuthenticated is false)
                if (user != null)
                {
                    foreach (var c in user.Claims)
                        Logger.LogDebug("Claim: {Type} = {Value}", c.Type, c.Value);
                }

                Guid? vendorId = null;

                // If the ClaimsPrincipal reports authenticated, prefer the claims-based lookup via your helper
                if (user?.Identity?.IsAuthenticated == true)
                {
                    var ctx = await user.LoadUserContextAsync(LocalStorage);
                    vendorId = ctx.VendorId;
                    Logger.LogDebug("Loaded user context from claims: {@ctx}", ctx);
                }

                // Fallback: try direct claim lookups even if IsAuthenticated is false (sometimes claims exist)
                if (!vendorId.HasValue && user != null)
                {
                    var claimValue = user.GetClaimValue("vendorPK") ?? user.GetClaimValue("vendorId") ?? user.GetClaimValue("VendorId");
                    vendorId = ParseGuid(claimValue);
                    Logger.LogDebug("Direct claim lookup returned vendorPK: {ClaimValue} => {VendorId}", claimValue, vendorId);
                }

                // Final fallback: local storage
                if (!vendorId.HasValue)
                {
                    try
                    {
                        var stored = await LocalStorage.GetItemAsync<string>("vendorPK");
                        vendorId = ParseGuid(stored);
                        Logger.LogDebug("LocalStorage vendorPK: {Stored} => {VendorId}", stored, vendorId);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Failed to read vendorPK from local storage");
                    }
                }

                LoggedInVendorId = vendorId;
                Logger.LogInformation("Resolved LoggedInVendorId: {VendorId}", LoggedInVendorId);

                if (!LoggedInVendorId.HasValue)
                {
                    NavigationManager.NavigateTo($"/register-vendor/{Guid.Empty}");
                    return;
                }

                // resolve registration id and navigate
                var regId = await VendorRegistrationRepository.GetVendorRegistrationByVendorIdAsync(LoggedInVendorId.Value);
                if (regId == Guid.Empty)
                    NavigationManager.NavigateTo($"/register-vendor/{Guid.Empty}");
                else
                    NavigationManager.NavigateTo($"/register-vendor/{regId}");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unhandled exception in OnAfterRenderAsync");
                NavigationManager.NavigateTo($"/register-vendor/{Guid.Empty}");
            }
            finally
            {
                Logger.LogDebug("VendorProfile.OnAfterRenderAsync end");
            }
        }

        /// <summary>
        /// Loads the vendor id for the currently authenticated user.
        /// It tries claims first ("vendorPK", "vendorId", "VendorId") then falls back to local storage key "vendorPK".
        /// Returns the discovered Guid or null.
        /// </summary>
        public async Task<Guid?> LoadLoggedInVendorIdAsync()
        {
            Logger.LogDebug("LoadLoggedInVendorIdAsync start");
            try
            {
                var authState = AuthState;
                if (authState == null)
                {
                    Logger.LogWarning("AuthState cascading parameter is null");
                }

                var actualAuthState = authState != null ? await authState : null;
                var user = actualAuthState?.User;

                if (user?.Identity?.IsAuthenticated == true)
                {
                    // Use the ClaimsHelper extension to look up claim values using multiple matching strategies
                    var value = user.GetClaimValue("vendorPK")
                                ?? user.GetClaimValue("vendorId")
                                ?? user.GetClaimValue("VendorId");

                    Logger.LogDebug("Claim lookup returned value: {ClaimValue}", value);

                    var parsed = ParseGuid(value);
                    if (parsed.HasValue)
                    {
                        LoggedInVendorId = parsed.Value;
                        Logger.LogInformation("Loaded vendor id from claims: {VendorId}", LoggedInVendorId);
                        return LoggedInVendorId;
                    }
                }

                // Fallback to local storage (string key commonly used in this project)
                try
                {
                    var stored = await LocalStorage.GetItemAsync<string>("vendorPK");
                    Logger.LogDebug("LocalStorage vendorPK value: {Stored}", stored);
                    var parsedStored = ParseGuid(stored);
                    if (parsedStored.HasValue)
                    {
                        LoggedInVendorId = parsedStored.Value;
                        Logger.LogInformation("Loaded vendor id from local storage: {VendorId}", LoggedInVendorId);
                        return LoggedInVendorId;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Error reading vendorPK from local storage");
                    // ignore local storage errors and return null
                }

                LoggedInVendorId = null;
                return null;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Exception in LoadLoggedInVendorIdAsync");
                LoggedInVendorId = null;
                return null;
            }
            finally
            {
                Logger.LogDebug("LoadLoggedInVendorIdAsync end");
            }
        }

        private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var g) ? g : (Guid?)null;
    }
}