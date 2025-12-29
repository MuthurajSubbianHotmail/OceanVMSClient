using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using MudBlazor;
using OceanVMSClient.HttpRepoInterface.InvoiceModule;
using Shared.DTO.POModule;
using System;
using System.Threading.Tasks;

namespace OceanVMSClient.Pages.InviceModule
{
    public partial class InvoiceInitiatorReview
    {
        // Component parameters (inputs)
        [Parameter] public InvoiceDto? _invoiceDto { get; set; }
        [Parameter] public PurchaseOrderDto? _PODto { get; set; }
        
        // EventCallback to notify parent that the review was saved
        [Parameter] public EventCallback<InvoiceDto?> OnSaved { get; set; }

        // Internal DTO used when completing Initiator review
        [Parameter] public InvInitiatorReviewCompleteDto _InitiatorCompleteDto { get; set; } = new();

        // repository + UI feedback + logger
        [Inject] private IInvoiceRepository InvoiceRepository { get; set; } = default!;
        [Inject] private ISnackbar Snackbar { get; set; } = default!;
        [Inject] private ILogger<InvoiceInitiatorReview> Logger { get; set; } = default!;

       

        // Logged-in user context (supplied by parent)
        [Parameter] public Guid _LoggedInEmployeeID { get; set; } = Guid.Empty;
        [Parameter] public string _LoggedInUserType { get; set; } = string.Empty;
        [Parameter] public Guid _LoggedInVendorID { get; set; } = Guid.Empty;
        [Parameter] public string _CurrentRoleName { get; set; } = string.Empty;
        [Parameter] public bool _isInvAssigned { get; set; } = false;
        [Parameter] public bool _isInitiator { get; set; } = false;

        // small state
        private bool _initiatorSaved = false;

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
            if (_editContext == null || _editContext.Model != _InitiatorCompleteDto)
                _editContext = new EditContext(_InitiatorCompleteDto);

            // Apply defaults if CheckerReviewStatus == "Pending"
            EnsureDefaultInitiatorAmounts();

            // If server already indicates review completed, lock UI
            _initiatorSaved = IsInitiatorReviewCompleted();
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
            if (_invoiceDto == null || _InitiatorCompleteDto == null)
                return;

            // populate only when the working DTO is empty (to avoid overwriting user edits)
            if (_InitiatorCompleteDto.InvoiceId == Guid.Empty || _InitiatorCompleteDto.InvoiceId != _invoiceDto.Id)
            {
                _InitiatorCompleteDto.InvoiceId = _invoiceDto.Id;
                _InitiatorCompleteDto.InitiatorReviewerID = _invoiceDto.InitiatorReviewerID ?? Guid.Empty;
                _InitiatorCompleteDto.InitiatorApprovedAmount = _invoiceDto.InitiatorApprovedAmount;
                _InitiatorCompleteDto.InitiatorWithheldAmount = _invoiceDto.InitiatorWithheldAmount;
                _InitiatorCompleteDto.InitiatorWithheldReason = _invoiceDto.InitiatorWithheldReason;
                _InitiatorCompleteDto.InitiatorReviewComment = _invoiceDto.InitiatorReviewComment;
                _InitiatorCompleteDto.InitiatorReviewStatus = _invoiceDto.InitiatorReviewStatus;
            }
        }

        /// <summary>
        /// If CheckerReviewStatus is "Pending" set defaults:
        ///  - CheckerApprovedAmount = InvoiceTotalValue
        ///  - CheckerWithheldAmount = 0
        /// </summary>
        /// 
        private void EnsureDefaultInitiatorAmounts()
        {
            if (_invoiceDto == null || _InitiatorCompleteDto == null)
                return;

            if (string.Equals(_invoiceDto.InitiatorReviewStatus, "Pending", StringComparison.OrdinalIgnoreCase))
            {
                var total = _invoiceDto.InvoiceTotalValue;
                // set defaults only when amounts are null
                if (!_InitiatorCompleteDto.InitiatorApprovedAmount.HasValue)
                    _InitiatorCompleteDto.InitiatorApprovedAmount = total;
                if (!_InitiatorCompleteDto.InitiatorWithheldAmount.HasValue)
                    _InitiatorCompleteDto.InitiatorWithheldAmount = 0m;
            }
        }

        private void OnInitiatorApprovedAmountChanged(decimal? newValue)
        {
            // Prevent programmatic/user changes when not allowed
            if (!CanEditApprovedAmount())
                return;

            if (_invoiceDto == null || _InitiatorCompleteDto == null)
                return;

            decimal total = _invoiceDto.InvoiceTotalValue;
            decimal approved = newValue.GetValueOrDefault(0m);

            if (approved < 0m) approved = 0m;
            if (approved > total) approved = total;

            _InitiatorCompleteDto.InitiatorApprovedAmount = approved;
            _InitiatorCompleteDto.InitiatorWithheldAmount = total - approved;

            StateHasChanged();
        }

