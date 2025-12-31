using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using MudBlazor;
using OceanVMSClient.HttpRepoInterface.InvoiceModule;
using Shared.DTO.POModule;
using System;
using System.Threading.Tasks;

namespace OceanVMSClient.Pages.InviceModule
{
    public partial class InvoiceCheckerReview
    {
        


        // Component parameters (inputs)
        [Parameter] public InvoiceDto? _invoiceDto { get; set; }
        [Parameter] public PurchaseOrderDto? _PODto { get; set; }

        // EventCallback to notify parent that the review was saved
        [Parameter] public EventCallback<InvoiceDto?> OnSaved { get; set; }

        // Internal DTO used when completing checker review
        [Parameter] public InvCheckerReviewCompleteDto _CheckercompleteDto { get; set; } = new();

        // repository + UI feedback + logger
        [Inject] private IInvoiceRepository InvoiceRepository { get; set; } = default!;
        [Inject] private ISnackbar Snackbar { get; set; } = default!;
        [Inject] private ILogger<InvoiceCheckerReview> Logger { get; set; } = default!;

        // Cascading parameters (UI theme / user context)
        //[CascadingParameter] public Margin _margin { get; set; } = Margin.Dense;
        //[CascadingParameter] public Variant _variant { get; set; } = Variant.Text;
        //[CascadingParameter] public Color _labelColor { get; set; } = Color.Default;
        [CascadingParameter] public Color _valueColor { get; set; } = Color.Default;
        [CascadingParameter] public Typo _labelTypo { get; set; } = Typo.subtitle2;
        [CascadingParameter] public Typo _valueTypo { get; set; } = Typo.body2;

        // Logged-in user context (supplied by parent)
        [Parameter] public Guid _LoggedInEmployeeID { get; set; } = Guid.Empty;
        [Parameter] public string _LoggedInUserType { get; set; } = string.Empty;
        [Parameter] public Guid _LoggedInVendorID { get; set; } = Guid.Empty;
        [Parameter] public string _CurrentRoleName { get; set; } = string.Empty;
        [Parameter] public bool _isInvAssigned { get; set; } = false;
        [Parameter] public bool _isChecker { get; set; } = false;

        // small state
        private bool _checkerSaved = false;

        // EditContext used for client-side DataAnnotations validation
        private EditContext? _editContext;

        #region Display helpers (computed properties)
        private string FormatCurrency(decimal? value) => value?.ToString("C") ?? "-";
        private string PoValueText => _PODto != null ? _PODto.ItemValue.ToString("N2") : "0.00";
        private string PoTaxText => _PODto != null ? _PODto.GSTTotal.ToString("N2") : "0.00";
        private string PoTotalText => _PODto != null ? _PODto.TotalValue.ToString("N2") : "0.00";
        private string PrevInvoiceCountText => _PODto?.PreviousInvoiceCount?.ToString() ?? "0";
        private string PrevInvoiceValueText => _PODto != null && _PODto.PreviousInvoiceValue.HasValue
            ? _PODto.PreviousInvoiceValue.Value.ToString("N2")
            : "0.00";
        private string InvoiceBalanceValueText => _PODto != null && _PODto.InvoiceBalanceValue.HasValue
            ? _PODto.InvoiceBalanceValue.Value.ToString("N2")
            : "0.00";
        #endregion

        #region Lifecycle
        protected override void OnParametersSet()
        {
            base.OnParametersSet();

            // Map server invoice values into working DTO when component opens
            MapFromInvoiceDto();

            // ensure EditContext tracks current working DTO so DataAnnotationsValidator works
            if (_editContext == null || _editContext.Model != _CheckercompleteDto)
                _editContext = new EditContext(_CheckercompleteDto);

            // Apply defaults if CheckerReviewStatus == "Pending"
            EnsureDefaultCheckerAmounts();

            // If server already indicates review completed, lock UI
            _checkerSaved = IsCheckerReviewCompleted();
        }
        #endregion

        #region UI helpers
        private Color GetSLAStatusColor(string? slaStatus) =>
            slaStatus switch
            {
                null => Color.Default,
                var s when s.Equals("Delayed", StringComparison.OrdinalIgnoreCase) => Color.Warning,
                var s when s.Equals("NA", StringComparison.OrdinalIgnoreCase) => Color.Default,
                var s when s.Equals("Within SLA", StringComparison.OrdinalIgnoreCase) => Color.Success,
                _ => Color.Info
            };
        private Color GetSLAColor(string? slaStatus) => GetSLAStatusColor(slaStatus);
        #endregion

