using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using MudBlazor;
using OceanVMSClient.HttpRepo.Authentication;
using OceanVMSClient.HttpRepoInterface.PoModule;
using OceanVMSClient.HttpRepoInterface.POModule;
using OceanVMSClient.Pages.InviceModule;
using Shared.DTO.POModule;
using System.Security.Claims;
using System.Threading;

namespace OceanVMSClient.Pages.POModule
{
    public partial class ViewPurchaseOrder
    {
        private bool isDetailsLoading = true;
        private bool isActionInProgress = false;
        private CancellationTokenSource _cts = new();

        // Placeholder for the model (kept as-is)
        [Parameter] public Guid PurchaseOrderId { get; set; }

        // Assuming this model property exists in the original component (left unchanged)
        public PurchaseOrderDto PurchaseOrderDetails { get; set; } = new PurchaseOrderDto();

        [Inject] public IPurchaseOrderRepository purchaseOrderRepository { get; set; }
        [Inject] public IInvoiceApproverRepository invoiceApproverRepository { get; set; }
        [Inject] public ILocalStorageService LocalStorage { get; set; } = default!;
        [Inject] public NavigationManager NavigationManager { get; set; }
        [Inject] public ISnackbar Snackbar { get; set; } = default!;
        [Inject] public ILogger<ViewPurchaseOrder> Logger { get; set; } = default!;

        [CascadingParameter] public Task<AuthenticationState> AuthState { get; set; } = default!;

        // Cached user type loaded from local storage before fetching data
        private string? _userType;
        private Guid? _vendorId;
        private Guid? _vendorContactId;
        private Guid? _employeeId;

        protected override async Task OnInitializedAsync()
        {
            try
            {
                // Try to get values from authenticated user's claims first
                var authState = await AuthState;
                var user = authState.User;

                if (user?.Identity?.IsAuthenticated == true)
                {
                    _userType = GetClaimValue(user, "userType");
                    _vendorId = ParseGuid(GetClaimValue(user, "vendorPK") ?? GetClaimValue(user, "vendorId"));
                    _vendorContactId = ParseGuid(GetClaimValue(user, "vendorContactId") ?? GetClaimValue(user, "vendorContact"));
                    _employeeId = ParseGuid(GetClaimValue(user, "empPK") ?? GetClaimValue(user, "EmployeeId"));
                }
                else
                {
                    NavigationManager.NavigateTo("/");
                    return;
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

                // Load details with error handling
                try
                {
                    await LoadPurchaseOrderDetails();
                    // compute permission after details and user type are available
                    await UpdateCreateInvoicePermissionAsync();
                }
                catch (OperationCanceledException)
                {
                    // canceled by navigation/dispose - don't show error
                    Logger.LogInformation("LoadPurchaseOrderDetails canceled.");
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Failed to load purchase order details.");
                    Snackbar.Add("Failed to load purchase order details. Please try again.", Severity.Error);
                }
            }
            finally
            {
                isDetailsLoading = false;
                StateHasChanged();
            }
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

            // If user is Employee -> check approver assignment asynchronously
            if (string.Equals(_userType, "Employee", StringComparison.OrdinalIgnoreCase))
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

                    // If upload by initiator is allowed, only Initiator may create
                    if (allowUploadByInitiator)
                    {
                        CanCreateInvoice = CurrentRoleName.Equals("Initiator", StringComparison.OrdinalIgnoreCase) || empInitiatorPermission.IsAssigned;
                    }
                    else
                    {
                        CanCreateInvoice = false;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error checking invoice approver assignment.");
                    CanCreateInvoice = false;
                    CurrentRoleName = string.Empty;
                    Snackbar.Add("Unable to determine invoice creation permission.", Severity.Warning);
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
            // If repository supports CancellationToken in future, pass _cts.Token
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
                "part invoiced" => Color.Info,
                "not invoiced" => Color.Default,
                "rejected" => Color.Error,
                "fully invoiced" => Color.Success,
                "over invoiced" => Color.Warning,
                _ => Color.Default
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

        // Dispose pattern to cancel any ongoing work when component is removed
        public async ValueTask DisposeAsync()
        {
            try
            {
                _cts.Cancel();
            }
            catch { /* ignore */ }
            finally
            {
                _cts.Dispose();
            }
            await Task.CompletedTask;
        }

        private void ShowInvoices(Guid purchaseOrderId)
        {
            var parameters = new DialogParameters { ["PurchaseOrderId"] = purchaseOrderId };
            var options = new DialogOptions
            {
                MaxWidth = MaxWidth.Large,
                FullWidth = true,
                CloseButton = true,
                BackdropClick = false
            };
            DialogService.Show<InvoiceListOfPO>("Invoices", parameters, options);
        }

        private void newInvoice(Guid vendorId, Guid purchaseOrderId)
        {
            // Basic validation
            if (vendorId == Guid.Empty || purchaseOrderId == Guid.Empty)
            {
                Console.WriteLine("Invalid vendor or purchase order id for new invoice.");
                return;
            }

            // Navigate to create-invoice page — adjust route as your app expects
            NavigationManager.NavigateTo($"/newinvoice/{vendorId}/{purchaseOrderId}");
        }
    }
}
