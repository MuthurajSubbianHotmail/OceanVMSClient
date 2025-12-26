using Microsoft.AspNetCore.Components;
using OceanVMSClient.HttpRepoInterface.PoModule;
using Shared.DTO.POModule;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;
using OceanVMSClient.HttpRepo.Authentication;
using System.Security.Claims;
using OceanVMSClient.HttpRepoInterface.POModule;
using MudBlazor;
namespace OceanVMSClient.Pages.POModule
{
    public partial class ViewPurchaseOrder
    {
        private bool isDetailsLoading = true;

        // Placeholder for the model (kept as-is)
        [Parameter]
        public Guid PurchaseOrderId { get; set; }

        // Assuming this model property exists in the original component (left unchanged)
        public PurchaseOrderDto PurchaseOrderDetails { get; set; } = new PurchaseOrderDto();
        [Inject]
        public IPurchaseOrderRepository purchaseOrderRepository { get; set; }
        [Inject]
        public IInvoiceApproverRepository invoiceApproverRepository { get; set; }

        // Cached user type loaded from local storage before fetching data
        private string? _userType;
        private Guid? _vendorId;
        private Guid? _vendorContactId;
        private Guid? _employeeId;

        [Inject]
        public NavigationManager NavigationManager { get; set; }

        // Inject Blazored local storage to read userType
        [Inject]
        public ILocalStorageService LocalStorage { get; set; } = default!;
        [CascadingParameter]
        public Task<AuthenticationState> AuthState { get; set; } = default!;

        protected override async Task OnInitializedAsync()
        {

            // Try to get values from authenticated user's claims first
            var authState = await AuthState;
            var user = authState.User;

            if (user?.Identity?.IsAuthenticated == true)
            {

                // Try vendor/employee ids from claims (claim names depend on your auth server)
                _userType = GetClaimValue(user, "userType");
                _vendorId = ParseGuid(GetClaimValue(user, "vendorPK") ?? GetClaimValue(user, "vendorId"));
                _vendorContactId = ParseGuid(GetClaimValue(user, "vendorContactId") ?? GetClaimValue(user, "vendorContact"));
                _employeeId = ParseGuid(GetClaimValue(user, "empPK") ?? GetClaimValue(user, "EmployeeId"));
            }
            else
            {
                NavigationManager.NavigateTo("/");
            }

            // Fallback to local storage if claims do not contain the info
            if (string.IsNullOrWhiteSpace(_userType))
            {
                _userType = await LocalStorage.GetItemAsync<string>("userType");
            }

            if (_userType == "VENDOR")
            {
                _vendorId ??= ParseGuid(await LocalStorage.GetItemAsync<string>("vendorPK"));
                _vendorContactId ??= ParseGuid(await LocalStorage.GetItemAsync<string>("vendorContactId"));
            }
            else
            {
                _employeeId ??= ParseGuid(await LocalStorage.GetItemAsync<string>("empPK"));
            }


            isDetailsLoading = true;
            await LoadPurchaseOrderDetails();

            // compute permission after details and user type are available
            await UpdateCreateInvoicePermissionAsync();

            isDetailsLoading = false;
        }

        // New: permission variable used by the MudButton
        private bool CanCreateInvoice { get; set; } = false;
        private string CurrentRoleName { get; set; } = string.Empty;

        // New: compute create-invoice permission based on user type and allowUploadByInitiator
        private async Task UpdateCreateInvoicePermissionAsync()
        {
            // Default deny if PurchaseOrderDetails not loaded
            if (PurchaseOrderDetails == null)
            {
                CanCreateInvoice = false;
                CurrentRoleName = string.Empty;
                return;
            }

            // If user is Employee and upload by initiator is NOT allowed -> check approver assignment asynchronously
            if (string.Equals(_userType.ToUpper(), "EMPLOYEE", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (_employeeId == null || _employeeId == Guid.Empty)
                    {
                        CanCreateInvoice = false;
                        CurrentRoleName = string.Empty;
                        return;
                    }

                    var empInitiatorPermission = await invoiceApproverRepository.IsInvoiceApproverAsync(PurchaseOrderDetails.ProjectId, _employeeId.Value);
                    CurrentRoleName = empInitiatorPermission.AssignedType ?? string.Empty;
                    if (allowUploadByInitiator)
                    {
                        // If upload by initiator is allowed, any employee can create invoice
                        if (CurrentRoleName.Equals("Initiator", StringComparison.OrdinalIgnoreCase))
                        {
                            CanCreateInvoice = true;
                        }
                        else
                        {
                            CanCreateInvoice = false;
                        }
                    }
                    else
                    {
                        CanCreateInvoice = false;
                    }

                }
                catch
                {
                    // On error, be conservative and disallow creation; optionally log the exception
                    CanCreateInvoice = false;
                    CurrentRoleName = string.Empty;
                }
            }
            else
            {
                CurrentRoleName = _userType ?? string.Empty;
                CanCreateInvoice = true;
            }
        }

