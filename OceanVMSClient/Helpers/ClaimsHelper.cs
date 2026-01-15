// PSEUDOCODE / PLAN:
// 1. Create a single, reusable helper placed under Helpers so all pages/components can call it.
// 2. Provide an extension method for ClaimsPrincipal to improve discoverability and call-site readability.
// 3. Keep the exact lookup logic as the original method:
//    a. If user is null -> return null.
//    b. Try to find a claim whose Type exactly equals claimType (case-insensitive).
//    c. If not found, try claim.Type.EndsWith("/" + claimType) (case-insensitive).
//    d. If still not found, try claim.Type.EndsWith(claimType) (case-insensitive).
//    e. Return the claim value or null if none found.
// 4. Add XML docs and small examples in comments to guide usage.
// 5. Make the class static and thread-safe (pure function).
//
// USAGE EXAMPLES:
//   var value = user.GetClaimValue("userType");
//   var ctx = await user.LoadUserContextAsync(localStorage);
//   var vendorId = ctx.VendorId;
//
// This file defines a small, focused helper so any Razor page or component can call it.
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Blazored.LocalStorage;

namespace OceanVMSClient.Helpers
{
    /// <summary>
    /// Helper extensions for retrieving claim values from a <see cref="ClaimsPrincipal"/> and
    /// for loading a simple user context (userType + ids + role) with localStorage fallback.
    /// </summary>
    public static class ClaimsHelper
    {
        /// <summary>
        /// Result object returned by <see cref="LoadUserContextAsync(ClaimsPrincipal,System.Threading.Tasks.Task{Blazored.LocalStorage.ILocalStorageService})"/>.
        /// Contains the discovered user type, vendor/employee ids and the user's role (if present in claims).
        /// </summary>
        public sealed record UserContext(string? UserType, Guid? VendorId, Guid? VendorContactId, Guid? EmployeeId, string? Role);

        /// <summary>
        /// Retrieves the value of a claim by trying multiple matching strategies:
        /// 1) Exact match on claim.Type (case-insensitive)
        /// 2) EndsWith("/{claimType}") (case-insensitive)
        /// 3) EndsWith("{claimType}") (case-insensitive)
        /// Returns null if no matching claim is found or <paramref name="user"/> is null.
        /// </summary>
        /// <param name="user">The <see cref="ClaimsPrincipal"/> instance (may be null).</param>
        /// <param name="claimType">The claim type to search for.</param>
        /// <returns>The claim value, or null if not found.</returns>
        public static string? GetClaimValue(this ClaimsPrincipal? user, string claimType)
        {
            if (user == null) return null;
            if (string.IsNullOrWhiteSpace(claimType)) return null;

            // 1) Exact match (case-insensitive)
            var claim = user.Claims.FirstOrDefault(c => string.Equals(c.Type, claimType, StringComparison.OrdinalIgnoreCase))
                        // 2) Ends with "/{claimType}" (case-insensitive)
                        ?? user.Claims.FirstOrDefault(c => c.Type.EndsWith($"/{claimType}", StringComparison.OrdinalIgnoreCase))
                        // 3) Ends with "{claimType}" (case-insensitive)
                        ?? user.Claims.FirstOrDefault(c => c.Type.EndsWith(claimType, StringComparison.OrdinalIgnoreCase));

            return claim?.Value;
        }

        /// <summary>
        /// Loads a small user context (userType, vendorId, vendorContactId, employeeId, role) by reading claims first
        /// and falling back to values stored in Blazored.LocalStorage. Designed for use in components/pages.
        /// Examples:
        ///   var ctx = await user.LoadUserContextAsync(localStorage);
        ///   var vendorId = ctx.VendorId;
        ///   var role = ctx.Role;
        /// </summary>
        /// <param name="user">Claims principal (may be null).</param>
        /// <param name="localStorage">Blazored local storage service (may be null).</param>
        /// <returns>A <see cref="UserContext"/> with values discovered from claims or local storage.</returns>
        public static async Task<UserContext> LoadUserContextAsync(this ClaimsPrincipal? user, ILocalStorageService? localStorage)
        {
            // Read from claims first
            var userType = user.GetClaimValue("userType");
            var vendorId = ParseGuid(user.GetClaimValue("vendorPK") ?? user.GetClaimValue("vendorId"));
            var vendorContactId = ParseGuid(user.GetClaimValue("vendorContactId") ?? user.GetClaimValue("vendorContact"));
            var employeeId = ParseGuid(user.GetClaimValue("empPK") ?? user.GetClaimValue("EmployeeId"));

            // Role: try common role claim names (reuses GetClaimValue matching rules)
            var role = user.GetClaimValue(ClaimTypes.Role)      // "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"
                       ?? user.GetClaimValue("role")
                       ?? user.GetClaimValue("roles");

            // Fallback to local storage when values are missing and localStorage provided
            if (string.IsNullOrWhiteSpace(userType) && localStorage != null)
            {
                try
                {
                    userType = await localStorage.GetItemAsync<string>("userType");
                }
                catch
                {
                    // ignore local storage read errors - return what we have
                }
            }

            if (!string.IsNullOrWhiteSpace(userType) && string.Equals(userType, "VENDOR", StringComparison.OrdinalIgnoreCase))
            {
                if ((vendorId == null || vendorId == Guid.Empty) && localStorage != null)
                {
                    try
                    {
                        var v = await localStorage.GetItemAsync<string>("vendorPK");
                        vendorId = ParseGuid(v);
                    }
                    catch { }
                }

                if ((vendorContactId == null || vendorContactId == Guid.Empty) && localStorage != null)
                {
                    try
                    {
                        var vc = await localStorage.GetItemAsync<string>("vendorContactId");
                        vendorContactId = ParseGuid(vc);
                    }
                    catch { }
                }
            }
            else
            {
                if ((employeeId == null || employeeId == Guid.Empty) && localStorage != null)
                {
                    try
                    {
                        var e = await localStorage.GetItemAsync<string>("empPK");
                        employeeId = ParseGuid(e);
                    }
                    catch { }
                }
            }

            return new UserContext(userType, vendorId, vendorContactId, employeeId, role);
        }

        // internal helper
        private static Guid? ParseGuid(string? value)
            => Guid.TryParse(value, out var g) ? g : (Guid?)null;
    }
}
