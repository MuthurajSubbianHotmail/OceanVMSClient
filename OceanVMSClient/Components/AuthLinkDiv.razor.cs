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
    public partial class AuthLinkDiv
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

        protected override async Task OnInitializedAsync()
        {
            // Try JS localStorage first (fast, present after login), fallback to AuthenticationState claims
            try
            {
                // Replace the assignments in OnInitializedAsync (localStorage reads) with unwrapping:
                var userTypeStored = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "userType");
                var fullNameStored = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "fullName");
                var emailStored = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "email");
                var rolesStored = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "userRoles");
                var vendorStored = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "vendorName");

                userTypeValue = !string.IsNullOrWhiteSpace(userTypeStored) ? FormatUserType(UnwrapQuotes(userTypeStored)) : null;
                fullNameValue = !string.IsNullOrWhiteSpace(fullNameStored) ? UnwrapQuotes(fullNameStored) : null;
                emailValue = !string.IsNullOrWhiteSpace(emailStored) ? UnwrapQuotes(emailStored) : null;
                rolesValue = !string.IsNullOrWhiteSpace(rolesStored) ? NormalizeRolesString(UnwrapQuotes(rolesStored)) : null;
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
                        // Replace claim-based assignments to unwrap quotes as well
                        if (userTypeValue == null)
                        {
                            var claim = GetClaimFromPrincipal(user, "userType") ?? GetClaimFromPrincipal(user, "UserType");
                            userTypeValue = FormatUserType(UnwrapQuotes(claim));
                        }

                        if (fullNameValue == null)
                        {
                            fullNameValue = UnwrapQuotes(GetClaimFromPrincipal(user, "fullName"))
                                            ?? $"{UnwrapQuotes(GetClaimFromPrincipal(user, ClaimTypes.GivenName))} {UnwrapQuotes(GetClaimFromPrincipal(user, ClaimTypes.Surname))}".Trim();
                            if (string.IsNullOrWhiteSpace(fullNameValue))
                                fullNameValue = user.Identity?.Name;
                        }

                        if (emailValue == null)
                            emailValue = UnwrapQuotes(GetClaimFromPrincipal(user, ClaimTypes.Email) ?? GetClaimFromPrincipal(user, "email"));

                        if (rolesValue == null)
                            rolesValue = NormalizeRolesString(UnwrapQuotes(GetRolesFromPrincipal(user) ?? string.Empty));

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

        // Toggle/close helpers used by the Razor markup
        private void TogglePanel() => isPanelOpen = !isPanelOpen;
        private void ClosePanel() => isPanelOpen = false;
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

        private static string? GetRolesFromPrincipal(ClaimsPrincipal p)
        {
            if (p == null) return null;

            // Common role claim types: ClaimTypes.Role, "role", "roles"
            var roleClaims = p.Claims
                .Where(c => string.Equals(c.Type, ClaimTypes.Role, StringComparison.OrdinalIgnoreCase)
                         || c.Type.EndsWith("/role", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(c.Type, "role", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(c.Type, "roles", StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Value?.Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v) && !string.Equals(v, "EMPLOYEE", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (roleClaims.Length > 0)
                return string.Join(", ", roleClaims.Distinct(StringComparer.OrdinalIgnoreCase));

            // fallback: if a single claim contains a JSON array string like ["ROLE"], try to parse it,
            // then filter out "EMPLOYEE"
            var maybeRoles = p.Claims.FirstOrDefault(c => (c.Type ?? string.Empty).EndsWith("roles", StringComparison.OrdinalIgnoreCase))?.Value;
            if (!string.IsNullOrWhiteSpace(maybeRoles))
            {
                var normalized = NormalizeRolesString(maybeRoles);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    var parts = normalized.Split(',')
                                          .Select(s => s.Trim())
                                          .Where(s => !string.IsNullOrEmpty(s) && !string.Equals(s, "EMPLOYEE", StringComparison.OrdinalIgnoreCase))
                                          .Distinct(StringComparer.OrdinalIgnoreCase)
                                          .ToArray();
                    if (parts.Length > 0)
                        return string.Join(", ", parts);
                }
            }

            return null;
        }

        private static string NormalizeRolesString(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;
            var s = raw.Trim();
            if (s.StartsWith("[") && s.EndsWith("]"))
            {
                try
                {
                    var arr = JsonSerializer.Deserialize<string[]>(s);
                    if (arr != null && arr.Length > 0)
                        return string.Join(", ", arr.Select(r => r?.Trim('"', '\'').Trim()));
                }
                catch
                {
                    s = s.Trim('[', ']', '"', '\'');
                }
            }
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
    }
}