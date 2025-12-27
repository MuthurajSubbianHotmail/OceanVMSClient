using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using MudBlazor;
using OceanVMSClient.HttpRepoInterface.InvoiceModule;
using Shared.DTO.POModule;
using System;
using System.Threading.Tasks;
namespace OceanVMSClient.Pages.InviceModule
{
    public partial class InvoiceValidatorReview
    {
        // Component parameters (inputs)
        [Parameter] public InvoiceDto? _invoiceDto { get; set; }
        [Parameter] public PurchaseOrderDto? _PODto { get; set; }

        // Internal DTO used when completing Validator review
        [Parameter] public InvValidatorReviewCompleteDto _validatorCompleteDto { get; set; } = new();

        // repository + UI feedback + logger
        [Inject] private IInvoiceRepository InvoiceRepository { get; set; } = default!;
        [Inject] private ISnackbar Snackbar { get; set; } = default!;
        [Inject] private ILogger<InvoiceValidatorReview> Logger { get; set; } = default!;

        // Cascading parameters (UI theme / user context)
        [CascadingParameter] public Margin _margin { get; set; } = Margin.Dense;
        [CascadingParameter] public Variant _variant { get; set; } = Variant.Text;
        [CascadingParameter] public Color _labelColor { get; set; } = Color.Default;
        [CascadingParameter] public Color _valueColor { get; set; } = Color.Default;
        [CascadingParameter] public Typo _labelTypo { get; set; } = Typo.subtitle2;
        [CascadingParameter] public Typo _valueTypo { get; set; } = Typo.body2;

        // Logged-in user context (supplied by parent)
        [Parameter] public Guid _LoggedInEmployeeID { get; set; } = Guid.Empty;
        [Parameter] public string _LoggedInUserType { get; set; } = string.Empty;
        [Parameter] public Guid _LoggedInVendorID { get; set; } = Guid.Empty;
        [Parameter] public string _CurrentRoleName { get; set; } = string.Empty;
        [Parameter] public bool _isInvAssigned { get; set; } = false;
        [Parameter] public bool _IsValidator { get; set; } = false;

        // small state
        private bool _validatorSaved = false;

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
            if (_editContext == null || _editContext.Model != _validatorCompleteDto)
                _editContext = new EditContext(_validatorCompleteDto);

            // Apply defaults if CheckerReviewStatus == "Pending"
            EnsureDefaultValidatorAmounts();

            // If server already indicates review completed, lock UI
            _validatorSaved = IsValidationReviewCompleted();
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
            if (_invoiceDto == null || _validatorCompleteDto == null)
                return;

            // populate only when the working DTO is empty (to avoid overwriting user edits)
            if (_validatorCompleteDto.InvoiceId == Guid.Empty || _validatorCompleteDto.InvoiceId != _invoiceDto.Id)
            {
                _validatorCompleteDto.InvoiceId = _invoiceDto.Id;
                _validatorCompleteDto.ValidatorID = _invoiceDto.ValidatorID ?? Guid.Empty;
                _validatorCompleteDto.ValidatorApprovedAmount = _invoiceDto.ValidatorApprovedAmount;
                _validatorCompleteDto.ValidatorWithheldAmount = _invoiceDto.ValidatorWithheldAmount;
                _validatorCompleteDto.ValidatorWithheldReason = _invoiceDto.ValidatorWithheldReason;
                _validatorCompleteDto.ValidatorReviewComment = _invoiceDto.ValidatorReviewComment;
                _validatorCompleteDto.ValidatorReviewStatus = _invoiceDto.ValidatorReviewStatus;
            }
        }

        /// <summary>
        /// If CheckerReviewStatus is "Pending" set defaults:
        ///  - CheckerApprovedAmount = InvoiceTotalValue
        ///  - CheckerWithheldAmount = 0
        /// </summary>
        /// 
        private void EnsureDefaultValidatorAmounts()
        {
            if (_invoiceDto == null || _validatorCompleteDto == null)
                return;

            if (string.Equals(_invoiceDto.ValidatorReviewStatus, "Pending", StringComparison.OrdinalIgnoreCase))
            {
                var total = _invoiceDto.InvoiceTotalValue;
                // set defaults only when amounts are null
                if (!_validatorCompleteDto.ValidatorApprovedAmount.HasValue)
                    _validatorCompleteDto.ValidatorApprovedAmount = total;
                if (!_validatorCompleteDto.ValidatorWithheldAmount.HasValue)
                    _validatorCompleteDto.ValidatorWithheldAmount = 0m;
            }
        }

