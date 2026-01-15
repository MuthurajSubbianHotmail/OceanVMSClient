using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace OceanVMSClient.Helpers
{
    public static class RoleHelper
    {
        /// <summary>
        /// Returns true for common variants of Accounts Payable roles (e.g. "AP", "Accounts Payable", "Accountspayable").
        /// </summary>
        public static bool IsAccountPayableRole(string? role)
        {
            if (string.IsNullOrWhiteSpace(role)) return false;
            var r = role.Trim().ToLowerInvariant();
            if (r == "ap" || r == "accounts payable" || r == "account payable" || r == "accountspayable" || r == "accountspayables")
                return true;
            return r.Contains("accounts payable") || r.Contains("account payable") || r.Contains("accountspayable");
        }

        /// <summary>
        /// Returns true for admin-like roles (e.g. "Admin", "Administrator", "site-admin").
        /// </summary>
        public static bool IsAdminRole(string? role)
        {
            if (string.IsNullOrWhiteSpace(role)) return false;
            var r = role.Trim().ToLowerInvariant();
            return r == "admin" || r == "administrator" || r.Contains("admin");
        }

        /// <summary>
        /// Returns true for plain vendor roles. Excludes vendor approver/validator/reviewer variants.
        /// </summary>
        public static bool IsVendorRole(string? role)
        {
            if (string.IsNullOrWhiteSpace(role)) return false;
            var r = role.Trim().ToLowerInvariant();

            // Exact vendor or contains vendor but not an approver/validator/reviewer variant
            if (r == "vendor") return true;

            if (r.Contains("vendor"))
            {
                // exclude compound vendor roles that imply approver/validator/reviewer responsibilities
                if (r.Contains("approver") || r.Contains("validator") || r.Contains("reviewer"))
                    return false;

                return true;
            }

            return false;
        }

        /// <summary>
        /// Returns true for vendor validator roles (e.g. "Vendor Validator", "VENDOR VALIDATOR").
        /// Also matches strings that contain both "vendor" and "validator".
        /// </summary>
        public static bool IsVendorValidatorRole(string? role)
        {
            if (string.IsNullOrWhiteSpace(role)) return false;
            var r = role.Trim().ToLowerInvariant();

            if (r == "vendor validator" || r.Contains("vendor validator")) return true;

            // Match roles that explicitly mention both vendor and validator
            if (r.Contains("vendor") && r.Contains("validator")) return true;

            return false;
        }

        /// <summary>
        /// Returns true for vendor approver roles (e.g. "Vendor Approver", "VENDOR APPROVER").
        /// Also matches strings that contain both "vendor" and "approver".
        /// </summary>
        public static bool IsVendorApproverRole(string? role)
        {
            if (string.IsNullOrWhiteSpace(role)) return false;
            var r = role.Trim().ToLowerInvariant();

            if (r == "vendor approver" || r.Contains("vendor approver")) return true;

            // Match roles that explicitly mention both vendor and approver
            if (r.Contains("vendor") && r.Contains("approver")) return true;

            return false;
        }

        /// <summary>
        /// Helper to evaluate whether a ClaimsPrincipal contains a role that matches a predicate.
        /// This handles ClaimTypes.Role, "role"/"roles", "/role", arrays and comma lists.
        /// Example: RoleHelper.UserHasRole(user, RoleHelper.IsAdminRole)
        /// </summary>
        public static bool UserHasRole(ClaimsPrincipal? user, Func<string?, bool> roleMatch)
        {
            if (user?.Identity?.IsAuthenticated != true) return false;

            var roleValues = user.Claims
                .Where(c =>
                    string.Equals(c.Type, ClaimTypes.Role, StringComparison.OrdinalIgnoreCase)
                    || c.Type.EndsWith("/role", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(c.Type, "role", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(c.Type, "roles", StringComparison.OrdinalIgnoreCase)
                    || c.Type.EndsWith("/roles", StringComparison.OrdinalIgnoreCase))
                .SelectMany(c => SplitRoleClaimValue(c.Value))
                .Select(r => r.Trim().Trim('"', '\''))
                .Where(r => !string.IsNullOrEmpty(r));

            return roleValues.Any(rv => roleMatch(rv));
        }

        /// <summary>
        /// Convenience wrappers for ClaimsPrincipal checks.
        /// </summary>
        public static bool UserIsVendor(ClaimsPrincipal? user) => UserHasRole(user, IsVendorRole);
        public static bool UserIsVendorValidator(ClaimsPrincipal? user) => UserHasRole(user, IsVendorValidatorRole);
        public static bool UserIsVendorApprover(ClaimsPrincipal? user) => UserHasRole(user, IsVendorApproverRole);

        private static IEnumerable<string> SplitRoleClaimValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) yield break;

            var s = value.Trim();
            if (s.StartsWith("[") && s.EndsWith("]"))
            {
                s = s.Trim('[', ']', '"', '\'');
            }

            foreach (var part in s.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                yield return part;
        }
    }
}