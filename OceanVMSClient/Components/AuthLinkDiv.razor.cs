using System;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace OceanVMSClient.Components
{
    public partial class AuthLinkDiv : IDisposable
    {
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
        [Inject] private NavigationManager NavigationManager { get; set; } = default!;
        [Inject] private AuthenticationStateProvider AuthStateProvider { get; set; } = default!;

        // values bound to the Razor markup
        private string? userTypeValue;
        private string? fullNameValue;
        private string? emailValue;
        private string? rolesValue;
        private string? vendorNameValue;
        private bool isPanelOpen;

        // element reference for the details panel and DotNet ref for callbacks
        private ElementReference panelRef;
        private DotNetObjectReference<AuthLinkDiv>? dotNetRef;

        // track registration state to avoid duplicate handlers
        private bool isOutsideClickRegistered;

        protected override async Task OnInitializedAsync()
        {
            // Initial populate (try localStorage first, then claims)
            await RefreshUserContextAsync();

            // Subscribe to auth state changes so we update when the user logs in for the first time
            AuthStateProvider.AuthenticationStateChanged += OnAuthenticationStateChanged;
        }

        private void OnAuthenticationStateChanged(Task<AuthenticationState> task)
        {
            // Use the discard operator to avoid assigning to the incoming parameter.
            // InvokeAsync returns Task — do not cast. This marshals work to the renderer.
            _ = InvokeAsync(async () =>
            {
                // Re-read localStorage / claims when auth state changes
                await RefreshUserContextAsync();
                StateHasChanged();
            });
        }

        // Toggle/close helpers used by the Razor markup
        private Task TogglePanel()
        {
            isPanelOpen = !isPanelOpen;
            // Let OnAfterRenderAsync handle registering/unregistering JS handlers after render.
            return InvokeAsync(StateHasChanged);
        }

        private async Task ClosePanel()
        {
            if (!isPanelOpen) return;
            isPanelOpen = false;
            await UnregisterOutsideClickAsync();
            await InvokeAsync(StateHasChanged);
        }

        private void NavigateToChangePassword()
        {
            isPanelOpen = false;
            NavigationManager.NavigateTo("ChangePassword");
        }
        private void NavigateToLogout()
        {
            isPanelOpen = false;
            NavigationManager.NavigateTo("Logout");
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            // If panel opened and not registered yet, register the outside-click handler.
            if (isPanelOpen && !isOutsideClickRegistered)
            {
                try
                {
                    dotNetRef ??= DotNetObjectReference.Create(this);
                    await RegisterOutsideClickAsync();
                    isOutsideClickRegistered = true;
                }
                catch
                {
                    // swallow
                }
            }
            // If panel closed but handler still registered, unregister it.
            else if (!isPanelOpen && isOutsideClickRegistered)
            {
                await UnregisterOutsideClickAsync();
            }

            await base.OnAfterRenderAsync(firstRender);
        }

        // JS interop registration
        private async Task RegisterOutsideClickAsync()
        {
            try
            {
                if (dotNetRef == null)
                    dotNetRef = DotNetObjectReference.Create(this);

                // pass the panel element and the dotnet reference
                await JSRuntime.InvokeVoidAsync("authLinkDiv.registerOutsideClick", panelRef, dotNetRef);
            }
            catch
            {
                // ignore JS errors
            }
        }

        private async Task UnregisterOutsideClickAsync()
        {
            try
            {
                if (isOutsideClickRegistered)
                {
                    // ask JS to remove listeners
                    await JSRuntime.InvokeVoidAsync("authLinkDiv.unregisterOutsideClick", panelRef);
                    isOutsideClickRegistered = false;
                }
            }
            catch
            {
                // ignore
            }
            // DO NOT dispose dotNetRef here — disposing can race with pending JS unregister and cause
            // "There is no tracked object with id" errors. We'll null the reference so it can be GC'd later.
            finally
            {
                // leave the DotNetObjectReference alive (avoid Dispose race)
                // but allow it to be re-created next time if needed
                dotNetRef = dotNetRef; // no-op to emphasize we intentionally do not dispose
            }
        }

        [JSInvokable]
        public async Task NotifyClickOutside()
        {
            // called from JS when a click/touch happens outside the panel
            isPanelOpen = false;
            await UnregisterOutsideClickAsync();
            await InvokeAsync(StateHasChanged);
        }

        private async Task RefreshUserContextAsync()
        {
            // Reset values before populating
            userTypeValue = null;
            fullNameValue = null;
            emailValue = null;
            rolesValue = null;
            vendorNameValue = null;

            // Try JS localStorage first (fast, usually set by login flow). Continue to claims if missing.
            try
            {
                var userTypeStored = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "userType");
                var fullNameStored = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "fullName");
                var emailStored = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "email");
                var rolesStored = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "userRoles");
                var vendorStored = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "vendorName");

                userTypeValue = !string.IsNullOrWhiteSpace(userTypeStored) ? FormatUserType(UnwrapQuotes(userTypeStored)) : null;
                fullNameValue = !string.IsNullOrWhiteSpace(fullNameStored) ? UnwrapQuotes(fullNameStored) : null;
                emailValue = !string.IsNullOrWhiteSpace(emailStored) ? UnwrapQuotes(emailStored) : null;

                // Filter out Employee and Vendor when reading from localStorage
                rolesValue = !string.IsNullOrWhiteSpace(rolesStored)
                    ? FilterRoles(NormalizeRolesString(UnwrapQuotes(rolesStored)))
                    : null;

                vendorNameValue = !string.IsNullOrWhiteSpace(vendorStored) ? UnwrapQuotes(vendorStored) : null;
            }
            catch
            {
                // ignore JS errors → fall back to claims
            }

            // If any piece is still missing, obtain from claims
            if (userTypeValue == null || fullNameValue == null || emailValue == null || rolesValue == null || vendorNameValue == null)
            {
                try
                {
                    var authState = await AuthStateProvider.GetAuthenticationStateAsync();
                    var user = authState.User;

                    if (user?.Identity?.IsAuthenticated == true)
                    {
                        if (userTypeValue == null)
                        {
                            var claim = GetClaimFromPrincipal(user, "userType") ?? GetClaimFromPrincipal(user, "UserType");
                            userTypeValue = FormatUserType(UnwrapQuotes(claim));
                        }

                        if (fullNameValue == null)
                        {
                            // try multiple claim types for full name
                            var fn = UnwrapQuotes(GetClaimFromPrincipal(user, "fullName"))
                                     ?? UnwrapQuotes(GetClaimFromPrincipal(user, "name"))
                                     ?? UnwrapQuotes(GetClaimFromPrincipal(user, ClaimTypes.Name));

                            if (string.IsNullOrWhiteSpace(fn))
                            {
                                // try to construct from given/surname
                                var given = UnwrapQuotes(GetClaimFromPrincipal(user, ClaimTypes.GivenName));
                                var sur = UnwrapQuotes(GetClaimFromPrincipal(user, ClaimTypes.Surname));
                                fn = $"{given} {sur}".Trim();
                            }

                            fullNameValue = string.IsNullOrWhiteSpace(fn) ? null : fn;
                            if (fullNameValue == null)
                                fullNameValue = user.Identity?.Name; // last resort (often shows email)
                        }

                        if (emailValue == null)
                            emailValue = UnwrapQuotes(GetClaimFromPrincipal(user, ClaimTypes.Email) ?? GetClaimFromPrincipal(user, "email"));

                        if (rolesValue == null)
                        {
                            var rawRoles = UnwrapQuotes(GetRolesFromPrincipal(user) ?? string.Empty);
                            rolesValue = FilterRoles(NormalizeRolesString(rawRoles));
                        }

                        if (vendorNameValue == null)
                            vendorNameValue = UnwrapQuotes(GetClaimFromPrincipal(user, "vendorName") ?? GetClaimFromPrincipal(user, "VendorName"));
                    }
                }
                catch
                {
                    // swallow — we will render what we have
                }
            }
        }

        // --- Helpers --- //

        private static string? GetClaimFromPrincipal(ClaimsPrincipal p, string claimType)
        {
            if (p == null) return null;
            var c = p.Claims.FirstOrDefault(x =>
                string.Equals(x.Type, claimType, StringComparison.OrdinalIgnoreCase)
                || x.Type.EndsWith($"/{claimType}", StringComparison.OrdinalIgnoreCase)
                || x.Type.EndsWith(claimType, StringComparison.OrdinalIgnoreCase));
            return c?.Value;
        }

        private static string? FilterRoles(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var parts = raw.Split(',')
                           .Select(p => p.Trim().Trim('"', '\''))
                           .Where(p => !string.IsNullOrEmpty(p))
                           .Select(p => new { Orig = p, Key = p.ToUpperInvariant() })
                           // filter out anything that looks like employee or vendor (tolerant to misspellings)
                           .Where(x => !(x.Key.Contains("EMPLOY") || x.Key.Contains("VENDOR")))
                           .Select(x => x.Orig)
                           .Distinct(StringComparer.OrdinalIgnoreCase)
                           .ToArray();

            return parts.Length == 0 ? null : string.Join(", ", parts);
        }

        private static string NormalizeRolesString(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;
            var s = raw.Trim();

            // Try to deserialize JSON array first (e.g. ["QS Head","EMPLOYEE"])
            if ((s.StartsWith("[") && s.EndsWith("]")) || (s.StartsWith("\"[") && s.EndsWith("]\"")))
            {
                try
                {
                    var arr = JsonSerializer.Deserialize<string[]>(s);
                    if (arr != null && arr.Length > 0)
                        return string.Join(", ", arr.Select(r => (r ?? string.Empty).Trim().Trim('"', '\'')).Where(r => !string.IsNullOrEmpty(r)));
                }
                catch
                {
                    // fall back to trimming characters below
                    s = s.Trim('[', ']', '"', '\'');
                }
            }

            // Trim surrounding quotes/brackets and return a comma-separated string
            s = s.Trim('[', ']', '"', '\'').Trim();
            return s;
        }

        private static string FormatUserType(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            try
            {
                var cleaned = raw.Trim();
                var lower = cleaned.ToLower(CultureInfo.CurrentCulture);
                return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(lower);
            }
            catch
            {
                return raw ?? string.Empty;
            }
        }

        private string FormatFullName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            // Example: trim and capitalize first letter of each word
            return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name.Trim());
        }

        private string FormatLabel(string label)
        {
            // Example: just return the label as-is, or add formatting as needed
            return label;
        }

        private string FormatStringType(string? value)
        {
            // Example: return the value as-is, or add formatting as needed
            return value ?? string.Empty;
        }

        // Add this helper method near the other helpers
        private static string? UnwrapQuotes(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return s;
            var t = s.Trim();
            if ((t.Length >= 2) && ((t.StartsWith("\"") && t.EndsWith("\"")) || (t.StartsWith("'") && t.EndsWith("'"))))
                return t.Substring(1, t.Length - 2).Trim();
            return t;
        }

        // Add this helper method near the other helpers
        private static string? GetRolesFromPrincipal(ClaimsPrincipal p)
        {
            if (p == null) return null;
            // Try to get all role claims (standard and custom)
            var roles = p.Claims
                .Where(x =>
                    x.Type == ClaimTypes.Role ||
                    x.Type.EndsWith("/role", StringComparison.OrdinalIgnoreCase) ||
                    x.Type.EndsWith("role", StringComparison.OrdinalIgnoreCase) ||
                    x.Type.Equals("roles", StringComparison.OrdinalIgnoreCase) ||
                    x.Type.EndsWith("/roles", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToArray();

            if (roles.Length == 0) return null;
            return string.Join(", ", roles);
        }

        public void Dispose()
        {
            try
            {
                AuthStateProvider.AuthenticationStateChanged -= OnAuthenticationStateChanged;
            }
            catch
            {
                // ignore
            }

            try
            {
                // best-effort: request JS to remove listeners (do not await)
                _ = JSRuntime.InvokeVoidAsync("authLinkDiv.unregisterOutsideClick", panelRef);
            }
            catch
            {
                // ignore
            }

            // Intentionally do NOT call dotNetRef?.Dispose() here to avoid a race:
            // disposing the DotNetObjectReference while JS unregister is still pending results
            // in the "no tracked object" JS error. The reference will be GC'd later.
            dotNetRef = null;
        }
    }
}