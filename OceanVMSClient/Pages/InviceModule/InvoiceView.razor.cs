using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using OceanVMSClient.HttpRepoInterface.InvoiceModule;
using OceanVMSClient.HttpRepoInterface.PoModule;
using OceanVMSClient.HttpRepoInterface.POModule;
using Shared.DTO.POModule;
using System.Security.Claims;
using MudBlazor;
using Microsoft.AspNetCore.Components.Web;

namespace OceanVMSClient.Pages.InviceModule
{
    public partial class InvoiceView
    {
        [CascadingParameter]
        public Task<AuthenticationState> AuthState { get; set; } = default!;

        [Inject] public NavigationManager NavigationManager { get; set; }
        [Inject] public ILocalStorageService LocalStorage { get; set; } = default!;
        [Inject] public ILogger<InvoiceView> Logger { get; set; } = default!;
        [Inject] public IInvoiceRepository InvoiceRepository { get; set; } = default!;
        [Inject] public IVendorRepository VendorRepository { get; set; } = default!;
        [Inject] public IPurchaseOrderRepository PurchaseOrderRepository { get; set; } = default!;

        // Route parameter for the invoice to view
        [Parameter]
        public string? InvoiceId { get; set; }

        private InvoiceDto _invoiceDto = new InvoiceDto();
        private InvInitiatorReviewCompleteDto _initiatorReviewCompleteDto = new InvInitiatorReviewCompleteDto();

        private bool _isInvRejected = false;
        private Guid InvoiceGuid;
        private string VendorName = string.Empty;
        private string PurchaseOrderNumber = string.Empty;
        private DateTime PurchaseOrderDate = DateTime.Today;

        // Purchase order details for right-side display
        private PurchaseOrderDto? PurchaseOrderDetails { get; set; }

        private string PoDateText =>
            PurchaseOrderDetails?.SAPPODate is DateTime d ? d.ToString("dd-MMM-yy") : "—";

        private string ProjectNameText => PurchaseOrderDetails?.ProjectName ?? "—";

        private string PoValueText => PurchaseOrderDetails != null ? PurchaseOrderDetails.ItemValue.ToString("N2") : "0.00";

        private string PoTaxText => PurchaseOrderDetails != null ? PurchaseOrderDetails.GSTTotal.ToString("N2") : "0.00";

        private string PoTotalText => PurchaseOrderDetails != null ? PurchaseOrderDetails.TotalValue.ToString("N2") : "0.00";

        private string PrevInvoiceCountText => PurchaseOrderDetails?.PreviousInvoiceCount?.ToString() ?? "0";

        private string PrevInvoiceValueText => PurchaseOrderDetails != null && PurchaseOrderDetails.PreviousInvoiceValue.HasValue
            ? PurchaseOrderDetails.PreviousInvoiceValue.Value.ToString("N2")
            : "0.00";

        private string InvoiceBalanceValueText => PurchaseOrderDetails != null && PurchaseOrderDetails.InvoiceBalanceValue.HasValue
            ? PurchaseOrderDetails.InvoiceBalanceValue.Value.ToString("N2")
            : "0.00";

        // EditContext (not used for editing here, kept for compatibility)
        private EditContext? _editContext;

