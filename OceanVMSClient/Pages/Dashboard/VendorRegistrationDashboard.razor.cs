using Blazored.LocalStorage;
using Entities.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using OceanVMSClient.AuthProviders;
using OceanVMSClient.Helpers;
using OceanVMSClient.HttpRepoInterface.VendorRegistration;
using OceanVMSClient.HttpRepoInterface.POModule;
using Shared.DTO.VendorReg;
using Shared.DTO.VendorReg;
using Shared.RequestFeatures;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace OceanVMSClient.Pages.Dashboard
{
    public partial class VendorRegistrationDashboard : IDisposable
    {
        [Inject] private IVendorRegistrationRepository VendorRegRepo { get; set; } = default!;
        [Inject] private IInvoiceApproverRepository InvoiceApproverRepository { get; set; } = default!;
        [Inject] private AuthenticationStateProvider AuthStateProvider { get; set; } = default!;
        [Inject] private ILocalStorageService LocalStorage { get; set; } = default!;
        [Inject] private NavigationManager NavigationManager { get; set; } = default!;

        private Shared.DTO.VendorReg.VendorRegistrationStatsDto? Stats;
        private List<VendorRegistrationFormDto> LastPending = new();
        private bool IsInvoiceReviewer;
        private bool ShowInvReviewer;
        private bool IsLoadingLastPending = false;
        private string ReviewerName = "Vendor Dashboard";
        private DateTime? LastRefreshedAt;

        protected override async Task OnInitializedAsync()
        {
            var auth = await AuthStateProvider.GetAuthenticationStateAsync();
            var user = auth.User;

            // Use same robust reviewer-name initialization as InvReviewerDashBoard
            await SetReviewerNameFromClaimsOrStorageAsync(user);

            // Determine Invoice Reviewer using same logic used in Home.razor:
            // - Vendor validator/approver roles take precedence (do not show invoice reviewer dashboard)
            // - Otherwise prefer repository check using EmployeeId from user context
            // - Fallback to role-name inspection if repo/context not available
            await DetermineInvoiceReviewerAsync(user);

            // Only load data for Vendor Validator / Vendor Approver roles
            if (RoleHelper.UserIsVendorValidator(user) || RoleHelper.UserIsVendorApprover(user))
            {
                await LoadAll();
            }

            // Keep reviewer name and reviewer access in sync when auth state changes
            AuthStateProvider.AuthenticationStateChanged += OnAuthenticationStateChanged;
        }

        private async Task DetermineInvoiceReviewerAsync(ClaimsPrincipal? user)
        {
            try
            {
                // Vendor roles take precedence; if present do not enable invoice reviewer dashboard here
                var isVendorRoleUser = RoleHelper.UserIsVendorValidator(user) || RoleHelper.UserIsVendorApprover(user);
                //if (isVendorRoleUser)
                //{
                //    IsInvoiceReviewer = false;
                //    StateHasChanged();
                //    return;
                //}

                // Load user context (claims + local storage) similarly to Home.razor
                var ctx = await user.LoadUserContextAsync(LocalStorage);
                var userType = ctx.UserType;

                if (!string.IsNullOrWhiteSpace(userType) &&
                    string.Equals(userType, "EMPLOYEE", StringComparison.OrdinalIgnoreCase) &&
                    ctx.EmployeeId.HasValue)
                {
                    try
                    {
                        IsInvoiceReviewer = await InvoiceApproverRepository.IsEmployeeInvoiceReviewerAsync(ctx.EmployeeId.Value);
                    }
                    catch
                    {
                        // on errors treat as not a reviewer
                        IsInvoiceReviewer = false;
                    }
                }
                else
                {
                    // Fallback: inspect roles for invoice reviewer keywords (best-effort)
                    IsInvoiceReviewer = RoleHelper.UserHasRole(user, r =>
                         !string.IsNullOrWhiteSpace(r)
                         && r.Contains("invoice", StringComparison.OrdinalIgnoreCase)
                         && r.Contains("reviewer", StringComparison.OrdinalIgnoreCase));
                }

                StateHasChanged();
            }
            catch
            {
                // on unexpected errors treat as not a reviewer
                IsInvoiceReviewer = false;
                StateHasChanged();
            }
        }

        private async Task LoadAll()
        {
            await LoadStats();
            await LoadLastPendingRegistrations();
            LastRefreshedAt = DateTime.UtcNow;
            StateHasChanged();
        }

        private async Task LoadStats()
        {
            try
            {
                Stats = await VendorRegRepo.GetVendorRegistrationStatisticsAsync();
            }
            catch (Exception ex)
            {
                // log/handle as required - keep UI stable
                Console.Error.WriteLine($"Error loading vendor stats: {ex.Message}");
                Stats = new VendorRegistrationStatsDto(); // safe fallback with zeros
            }
        }

        private async Task LoadLastPendingRegistrations()
        {
            IsLoadingLastPending = true;
            try
            {
                var reviewerParams = new VendorRegistrationFormParameters { ReviewerStatus = "Pending", OrderBy = "CreatedDate desc" };
                var approverParams = new VendorRegistrationFormParameters { ApproverStatus = "Pending", OrderBy = "CreatedDate desc" };

                var r1 = await VendorRegRepo.GetAllVendorRegistration(reviewerParams);
                var r2 = await VendorRegRepo.GetAllVendorRegistration(approverParams);

                // Merge results, remove duplicates by Id and take first 5
                var combined = (r1.Items ?? new List<VendorRegistrationFormDto>())
                               .Concat(r2.Items ?? new List<VendorRegistrationFormDto>())
                               .GroupBy(x => x.Id)
                               .Select(g => g.First())
                               .Take(5)
                               .ToList();

                LastPending = combined;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error loading pending regs: {ex.Message}");
                LastPending = new List<VendorRegistrationFormDto>();
            }
            finally
            {
                IsLoadingLastPending = false;
            }
        }

        private async Task Refresh()
        {
            await LoadAll();
        }

        private static string? GetClaimValueInsensitive(ClaimsPrincipal? user, string claimKey)
        {
            if (user == null) return null;
            var c = user.Claims.FirstOrDefault(x =>
                string.Equals(x.Type, claimKey, StringComparison.OrdinalIgnoreCase)
                || x.Type.EndsWith($"/{claimKey}", StringComparison.OrdinalIgnoreCase)
                || x.Type.EndsWith(claimKey, StringComparison.OrdinalIgnoreCase));
            return c?.Value;
        }

        private async Task SetReviewerNameFromClaimsOrStorageAsync(ClaimsPrincipal? user)
        {
            try
            {
                string? name = null;

                // 1) Prefer explicit FullName claim
                name = GetClaimValueInsensitive(user, "FullName") ?? GetClaimValueInsensitive(user, "fullName");

                // 2) Prefer combination of FirstName + LastName
                if (string.IsNullOrWhiteSpace(name))
                {
                    var first = GetClaimValueInsensitive(user, "FirstName") ?? GetClaimValueInsensitive(user, ClaimTypes.GivenName);
                    var last = GetClaimValueInsensitive(user, "LastName") ?? GetClaimValueInsensitive(user, ClaimTypes.Surname);
                    if (!string.IsNullOrWhiteSpace(first) || !string.IsNullOrWhiteSpace(last))
                        name = $"{first} {last}".Trim();
                }

                // 3) Check localStorage persisted fullName
                if (string.IsNullOrWhiteSpace(name))
                {
                    var storedFullName = await LocalStorage.GetItemAsync<string>("fullName");
                    if (!string.IsNullOrWhiteSpace(storedFullName))
                        name = storedFullName;
                }

                // 4) Fallback to generic name claims or Identity.Name
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = GetClaimValueInsensitive(user, "name")
                                               ?? GetClaimValueInsensitive(user, ClaimTypes.Name)
                                               ?? user?.Identity?.Name;
                }

                if (!string.IsNullOrWhiteSpace(name))
                    ReviewerName = name;
            }
            catch
            {
                // ignore and keep default
            }
        }

        private void OnAuthenticationStateChanged(Task<AuthenticationState> task)
        {
            _ = InvokeAsync(async () =>
            {
                try
                {
                    var authState = await task;
                    await SetReviewerNameFromClaimsOrStorageAsync(authState.User);
                    await DetermineInvoiceReviewerAsync(authState.User);
                    StateHasChanged();
                }
                catch
                {
                    // swallow exceptions to avoid breaking UI updates
                }
            });
        }

        private void OnVendorRegListClick()
        {
            // Navigate to the vendor registration list page
            NavigationManager.NavigateTo("/vendor-registrations");
        }

        public void Dispose()
        {
            try
            {
                AuthStateProvider.AuthenticationStateChanged -= OnAuthenticationStateChanged;
            }
            catch { }
        }


    }
}
