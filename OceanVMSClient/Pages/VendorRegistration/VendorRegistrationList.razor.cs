using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using MudBlazor;
using OceanVMSClient.HttpRepoInterface.VendorRegistration;
using Shared.DTO.VendorReg;
using Shared.RequestFeatures;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace OceanVMSClient.Pages.VendorRegistration
{
    public partial class VendorRegistrationList
    {
        [CascadingParameter] public Task<AuthenticationState> AuthState { get; set; } = default!;
        [Inject] public ILocalStorageService LocalStorage { get; set; } = default!;
        [Inject] public IVendorRegistrationRepository Repository { get; set; } = default!;
        [Inject] public ISnackbar Snackbar { get; set; } = default!;
        [Inject] public ILogger<VendorRegistrationList> Logger { get; set; } = default!;

        private MudTable<VendorRegistrationFormDto>? _table;
        private readonly int[] _pageSizeOption = { 15, 25, 50 };
        public enum ReviewStatus
        {
            Received,
            Pending,
            Approved,
            Rejected,
            Resent
        }

        private string? registrationNo;
        private string? organizationName;
        private string? responderFName;
        private string? responderLName;
        private string? reviewerStatus;
        private string? approverStatus;
        private string? cityName;
        private string? gstno;
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        private ReviewStatus? reviewStatus;
        private ReviewStatus? approvalStatus;

        private string? _userType;
        private Guid? _employeeId;
        private Guid? _vendorId;

        private List<BreadcrumbItem> _items = new()
        {
            new BreadcrumbItem("Home", href: "/", disabled: false),
            new BreadcrumbItem("Vendor Registrations", href: "/vendor-registrations", disabled: true),
        };

        protected override async Task OnInitializedAsync()
        {
            await LoadUserContextAsync();
        }

        private async Task<TableData<VendorRegistrationFormDto>> GetServerData(TableState state, CancellationToken cancellationToken)
        {
            try
            {
                // normalize "All" filter to null so the server doesn't receive "All" as a value
                string? reviewerFilter = string.IsNullOrWhiteSpace(reviewerStatus) || string.Equals(reviewerStatus, "All", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : reviewerStatus;
                string? approverFilter = string.IsNullOrWhiteSpace(approverStatus) || string.Equals(approverStatus, "All", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : approverStatus    ;

                var parameters = new VendorRegistrationFormParameters
                {
                    PageNumber = state.Page + 1,
                    PageSize = state.PageSize,
                    OrganizationName = string.IsNullOrWhiteSpace(organizationName) ? null : organizationName,
                    ResponderFName = string.IsNullOrWhiteSpace(responderFName) ? null : responderFName,
                    ResponderLName = string.IsNullOrWhiteSpace(responderLName) ? null : responderLName,
                    ReviewerStatus = reviewerFilter,
                    ApproverStatus = approverFilter,
                    GSTNO = string.IsNullOrWhiteSpace(gstno) ? null : gstno
                };

                var response = await Repository.GetAllVendorRegistration(parameters);

                var items = response.Items?.ToList() ?? new List<VendorRegistrationFormDto>();

                return new TableData<VendorRegistrationFormDto>
                {
                    Items = items,
                    TotalItems = response.MetaData?.TotalCount ?? 0
                };
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error loading vendor registrations");
                Snackbar.Add("Failed to load vendor registrations", Severity.Error);
                return new TableData<VendorRegistrationFormDto> { Items = new List<VendorRegistrationFormDto>(), TotalItems = 0 };
            }
        }
        public void OnValueChanged(object sender, EventArgs e)
        {
            if (sender is MudSelect<string> select)
            {
                if (select.Label == "Reviewer Status")
                {
                    reviewerStatus = select.Value;
                }
                else if (select.Label == "Approver Status")
                {
                    approverStatus = select.Value;
                }
            }
            _ = OnSearch();
        }
        private Task OnSearch()
        {
            if (_table != null) return _table.ReloadServerData();
            return Task.CompletedTask;
        }

        private void ResetFilters()
        {
            //registrationNo = organizationName = regCity = string.Empty;
            //_ = OnSearch();
        }

        private void ViewRegistration(Guid id)
        {
            NavigationManager.NavigateTo($"/vendor-registration/{id}");
        }

        private async Task LoadUserContextAsync()
        {
            var authState = await AuthState;
            var user = authState.User;
            if (user?.Identity?.IsAuthenticated == true)
            {
                _userType = GetClaimValue(user, "userType");
                _vendorId = ParseGuid(GetClaimValue(user, "vendorPK") ?? GetClaimValue(user, "vendorId"));
                _employeeId = ParseGuid(GetClaimValue(user, "empPK") ?? GetClaimValue(user, "EmployeeId"));
            }
            else
            {
                NavigationManager.NavigateTo("/");
            }

            if (string.IsNullOrWhiteSpace(_userType))
            {
                _userType = await LocalStorage.GetItemAsync<string>("userType");
            }
        }

        private static string? GetClaimValue(ClaimsPrincipal? user, string claimType)
        {
            if (user == null) return null;
            var claim = user.Claims.FirstOrDefault(c => string.Equals(c.Type, claimType, StringComparison.OrdinalIgnoreCase))
                        ?? user.Claims.FirstOrDefault(c => c.Type.EndsWith($"/{claimType}", StringComparison.OrdinalIgnoreCase))
                        ?? user.Claims.FirstOrDefault(c => c.Type.EndsWith(claimType, StringComparison.OrdinalIgnoreCase));
            return claim?.Value;
        }

        // Add this private helper method to the class to fix CS0103
        private static Guid? ParseGuid(string? value)
        {
            if (Guid.TryParse(value, out var guid))
                return guid;
            return null;
        }
    }
}