        protected override async Task OnParametersSetAsync()
        {
            await base.OnParametersSetAsync();

            try
            {
                if (!string.IsNullOrWhiteSpace(InvoiceId) && Guid.TryParse(InvoiceId, out var parsed))
                {
                    InvoiceGuid = parsed;
                    _invoiceDto = await InvoiceRepository.GetInvoiceById(InvoiceGuid) ?? new InvoiceDto();

                    // set simple display values
                    if (_invoiceDto.PurchaseOrderId != null && _invoiceDto.PurchaseOrderId != Guid.Empty)
                    {
                        try
                        {
                            PurchaseOrderDetails = await PurchaseOrderRepository.GetPurchaseOrderById(_invoiceDto.PurchaseOrderId);
                            PurchaseOrderNumber = PurchaseOrderDetails?.SAPPONumber ?? string.Empty;
                            PurchaseOrderDate = PurchaseOrderDetails?.SAPPODate ?? DateTime.Today;
                            
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning(ex, "Failed to load purchase order for invoice {InvoiceId}", InvoiceGuid);
                        }
                    }

                    if (_invoiceDto.VendorId != Guid.Empty)
                    {
                        try
                        {
                            var vendor = await VendorRepository.GetVendorById(_invoiceDto.VendorId);
                            VendorName = vendor?.VendorName ?? string.Empty;
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning(ex, "Failed to load vendor for invoice {InvoiceId}", InvoiceGuid);
                        }
                    }
                    if(string.Equals(_invoiceDto.InvoiceStatus, "Rejected", StringComparison.OrdinalIgnoreCase))
                    {
                        _isInvRejected = true;
                    }
                    else
                    {
                        _isInvRejected = false;
                    }
                        // create EditContext so UI helpers that expect it can work
                        _editContext = new EditContext(_invoiceDto);
                    StateHasChanged();
                }
                else
                {
                    Logger.LogWarning("InvoiceId route parameter missing or invalid.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error loading invoice view");
            }
        }

        protected override async Task OnInitializedAsync()
        {
            // no-op beyond typical user context; keep for future
            try
            {
                await LoadUserContextAsync();
            }
            catch
            {
                // ignore
            }
        }

        // Keep same mapping used in InvoiceList child row for chip color
        private Color GetInvoiceChipColor(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return Color.Default;

            var s = status.Trim().ToLowerInvariant();
            return s switch
            {
                "approved" or "paid" or "fully invoiced" => Color.Success,
                "rejected" or "declined" or "overdue" => Color.Error,
                "submitted" or "awaiting approval" or "awaiting" => Color.Default,
                "with initiator" or "with checker" or "with validator" or "with approver" or "under review" => Color.Warning,
                "part invoiced" or "partially paid" or "partial" => Color.Info,
                "cancelled" or "void" => Color.Secondary,
                _ => Color.Secondary
            };
        }

        // Map invoice status to workflow stepper index
        private int GetWorkflowStepIndex(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return 0;

            var s = status.Trim().ToLowerInvariant();

            // map to defined steps order:
            // 0 Submitted
            // 1 With Initiator
            // 2 With Checker
            // 3 With Validator
            // 4 With Approver
            // 5 With Accounts Payable
            // 6 Approved
            return s switch
            {
                "submitted" => 0,
                "with initiator" or "initiator" or "initiator review" => 1,
                "with checker" or "checker" or "checker review" => 2,
                "with validator" or "validator" or "validator review" => 3,
                "with approver" or "approver" or "approver review" => 4,
                "accounts payable" or "accounts" or "ap" or "with accounts payable" => 5,
                "approved" or "paid" or "fully invoiced" => 6,
                _ => 0
            };
        }

        #region User context helpers

        private string? _userType;
        private Guid? _vendorId;
        private Guid? _vendorContactId;
        private Guid? _employeeId;

        private async Task LoadUserContextAsync()
        {
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

            if (string.IsNullOrWhiteSpace(_userType))
            {
                _userType = await LocalStorage.GetItemAsync<string>("userType");
            }

            if (string.Equals(_userType, "VENDOR", StringComparison.OrdinalIgnoreCase))
            {
                _vendorId ??= ParseGuid(await LocalStorage.GetItemAsync<string>("vendorPK"));
                _vendorContactId ??= ParseGuid(await LocalStorage.GetItemAsync<string>("vendorContactId"));
                }
                else
                {
                    _employeeId ??= ParseGuid(await LocalStorage.GetItemAsync<string>("empPK"));
                }
            }

        #endregion

        #region Helpers

        private static string? GetClaimValue(ClaimsPrincipal? user, string claimType)
        {
            if (user == null) return null;
            var claim = user.Claims.FirstOrDefault(c => string.Equals(c.Type, claimType, StringComparison.OrdinalIgnoreCase))
                        ?? user.Claims.FirstOrDefault(c => c.Type.EndsWith($"/{claimType}", StringComparison.OrdinalIgnoreCase))
                        ?? user.Claims.FirstOrDefault(c => c.Type.EndsWith(claimType, StringComparison.OrdinalIgnoreCase));
            return claim?.Value;
        }

        private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var g) ? g : (Guid?)null;
        #endregion
    }
}