        private Task OnSupportingFileUploaded(string? url)
        {
            _InitiatorCompleteDto.InitiatorReviewAttachment = url ?? string.Empty;
            return Task.CompletedTask;
        }
        #endregion

        #region Permissions / read-only
        private bool IsInitiatorReviewRequired => _invoiceDto?.IsInitiatorReviewRequired ?? false;

        private bool CanEditApprovedAmount()
        {
            // must be initiator
            if (!_isInitiator)
                return false;

            // must be assigned to this invoice
            if (!_isInvAssigned)
                return false;

            // role must indicate Initiator
            if (string.IsNullOrWhiteSpace(_CurrentRoleName) || !_CurrentRoleName.Contains("Initiator", StringComparison.OrdinalIgnoreCase))
                return false;

            // invoice must be in "With Initiator" status
            var invStatus = _invoiceDto?.InvoiceStatus?.Trim();
            if (!string.Equals(invStatus, "With Initiator", StringComparison.OrdinalIgnoreCase))
                return false;

            // if server indicates review already completed, disallow edit
            if (IsInitiatorReviewCompleted())
                return false;

            return true;
        }

        private bool IsInitiatorReviewCompleted()
        {
            var status = _invoiceDto?.InitiatorReviewStatus?.Trim();
            if (string.IsNullOrWhiteSpace(status))
                return false;

            return status.Equals("Approved", StringComparison.OrdinalIgnoreCase)
                || status.Equals("Rejected", StringComparison.OrdinalIgnoreCase)
                || status.Equals("Completed", StringComparison.OrdinalIgnoreCase);
        }

        // UI read-only helper used by markup
        private bool IsReadOnly => _initiatorSaved || IsInitiatorReviewCompleted();
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

                var status = _InitiatorCompleteDto.InitiatorReviewStatus?.Trim();
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
                    if (!_InitiatorCompleteDto.InitiatorApprovedAmount.HasValue)
                    {
                        Snackbar.Add("Approved amount is required when status is Approved.", Severity.Warning);
                        return;
                    }

                    var approvedValue = _InitiatorCompleteDto.InitiatorApprovedAmount.GetValueOrDefault(0m);
                    if (approvedValue <= 0m)
                    {
                        Snackbar.Add("Approved amount must be greater than zero.", Severity.Warning);
                        return;
                    }

                    if (approvedValue > total)
                    {
                        Snackbar.Add("Approved amount cannot exceed invoice total.", Severity.Warning);
                        // clamp and show recalculation
                        _InitiatorCompleteDto.InitiatorApprovedAmount = total;
                        _InitiatorCompleteDto.InitiatorWithheldAmount = 0m;
                        StateHasChanged();
                        return;
                    }

                    // recalc withheld
                    _InitiatorCompleteDto.InitiatorWithheldAmount = total - approvedValue;
                }
                else // Rejected
                {
                    // Remarks / comment required on rejection
                    if (string.IsNullOrWhiteSpace(_InitiatorCompleteDto.InitiatorReviewComment))
                    {
                        Snackbar.Add("Remarks are required when status is Rejected.", Severity.Warning);
                        return;
                    }

                    // When rejected, approved amount should be zero and withheld equals total
                    _InitiatorCompleteDto.InitiatorApprovedAmount = 0m;
                    _InitiatorCompleteDto.InitiatorWithheldAmount = total;
                }

                // Ensure invoice id is set
                _InitiatorCompleteDto.InvoiceId = _invoiceDto.Id;

                // If ReviewerID not provided, set from cascading employee id if available
                if (_InitiatorCompleteDto.InitiatorReviewerID == Guid.Empty && _LoggedInEmployeeID != Guid.Empty)
                    _InitiatorCompleteDto.InitiatorReviewerID = _LoggedInEmployeeID;

                // Persist via repository (returns updated InvoiceDto)
                var refreshed = await InvoiceRepository.UpdateInvoiceInitiatorReview(_InitiatorCompleteDto);
                if (refreshed != null)
                {
                    _invoiceDto = refreshed;
                    MapFromInvoiceDto();
                }

                // Lock UI
                _initiatorSaved = true;

                Snackbar.Add("Initiator review saved successfully.", Severity.Success);

                // Notify parent via EventCallback so parent can refresh data
                if (OnSaved.HasDelegate)
                    await OnSaved.InvokeAsync(_invoiceDto);

                // legacy / optional handler support (keeps previous behavior if used)
                if (this is IInvoiceCheckerReviewHandlers handlers)
                    await handlers.SaveAsync();

                StateHasChanged();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error saving Initiator review for Invoice ID {InvoiceId}", _invoiceDto?.Id);
                Snackbar.Add("An error occurred while saving the Initiator review.", Severity.Error);
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