        private void OnValidatorApprovedAmountChanged(decimal? newValue)
        {
            if (_invoiceDto == null || _validatorCompleteDto == null)
                return;

            decimal total = _invoiceDto.InvoiceTotalValue;
            decimal approved = newValue.GetValueOrDefault(0m);

            if (approved < 0m) approved = 0m;
            if (approved > total) approved = total;

            _validatorCompleteDto.ValidatorApprovedAmount = approved;
            _validatorCompleteDto.ValidatorWithheldAmount = total - approved;

            StateHasChanged();
        }
        #endregion

        #region Permissions / read-only
        private bool IsInitiatorReviewRequired => _invoiceDto?.IsInitiatorReviewRequired ?? false;

        private bool CanEditApprovedAmount()
        {
            // simple checks - adapt as needed (role/assignment)
            if (!_IsValidator)
                return false;
            if (!_isInvAssigned)
                return false;

            if (string.IsNullOrWhiteSpace(_CurrentRoleName) || !_CurrentRoleName.Contains("Validator", StringComparison.OrdinalIgnoreCase))
                return false;

            // If server status indicates completed, disallow edit
            if (IsValidationReviewCompleted())
                return false;

            return true;
        }

        private bool IsValidationReviewCompleted()
        {
            var status = _invoiceDto?.ValidatorReviewStatus?.Trim();
            if (string.IsNullOrWhiteSpace(status))
                return false;

            return status.Equals("Approved", StringComparison.OrdinalIgnoreCase)
                || status.Equals("Rejected", StringComparison.OrdinalIgnoreCase)
                || status.Equals("Completed", StringComparison.OrdinalIgnoreCase);
        }

        // UI read-only helper used by markup
        private bool IsReadOnly => _validatorSaved || IsValidationReviewCompleted();
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

                var status = _validatorCompleteDto.ValidatorReviewStatus?.Trim();
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
                    if (!_validatorCompleteDto.ValidatorApprovedAmount.HasValue)
                    {
                        Snackbar.Add("Approved amount is required when status is Approved.", Severity.Warning);
                        return;
                    }

                    var approvedValue = _validatorCompleteDto.ValidatorApprovedAmount.GetValueOrDefault(0m);
                    if (approvedValue <= 0m)
                    {
                        Snackbar.Add("Approved amount must be greater than zero.", Severity.Warning);
                        return;
                    }

                    if (approvedValue > total)
                    {
                        Snackbar.Add("Approved amount cannot exceed invoice total.", Severity.Warning);
                        // clamp and show recalculation
                        _validatorCompleteDto.ValidatorApprovedAmount = total;
                        _validatorCompleteDto.ValidatorWithheldAmount = 0m;
                        StateHasChanged();
                        return;
                    }

                    // recalc withheld
                    _validatorCompleteDto.ValidatorWithheldAmount = total - approvedValue;
                }
                else // Rejected
                {
                    // Remarks / comment required on rejection
                    if (string.IsNullOrWhiteSpace(_validatorCompleteDto.ValidatorReviewComment))
                    {
                        Snackbar.Add("Remarks are required when status is Rejected.", Severity.Warning);
                        return;
                    }

                    // When rejected, approved amount should be zero and withheld equals total
                    _validatorCompleteDto.ValidatorApprovedAmount    = 0m;
                    _validatorCompleteDto.ValidatorWithheldAmount = total;
                }

                // Ensure invoice id is set
                _validatorCompleteDto.InvoiceId = _invoiceDto.Id;

                // If ValidatorID not provided, set from cascading employee id if available
                if (_validatorCompleteDto.ValidatorID == Guid.Empty && _LoggedInEmployeeID != Guid.Empty)
                    _validatorCompleteDto.ValidatorID = _LoggedInEmployeeID;

                // Persist via repository (returns updated InvoiceDto)
                var refreshed = await InvoiceRepository.UpdateInvoiceValidatorApproval(_validatorCompleteDto);
                if (refreshed != null)
                {
                    _invoiceDto = refreshed;
                    MapFromInvoiceDto();
                }

                // Lock UI
                _validatorSaved = true;

                Snackbar.Add("Validator review saved successfully.", Severity.Success);

                if (this is IInvoiceValidatorReviewHandlers handlers)
                    await handlers.SaveAsync();

                StateHasChanged();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error saving Validator review for Invoice ID {InvoiceId}", _invoiceDto?.Id);
                Snackbar.Add("An error occurred while saving the Validator review.", Severity.Error);
            }
        }

        private Task Cancel()
        {
            if (this is IInvoiceValidatorReviewHandlers handlers)
                return handlers.CancelAsync();

            return Task.CompletedTask;
        }

        private interface IInvoiceValidatorReviewHandlers
        {
            Task SaveAsync();
            Task CancelAsync();
        }
        #endregion
    }
}
