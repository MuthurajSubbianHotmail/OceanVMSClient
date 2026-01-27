using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Primitives;
using MudBlazor;
using OceanVMSClient.HttpRepo.POModule;
using OceanVMSClient.HttpRepoInterface.InvoiceModule;
using OceanVMSClient.HttpRepoInterface.PoModule;
using OceanVMSClient.HttpRepoInterface.POModule;
using Shared.DTO.POModule;
using System.Security.Claims;
namespace OceanVMSClient.Pages.InviceModule
{
    public partial class InvoiceViewVendor
    {
        [CascadingParameter]
        public Task<AuthenticationState> AuthState { get; set; } = default!;
        public Margin _margin = Margin.Dense;
        public Variant _variant = Variant.Text!;
        public Color _labelColor = Color.Default!;
        public Color _valueColor = Color.Default!;
        public Typo _labelTypo = Typo.subtitle2!;
        public Typo _valueTypo = Typo.body2!;
        public Guid _LoggedInEmployeeID = Guid.Empty;
        public string _LoggedInUserType = string.Empty;
        public Guid _LoggedInVendorID = Guid.Empty;
        public string _CurrentRoleName = string.Empty;
        public bool _isInvAssigned = false;
        public bool _isInitiator = false;
        public bool _isChecker = false;
        public bool _isValidator = false;
        public bool _isApprover = false;
        public bool _isApApprover = false;

        [Inject] public NavigationManager NavigationManager { get; set; }
        [Inject] public ILocalStorageService LocalStorage { get; set; } = default!;
        [Inject] public ILogger<InvoiceView> Logger { get; set; } = default!;
        [Inject] public IInvoiceRepository InvoiceRepository { get; set; } = default!;
        [Inject] public IVendorRepository VendorRepository { get; set; } = default!;
        [Inject] public IPurchaseOrderRepository PurchaseOrderRepository { get; set; } = default!;
        [Inject] public IInvoiceApproverRepository invoiceApproverRepository { get; set; }

        // Inject dialog service to show approval summary in a popup
        [Inject] public IDialogService DialogService { get; set; } = default!;

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

        // Loading flag (controls progress indicator)
        private bool isDetailsLoading = true;

        private string PoDateText =>
            PurchaseOrderDetails?.SAPPODate is DateTime d ? d.ToString("dd-MMM-yy") : "—";

        private string ProjectNameText => PurchaseOrderDetails?.ProjectName ?? "—";

        private string PoValueText => PurchaseOrderDetails != null && PurchaseOrderDetails.ItemValue.HasValue ? PurchaseOrderDetails.ItemValue.Value.ToString("N2") : "0.00";

        private string PoTaxText => PurchaseOrderDetails != null && PurchaseOrderDetails.GSTTotal.HasValue ? PurchaseOrderDetails.GSTTotal.Value.ToString("N2") : "0.00";

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

            isDetailsLoading = true;
            try
            {
                await LoadUserContextAsync();
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
                    if (string.Equals(_invoiceDto.InvoiceStatus, "Rejected", StringComparison.OrdinalIgnoreCase))
                    {
                        _isInvRejected = true;
                    }
                    else
                    {
                        _isInvRejected = false;
                    }

                    if (PurchaseOrderDetails != null && _employeeId.HasValue)
                    {
                        var empInitiatorPermission = await invoiceApproverRepository.IsInvoiceApproverAsync(PurchaseOrderDetails.ProjectId, _employeeId.Value);
                        _CurrentRoleName = empInitiatorPermission.AssignedType ?? string.Empty;
                        _isInvAssigned = empInitiatorPermission.IsAssigned;
                    }
                    else
                    {
                        _CurrentRoleName = string.Empty;
                        _isInvAssigned = false;
                    }

                    _isInitiator = await PurchaseOrderRepository.IsEmployeeAssignedforRoleAsync(
                        _invoiceDto.PurchaseOrderId != Guid.Empty ? _invoiceDto.PurchaseOrderId : Guid.Empty,
                        _employeeId ?? Guid.Empty,
                            "Initiator"
                        );
                    _isChecker = await PurchaseOrderRepository.IsEmployeeAssignedforRoleAsync(
                        _invoiceDto.PurchaseOrderId != Guid.Empty ? _invoiceDto.PurchaseOrderId : Guid.Empty,
                        _employeeId ?? Guid.Empty,
                        "Checker"
                    );
                    _isValidator = await PurchaseOrderRepository.IsEmployeeAssignedforRoleAsync(
                        _invoiceDto.PurchaseOrderId != Guid.Empty ? _invoiceDto.PurchaseOrderId : Guid.Empty,
                        _employeeId ?? Guid.Empty,
                        "Validator"
                    );
                    _isApprover = await PurchaseOrderRepository.IsEmployeeAssignedforRoleAsync(
                        _invoiceDto.PurchaseOrderId != Guid.Empty ? _invoiceDto.PurchaseOrderId : Guid.Empty,
                        _employeeId ?? Guid.Empty,
                        "Approver"
                    );

                    // compute tab icons/colors after we have assignment/status info
                    RefreshTabIcons();

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
            finally
            {
                isDetailsLoading = false;
                StateHasChanged();
            }
        }