        #region Validation / defaults / mapping
        private void MapFromInvoiceDto()
        {
            if (_invoiceDto == null || _CheckercompleteDto == null)
                return;

            // populate only when the working DTO is empty (to avoid overwriting user edits)
            if (_CheckercompleteDto.InvoiceId == Guid.Empty || _CheckercompleteDto.InvoiceId != _invoiceDto.Id)
            {
                _CheckercompleteDto.InvoiceId = _invoiceDto.Id;
                _CheckercompleteDto.CheckerID = _invoiceDto.CheckerID ?? Guid.Empty;
                _CheckercompleteDto.CheckerApprovedAmount = _invoiceDto.CheckerApprovedAmount;
                _CheckercompleteDto.CheckerWithheldAmount = _invoiceDto.CheckerWithheldAmount;
                _CheckercompleteDto.CheckerWithheldReason = _invoiceDto.CheckerWithheldReason;
                _CheckercompleteDto.CheckerReviewComment = _invoiceDto.CheckerReviewComment;
                _CheckercompleteDto.CheckerReviewStatus = _invoiceDto.CheckerReviewStatus;
            }
        }

        /// <summary>
        /// If CheckerReviewStatus is "Pending" set defaults:
        ///  - CheckerApprovedAmount = InvoiceTotalValue
        ///  - CheckerWithheldAmount = 0
        /// </summary>
        /// 
        private void EnsureDefaultCheckerAmounts()
        {
            if (_invoiceDto == null || _CheckercompleteDto == null)
                return;

            if (string.Equals(_invoiceDto.CheckerReviewStatus, "Pending", StringComparison.OrdinalIgnoreCase))
            {
                var total = _invoiceDto.InvoiceTotalValue;
                // set defaults only when amounts are null
                if (!_CheckercompleteDto.CheckerApprovedAmount.HasValue)
                    _CheckercompleteDto.CheckerApprovedAmount = total;
                if (!_CheckercompleteDto.CheckerWithheldAmount.HasValue)
                    _CheckercompleteDto.CheckerWithheldAmount = 0m;
            }
        }

        private void OnCheckerApprovedAmountChanged(decimal? newValue)
        {
            // Prevent programmatic/user changes when not allowed
            if (!CanEditApprovedAmount())
                return;

            if (_invoiceDto == null || _CheckercompleteDto == null)
                return;

            decimal total = _invoiceDto.InvoiceTotalValue;
            decimal approved = newValue.GetValueOrDefault(0m);

            if (approved < 0m) approved = 0m;
            if (approved > total) approved = total;

            _CheckercompleteDto.CheckerApprovedAmount = approved;
            _CheckercompleteDto.CheckerWithheldAmount = total - approved;

            StateHasChanged();
        }

        private Task OnSupportingFileUploaded(string? url)
        {
            _CheckercompleteDto.CheckerReviewAttachment = url ?? string.Empty;
            return Task.CompletedTask;
        }
        #endregion

        #region Permissions / read-only
        private bool IsCheckerReviewRequired => _invoiceDto?.IsCheckerReviewRequired ?? false;

        private bool CanEditApprovedAmount()
        {
            // must be checker
            if (!_isChecker)
                return false;

            // must be assigned to this invoice
            if (!_isInvAssigned)
                return false;

            // role must indicate Checker
            if (string.IsNullOrWhiteSpace(_CurrentRoleName) || !_CurrentRoleName.Contains("Checker", StringComparison.OrdinalIgnoreCase))
                return false;

            // invoice must be in "With Checker" status
            var invStatus = _invoiceDto?.InvoiceStatus?.Trim();
            if (!string.Equals(invStatus, "With Checker", StringComparison.OrdinalIgnoreCase))
                return false;

            // if server indicates review already completed, disallow edit
            if (IsCheckerReviewCompleted())
                return false;

            return true;
        }

        private bool IsCheckerReviewCompleted()
        {
            var status = _invoiceDto?.CheckerReviewStatus?.Trim();
            if (string.IsNullOrWhiteSpace(status))
                return false;

            return status.Equals("Approved", StringComparison.OrdinalIgnoreCase)
                || status.Equals("Rejected", StringComparison.OrdinalIgnoreCase)
                || status.Equals("Completed", StringComparison.OrdinalIgnoreCase);
        }

        // UI read-only helper used by markup
        private bool IsReadOnly => _checkerSaved || IsCheckerReviewCompleted();
        #endregion