        private async Task LoadPurchaseOrderDetails()
        {
            PurchaseOrderDetails = await purchaseOrderRepository.GetPurchaseOrderById(PurchaseOrderId);
        }

        // Safe, formatted display properties to avoid null reference exceptions and centralize formatting
        private string PoNumberText =>
            PurchaseOrderDetails is null
                ? "—"
                : $"{PurchaseOrderDetails.PoTypeName ?? string.Empty}{(string.IsNullOrWhiteSpace(PurchaseOrderDetails?.PoTypeName) ? string.Empty : "-")}{PurchaseOrderDetails.SAPPONumber ?? string.Empty}";

        private string PoDateText => PurchaseOrderDetails?.SAPPODate is DateTime d ? d.ToString("dd-MMM-yy") : "—";
        private string VendorText => PurchaseOrderDetails?.VendorName ?? "—";
        private string ProjectText => PurchaseOrderDetails?.ProjectName ?? "—";

        private string ItemValueText => PurchaseOrderDetails != null ? PurchaseOrderDetails.ItemValue.ToString("N2") : "0.00";
        private string SpecialDiscountText => PurchaseOrderDetails != null ? PurchaseOrderDetails.SpecialDiscount.ToString("N2") : "0.00";
        private string GSTTotalText => PurchaseOrderDetails != null ? PurchaseOrderDetails.GSTTotal.ToString("N2") : "0.00";
        private string TotalValueText => PurchaseOrderDetails != null ? PurchaseOrderDetails.TotalValue.ToString("N2") : "0.00";

        private string InvoiceStatusText => PurchaseOrderDetails?.InvoiceStatus ?? "Unknown";
        private string PreviousInvoiceCountText => PurchaseOrderDetails != null && PurchaseOrderDetails.PreviousInvoiceCount.HasValue
            ? PurchaseOrderDetails.PreviousInvoiceCount.Value.ToString()
            : "0";

        private string PreviousInvoiceValueText => PurchaseOrderDetails != null && PurchaseOrderDetails.PreviousInvoiceValue.HasValue
            ? PurchaseOrderDetails.PreviousInvoiceValue.Value.ToString("N2")
            : "0.00";

        private string InvoiceBalanceValueText => PurchaseOrderDetails != null && PurchaseOrderDetails.InvoiceBalanceValue.HasValue
            ? PurchaseOrderDetails.InvoiceBalanceValue.Value.ToString("N2")
            : "0.00";

        private string PaidValueText => PurchaseOrderDetails != null ? PurchaseOrderDetails.PaidValue.ToString("N2") : "0.00";
        public bool allowUploadByInitiator => PurchaseOrderDetails != null && PurchaseOrderDetails.AllowInvUploadByInitiator == true;
        

        // Returns a MudBlazor Color based on the current invoice status
        private Color GetInvoiceChipColor()
        {
            var status = (InvoiceStatusText ?? string.Empty).Trim().ToLowerInvariant();

            return status switch
            {
                
                // Not Invoice
                "not invoiced" => Color.Default,
                // Positive
                "fully invoiced" or "paid" => Color.Success,

                // Negative
                "rejected" or "declined" or "overdue" => Color.Error,

                // Attention / waiting
                "pending" or "awaiting approval" or "awaiting" => Color.Warning,

                // Informational / partial
                "part invoiced" or "partially paid" or "partial" => Color.Info,

                // Neutral / cancelled
                "cancelled" or "void" => Color.Secondary,

                // Default
                _ => Color.Secondary,
            };
        }

        private static string? GetClaimValue(ClaimsPrincipal? user, string claimType)
        {
            if (user == null) return null;
            var claim = user.Claims.FirstOrDefault(c => string.Equals(c.Type, claimType, StringComparison.OrdinalIgnoreCase))
                        ?? user.Claims.FirstOrDefault(c => c.Type.EndsWith($"/{claimType}", StringComparison.OrdinalIgnoreCase))
                        ?? user.Claims.FirstOrDefault(c => c.Type.EndsWith(claimType, StringComparison.OrdinalIgnoreCase));
            return claim?.Value;
        }

        // Helper: parse GUID safely
        private static Guid? ParseGuid(String? value)
        {
            return Guid.TryParse(value, out var g) ? g : (Guid?)null;
        }
    }
}