        // Method called from UI to open approval summary component in a dialog
        private void ShowApprovalSummary()
        {
            var parameters = new DialogParameters
            {
                ["_invoiceDto"] = _invoiceDto,
                ["_PODto"] = PurchaseOrderDetails,
                ["CurrentTab"] = null
            };

            var options = new DialogOptions
            {
                MaxWidth = MaxWidth.Medium,
                FullWidth = true,
                CloseButton = true,
                BackdropClick = true
            };

            DialogService.Show<InvApprovalSummaryComponent>("Approval Summary", parameters, options);
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
                _LoggedInEmployeeID = _employeeId ?? Guid.Empty;
                _LoggedInUserType = _userType ?? string.Empty;
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

        // Initiator tab icon + color (computed)
        private string InitiatorIcon { get; set; } = Icons.Material.Rounded.AirlineSeatReclineNormal;
        private Color InitiatorIconColor { get; set; } = Color.Default;

        // Checker tab icon + color (computed)
        private string CheckerIcon { get; set; } = Icons.Material.Rounded.PlaylistAddCheckCircle;
        private Color CheckerIconColor { get; set; } = Color.Default;

        // Validator tab icon + color (computed)
        private string ValidatorIcon { get; set; } = Icons.Material.Rounded.AssignmentTurnedIn;
        private Color ValidatorIconColor { get; set; } = Color.Default;

        // Approver tab icon + color (computed)
        private string ApproverIcon { get; set; } = Icons.Material.Rounded.HowToReg;
        private Color ApproverIconColor { get; set; } = Color.Default;

        // Accounts Payable tab icon + color (computed)
        private string ApApproverIcon { get; set; } = Icons.Material.Rounded.AccountBalanceWallet;
        private Color ApApproverIconColor { get; set; } = Color.Default;

        //Payment tab icon + color (computed)
        private string PaymentIcon { get; set; } = Icons.Material.Rounded.Payments;
        private Color PaymentIconColor { get; set; } = Color.Default;

        private void UpdateInitiatorTabIcon()
        {
            // Completed -> CheckCircle (Blue)
            if (string.Equals(_invoiceDto?.InitiatorReviewStatus, "Completed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(_invoiceDto?.InitiatorReviewStatus, "Approved", StringComparison.OrdinalIgnoreCase))
            {
                InitiatorIcon = Icons.Material.Filled.CheckCircle;
                InitiatorIconColor = Color.Primary; // Blue
                return;
            }

            // Not assigned to this role -> Block, Default
            // Fix for CS0266 and CS8602: Use GetValueOrDefault() to safely handle nullable bool
            if (!_invoiceDto.IsInitiatorReviewRequired.GetValueOrDefault())
            {
                InitiatorIcon = Icons.Material.Filled.Block;
                InitiatorIconColor = Color.Default;
                return;
            }

            // SLA delayed/escalated -> Alarm / Warning
            if (string.Equals(_invoiceDto?.InitiatorReviewSLAStatus, "Delayed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(_invoiceDto?.InitiatorReviewSLAStatus, "Escalated", StringComparison.OrdinalIgnoreCase))
            {
                InitiatorIcon = Icons.Material.Filled.AccessAlarm;
                InitiatorIconColor = Color.Warning;
                return;
            }
            if (string.Equals(_invoiceDto?.InvoiceStatus, "with initiator", StringComparison.OrdinalIgnoreCase))
            {
                ApproverIcon = Icons.Material.Rounded.AirlineSeatReclineNormal;
                ApproverIconColor = Color.Secondary;
                return;
            }
            // default: within SLA & assigned -> Green airline seat
            InitiatorIcon = Icons.Material.Rounded.AirlineSeatReclineNormal;
            InitiatorIconColor = Color.Default;
        }

        private void UpdateCheckerTabIcon()
        {
            if (string.Equals(_invoiceDto?.CheckerReviewStatus, "Completed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(_invoiceDto?.CheckerReviewStatus, "Approved", StringComparison.OrdinalIgnoreCase))
            {
                CheckerIcon = Icons.Material.Filled.CheckCircle;
                CheckerIconColor = Color.Primary;
                return;
            }

            if (!_invoiceDto.IsCheckerReviewRequired.GetValueOrDefault())
            {
                CheckerIcon = Icons.Material.Filled.Block;
                CheckerIconColor = Color.Default;
                return;
            }

            if (string.Equals(_invoiceDto?.CheckerReviewSLAStatus, "Delayed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(_invoiceDto?.CheckerReviewSLAStatus, "Escalated", StringComparison.OrdinalIgnoreCase))
            {
                CheckerIcon = Icons.Material.Filled.AccessAlarm;
                CheckerIconColor = Color.Warning;
                return;
            }
            if (string.Equals(_invoiceDto?.InvoiceStatus, "with checker", StringComparison.OrdinalIgnoreCase))
            {
                ApproverIcon = Icons.Material.Rounded.PlaylistAddCheckCircle;
                ApproverIconColor = Color.Secondary;
                return;
            }
            CheckerIcon = Icons.Material.Rounded.PlaylistAddCheckCircle;
            CheckerIconColor = Color.Success;
        }

        private void UpdateValidatorTabIcon()
        {
            if (string.Equals(_invoiceDto?.ValidatorReviewStatus, "Completed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(_invoiceDto?.ValidatorReviewStatus, "Approved", StringComparison.OrdinalIgnoreCase))
            {
                ValidatorIcon = Icons.Material.Filled.CheckCircle;
                ValidatorIconColor = Color.Primary;
                return;
            }

            if (!_invoiceDto.IsValidatorReviewRequired.GetValueOrDefault())
            {
                ValidatorIcon = Icons.Material.Filled.Block;
                ValidatorIconColor = Color.Default;
                return;
            }

            if (string.Equals(_invoiceDto?.ValidatorReviewSLAStatus, "Delayed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(_invoiceDto?.ValidatorReviewSLAStatus, "Escalated", StringComparison.OrdinalIgnoreCase))
            {
                ValidatorIcon = Icons.Material.Filled.AccessAlarm;
                ValidatorIconColor = Color.Warning;
                return;
            }
            if (string.Equals(_invoiceDto?.InvoiceStatus, "with validator", StringComparison.OrdinalIgnoreCase))
            {
                ApproverIcon = Icons.Material.Rounded.AssignmentTurnedIn;
                ApproverIconColor = Color.Secondary;
                return;
            }
            ValidatorIcon = Icons.Material.Rounded.AssignmentTurnedIn;
            ValidatorIconColor = Color.Default;
        }

        private void UpdateApprovalTabIcon()
        {
            if (string.Equals(_invoiceDto?.ApproverReviewStatus, "Completed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(_invoiceDto?.ApproverReviewStatus, "Approved", StringComparison.OrdinalIgnoreCase))
            {
                ApproverIcon = Icons.Material.Filled.CheckCircle;
                ApproverIconColor = Color.Primary;
                return;
            }

            if (!_invoiceDto.IsApproverReviewRequired.GetValueOrDefault())
            {
                ApproverIcon = Icons.Material.Filled.Block;
                ApproverIconColor = Color.Default;
                return;
            }


            if (string.Equals(_invoiceDto?.ApproverReviewSLAStatus, "Delayed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(_invoiceDto?.ApproverReviewSLAStatus, "Escalated", StringComparison.OrdinalIgnoreCase))
            {
                ApproverIcon = Icons.Material.Filled.AccessAlarm;
                ApproverIconColor = Color.Warning;
                return;
            }
            if (string.Equals(_invoiceDto?.InvoiceStatus, "with approver", StringComparison.OrdinalIgnoreCase))
            {
                ApproverIcon = Icons.Material.Rounded.HowToReg;
                ApproverIconColor = Color.Secondary;
                return;
            }
            ApproverIcon = Icons.Material.Rounded.HowToReg;
            ApproverIconColor = Color.Default;
        }

        private void UpdateAPApproverTabIcon()
        {
            if (string.Equals(_invoiceDto?.APReviewStatus, "Completed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(_invoiceDto?.APReviewStatus, "Approved", StringComparison.OrdinalIgnoreCase))
            {
                ApApproverIcon = Icons.Material.Filled.CheckCircle;
                ApApproverIconColor = Color.Primary;
                return;
            }

            //if (!_isApApprover || !_isInvAssigned)
            //{
            //    ApApproverIcon = Icons.Material.Filled.Block;
            //    ApApproverIconColor = Color.Default;
            //    return;
            //}

            if (string.Equals(_invoiceDto?.APReviewSLAStatus, "Delayed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(_invoiceDto?.APReviewSLAStatus, "Escalated", StringComparison.OrdinalIgnoreCase))
            {
                ApApproverIcon = Icons.Material.Filled.AccessAlarm;
                ApApproverIconColor = Color.Warning;
                return;
            }
            if (string.Equals(_invoiceDto?.InvoiceStatus, "with validator", StringComparison.OrdinalIgnoreCase))
            {
                ApproverIcon = Icons.Material.Rounded.AccountBalanceWallet;
                ApproverIconColor = Color.Secondary;
                return;
            }
            ApApproverIcon = Icons.Material.Rounded.AccountBalanceWallet;
            ApApproverIconColor = Color.Default;
        }

        // After updating all icons, ensure UI refresh
        private void RefreshTabIcons()
        {
            UpdateInitiatorTabIcon();
            UpdateCheckerTabIcon();
            UpdateValidatorTabIcon();
            UpdateApprovalTabIcon();
            UpdateAPApproverTabIcon();
            StateHasChanged();
        }

        // Added field + OnInitializedAsync override to ensure user context loads
        private bool _userContextLoaded = false;

        protected override async Task OnInitializedAsync()
        {
            await base.OnInitializedAsync();

            if (_userContextLoaded)
                return;

            try
            {
                await LoadUserContextAsync();
                _userContextLoaded = true;
                // Optional: if you want tab icons to reflect user context before invoice loads
                RefreshTabIcons();
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "LoadUserContextAsync failed during OnInitializedAsync");
            }
        }

        // Add this public method to InvoiceView so child can request a refresh.
        // Place near other helpers in the partial class.

        public async Task RefreshAsync(InvoiceDto? refreshedInvoice)
        {
            try
            {
                if (refreshedInvoice != null)
                {
                    _invoiceDto = refreshedInvoice;
                }
                else if (InvoiceGuid != Guid.Empty)
                {
                    _invoiceDto = await InvoiceRepository.GetInvoiceById(InvoiceGuid) ?? new InvoiceDto();
                }

                // Refresh dependent data (PO, assignment, role flags) if needed
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
                        Logger.LogWarning(ex, "Failed to reload purchase order during refresh for invoice {InvoiceId}", InvoiceGuid);
                    }
                }

                // Re-evaluate assignment/role state (best-effort, keeps UI consistent)
                if (PurchaseOrderDetails != null && _employeeId.HasValue)
                {
                    var empInitiatorPermission = await invoiceApproverRepository.IsInvoiceApproverAsync(PurchaseOrderDetails.ProjectId, _employeeId.Value);
                    _CurrentRoleName = empInitiatorPermission.AssignedType ?? string.Empty;
                    _isInvAssigned = empInitiatorPermission.IsAssigned;
                }
                else
                {
                    _CurrentRoleName = string.Empty;
                    _isInvAssigned = false;
                }

                _isInitiator = await PurchaseOrderRepository.IsEmployeeAssignedforRoleAsync(
                    _invoiceDto.PurchaseOrderId != Guid.Empty ? _invoiceDto.PurchaseOrderId : Guid.Empty,
                    _employeeId ?? Guid.Empty,
                    "Initiator"
                );
                _isChecker = await PurchaseOrderRepository.IsEmployeeAssignedforRoleAsync(
                    _invoiceDto.PurchaseOrderId != Guid.Empty ? _invoiceDto.PurchaseOrderId : Guid.Empty,
                    _employeeId ?? Guid.Empty,
                    "Checker"
                );
                _isValidator = await PurchaseOrderRepository.IsEmployeeAssignedforRoleAsync(
                    _invoiceDto.PurchaseOrderId != Guid.Empty ? _invoiceDto.PurchaseOrderId : Guid.Empty,
                    _employeeId ?? Guid.Empty,
                    "Validator"
                );
                _isApprover = await PurchaseOrderRepository.IsEmployeeAssignedforRoleAsync(
                    _invoiceDto.PurchaseOrderId != Guid.Empty ? _invoiceDto.PurchaseOrderId : Guid.Empty,
                    _employeeId ?? Guid.Empty,
                    "Approver"
                );

                // recompute tab icons and UI
                RefreshTabIcons();
                StateHasChanged();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error refreshing invoice view after child save");
            }
        }
    }
}