        #region Actions
        private async Task SaveDecision()
        {
            try
            {
                if (_invoiceDto == null)
                {
                    Snackbar.Add("Invoice not loaded.", Severity.Error);
                    return;
                }

                var status = _CheckercompleteDto.CheckerReviewStatus?.Trim();
                if (string.IsNullOrWhiteSpace(status) ||
                    !(status.Equals("Approved", StringComparison.OrdinalIgnoreCase) || status.Equals("Rejected", StringComparison.OrdinalIgnoreCase)))
                {
                    Snackbar.Add("Please select Approval status (Approved or Rejected).", Severity.Warning);
                    return;
                }

                // Conditional validation
                decimal total = _invoiceDto.InvoiceTotalValue;

                if (status.Equals("Approved", StringComparison.OrdinalIgnoreCase))
                {
                    // Approved amount is required and must be > 0 and <= total
                    if (!_CheckercompleteDto.CheckerApprovedAmount.HasValue)
                    {
                        Snackbar.Add("Approved amount is required when status is Approved.", Severity.Warning);
                        return;
                    }

                    var approvedValue = _CheckercompleteDto.CheckerApprovedAmount.GetValueOrDefault(0m);
                    if (approvedValue <= 0m)
                    {
                        Snackbar.Add("Approved amount must be greater than zero.", Severity.Warning);
                        return;
                    }

                    if (approvedValue > total)
                    {
                        Snackbar.Add("Approved amount cannot exceed invoice total.", Severity.Warning);
                        // clamp and show recalculation
                        _CheckercompleteDto.CheckerApprovedAmount = total;
                        _CheckercompleteDto.CheckerWithheldAmount = 0m;
                        StateHasChanged();
                        return;
                    }

                    // recalc withheld
                    _CheckercompleteDto.CheckerWithheldAmount = total - approvedValue;
                }
                else // Rejected
                {
                    // Remarks / comment required on rejection
                    if (string.IsNullOrWhiteSpace(_CheckercompleteDto.CheckerReviewComment))
                    {
                        Snackbar.Add("Remarks are required when status is Rejected.", Severity.Warning);
                        return;
                    }

                    // When rejected, approved amount should be zero and withheld equals total
                    _CheckercompleteDto.CheckerApprovedAmount = 0m;
                    _CheckercompleteDto.CheckerWithheldAmount = total;
                }

                // Ensure invoice id is set
                _CheckercompleteDto.InvoiceId = _invoiceDto.Id;

                // If CheckerID not provided, set from cascading employee id if available
                if (_CheckercompleteDto.CheckerID == Guid.Empty && _LoggedInEmployeeID != Guid.Empty)
                    _CheckercompleteDto.CheckerID = _LoggedInEmployeeID;

                // Persist via repository (returns updated InvoiceDto)
                var refreshed = await InvoiceRepository.UpdateInvoiceCheckerReview(_CheckercompleteDto);
                if (refreshed != null)
                {
                    // Re-fetch full invoice from server to ensure all display fields (like CheckerName) are populated
                    try
                    {
                        var full = await InvoiceRepository.GetInvoiceById(refreshed.Id);
                        _invoiceDto = full ?? refreshed;
                    }
                    catch (Exception ex)
                    {
                        // If re-fetch fails, fall back to the update response
                        Logger.LogWarning(ex, "Failed to re-fetch invoice after checker update; using response object.");
                        _invoiceDto = refreshed;
                    }

                    MapFromInvoiceDto();
                }

                // Lock UI
                _checkerSaved = true;

                Snackbar.Add("Checker review saved successfully.", Severity.Success);

                // Notify parent via EventCallback so parent can refresh data
                if (OnSaved.HasDelegate)
                    await OnSaved.InvokeAsync(_invoiceDto);

                if (this is IInvoiceCheckerReviewHandlers handlers)
                    await handlers.SaveAsync();

                StateHasChanged();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error saving checker review for Invoice ID {InvoiceId}", _invoiceDto?.Id);
                Snackbar.Add("An error occurred while saving the checker review.", Severity.Error);
            }
        }

        private Task Cancel()
        {
            if (this is IInvoiceCheckerReviewHandlers handlers)
                return handlers.CancelAsync();

            return Task.CompletedTask;
        }

        private interface IInvoiceCheckerReviewHandlers
        {
            Task SaveAsync();
            Task CancelAsync();
        }
        #endregion
    }
}