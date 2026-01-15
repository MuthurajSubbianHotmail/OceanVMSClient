using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using OceanVMSClient.HttpRepoInterface.InvoiceModule;
using Shared.DTO.POModule;
using OceanVMSClient.Helpers;

namespace OceanVMSClient.Pages.InviceModule
{
    public partial class InvoiceAPReview
    {
        // Component parameters (inputs)
        [Parameter] public InvoiceDto? _invoiceDto { get; set; }
        [Parameter] public PurchaseOrderDto? _PODto { get; set; }

        // EventCallback to notify parent that the review was saved
        [Parameter] public EventCallback<InvoiceDto?> OnSaved { get; set; }

        // Internal DTO used when completing approver review
        [Parameter] public InvAPApproverReviewCompleteDto _apApproverCompleteDto { get; set; } = new();

        // repository + UI feedback + logger
        [Inject] private IInvoiceRepository InvoiceRepository { get; set; } = default!;
        [Inject] private ISnackbar Snackbar { get; set; } = default!;
        [Inject] private ILogger<InvoiceAPReview> Logger { get; set; } = default!;

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
        [Parameter] public bool _isApApprover { get; set; } = false;
        private (Guid? PreviousReviewerId, string? PreviousReviewerName, decimal? PrevApprovedAmount, decimal? PrevWithheldAmount) _previousReviewInfo = (null, null, null, null);

        // small state
        private bool _apApproverSaved = false;

        // store originals so we can show "only changed" values
        private decimal? _originalAPApprovedAmount;
        private decimal? _originalAPWithheldAmount;
        private string? _originalAPReviewComments;
        private string? _originalAPReviewStatus;

        // EditContext used for client-side DataAnnotations validation
        private EditContext? _editContext;
        private ValidationMessageStore? _messageStore;
        private bool _editContextSubscribed = false;

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

            // load previous review info first (used when applying approved/withheld defaults)
            _previousReviewInfo = InvoiceReviewHelpers.GetPreviousReviewInfo(_invoiceDto, "ap approver");

            // Map server invoice values into working DTO when component opens
            MapFromInvoiceDto();

            // ensure EditContext tracks current working DTO so DataAnnotationsValidator works
            if (_editContext == null || _editContext.Model != _apApproverCompleteDto)
            {
                _editContext = new EditContext(_apApproverCompleteDto);
                _messageStore = new ValidationMessageStore(_editContext);
                _editContextSubscribed = false;
            }

            // subscribe editContext events once
            if (_editContext != null && !_editContextSubscribed)
            {
                // validate on form submit
                _editContext.OnValidationRequested += (sender, args) => ValidateWithheldReason();

                // DO NOT validate on every field change — only on blur or submit per request
                _editContextSubscribed = true;
            }

            // Apply defaults based on current APReviewStatus or invoice state
            EnsureDefaultApproverAmounts();

            // If server already indicates review completed, lock UI
            _apApproverSaved = IsAPApproverReviewCompleted();
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
            if (_invoiceDto == null || _apApproverCompleteDto == null)
                return;

            // populate only when the working DTO is empty (to avoid overwriting user edits)
            if (_apApproverCompleteDto.InvoiceId == Guid.Empty || _apApproverCompleteDto.InvoiceId != _invoiceDto.Id)
            {
                _apApproverCompleteDto.InvoiceId = _invoiceDto.Id;
                _apApproverCompleteDto.APReviewerId = _invoiceDto.APReviewerId ?? Guid.Empty;
                _apApproverCompleteDto.APApprovedAmount = _invoiceDto.APApprovedAmount;
                _apApproverCompleteDto.APWithheldAmount = _invoiceDto.APWithheldAmount;
                _apApproverCompleteDto.APWithheldReason = _invoiceDto.APWithheldReason;
                _apApproverCompleteDto.APReviewComments = _invoiceDto.APReviewComments;
                _apApproverCompleteDto.APReviewStatus = _invoiceDto.APReviewStatus;

                // capture originals for "show only changes"
                _originalAPApprovedAmount = _invoiceDto.APApprovedAmount;
                _originalAPWithheldAmount = _invoiceDto.APWithheldAmount;
                _originalAPReviewComments = _invoiceDto.APReviewComments;
                _originalAPReviewStatus = _invoiceDto.APReviewStatus;
            }
        }

        /// <summary>
        /// Apply defaults based on the AP review status and available previous review info.
        /// Rules implemented:
        /// - Pending -> approved = 0, withheld = 0
        /// - Rejected -> approved = 0, withheld = 0
        /// - Approved -> if previous approved/withheld exist use them; otherwise approved = invoice total, withheld = 0
        /// Only set defaults when the working DTO amounts are null (do not overwrite user edits).
        /// </summary>
        private void EnsureDefaultApproverAmounts()
        {
            if (_invoiceDto == null || _apApproverCompleteDto == null)
                return;

            var status = _apApproverCompleteDto.APReviewStatus?.Trim();
            if (string.IsNullOrWhiteSpace(status))
                return;

            var total = _invoiceDto.InvoiceTotalValue;

            if (status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
            {
                if (!_apApproverCompleteDto.APApprovedAmount.HasValue)
                    _apApproverCompleteDto.APApprovedAmount = 0m;
                if (!_apApproverCompleteDto.APWithheldAmount.HasValue)
                    _apApproverCompleteDto.APWithheldAmount = 0m;
            }
            else if (status.Equals("Rejected", StringComparison.OrdinalIgnoreCase))
            {
                if (!_apApproverCompleteDto.APApprovedAmount.HasValue)
                    _apApproverCompleteDto.APApprovedAmount = 0m;
                if (!_apApproverCompleteDto.APWithheldAmount.HasValue)
                    _apApproverCompleteDto.APWithheldAmount = 0m;
            }
            else if (status.Equals("Approved", StringComparison.OrdinalIgnoreCase))
            {
                if (!_apApproverCompleteDto.APApprovedAmount.HasValue)
                {
                    if (_previousReviewInfo.PrevApprovedAmount.HasValue)
                        _apApproverCompleteDto.APApprovedAmount = _previousReviewInfo.PrevApprovedAmount;
                    else
                        _apApproverCompleteDto.APApprovedAmount = total;
                }

                if (!_apApproverCompleteDto.APWithheldAmount.HasValue)
                {
                    if (_previousReviewInfo.PrevWithheldAmount.HasValue)
                        _apApproverCompleteDto.APWithheldAmount = _previousReviewInfo.PrevWithheldAmount;
                    else
                        _apApproverCompleteDto.APWithheldAmount = total - _apApproverCompleteDto.APApprovedAmount.GetValueOrDefault(0m);
                }
            }
        }

        private void OnApproverApprovedAmountChanged(decimal? newValue)
        {
            if (!CanEditApprovedAmount())
                return;

            if (_invoiceDto == null || _apApproverCompleteDto == null)
                return;

            decimal total = _invoiceDto.InvoiceTotalValue;
            decimal approved = newValue.GetValueOrDefault(0m);

            if (approved < 0m) approved = 0m;
            if (approved > total) approved = total;

            _apApproverCompleteDto.APApprovedAmount = approved;
            _apApproverCompleteDto.APWithheldAmount = total - approved;

            // Clear withheld-reason validation while user is changing amounts (message will show only on blur or submit)
            if (_messageStore != null)
            {
                var reasonField = new FieldIdentifier(_apApproverCompleteDto, nameof(_apApproverCompleteDto.APWithheldReason));
                _messageStore.Clear(reasonField);
                _editContext?.NotifyValidationStateChanged();
            }

            StateHasChanged();
        }

        /// <summary>
        /// Called when user changes the APReviewStatus dropdown -- updates amounts immediately per rules.
        /// Mirrors InvoiceApproverReview behavior and clears withheld reason messages while changing status.
        /// </summary>
        private void OnAPReviewStatusChanged(string? newStatus)
        {
            if (_apApproverCompleteDto == null)
                return;

            _apApproverCompleteDto.APReviewStatus = newStatus?.Trim();

            if (string.Equals(_apApproverCompleteDto.APReviewStatus, "Rejected", StringComparison.OrdinalIgnoreCase))
            {
                // Per requirement: both should be 0 when rejected
                _apApproverCompleteDto.APApprovedAmount = 0m;
                _apApproverCompleteDto.APWithheldAmount = 0m;
            }
            else if (string.Equals(_apApproverCompleteDto.APReviewStatus, "Approved", StringComparison.OrdinalIgnoreCase))
            {
                // Prefer previous review amounts when available, otherwise use invoice total
                if (_previousReviewInfo.PrevApprovedAmount.HasValue && _previousReviewInfo.PrevWithheldAmount.HasValue)
                {
                    _apApproverCompleteDto.APApprovedAmount = _previousReviewInfo.PrevApprovedAmount;
                    _apApproverCompleteDto.APWithheldAmount = _previousReviewInfo.PrevWithheldAmount;
                }
                else if (_invoiceDto != null)
                {
                    _apApproverCompleteDto.APApprovedAmount = _invoiceDto.InvoiceTotalValue;
                    _apApproverCompleteDto.APWithheldAmount = 0m;
                }
            }
            else
            {
                // Pending or other -> reset to 0
                _apApproverCompleteDto.APApprovedAmount = 0m;
                _apApproverCompleteDto.APWithheldAmount = 0m;
            }

            // Clear withheld-reason validation while changing status
            if (_messageStore != null)
            {
                var reasonField = new FieldIdentifier(_apApproverCompleteDto, nameof(_apApproverCompleteDto.APWithheldReason));
                _messageStore.Clear(reasonField);
                _editContext?.NotifyValidationStateChanged();
            }

            StateHasChanged();
        }

        private void OnAPWithheldReasonBlur(FocusEventArgs e)
        {
            ValidateWithheldReason();
        }

        /// <summary>
        /// Validate that APWithheldReason is present when APWithheldAmount != 0.
        /// Uses ValidationMessageStore so messages appear in ValidationSummary and next to fields.
        /// Only adds messages; clearing is done by callers when immediate hide is desired.
        /// </summary>
        private void ValidateWithheldReason()
        {
            if (_editContext == null || _messageStore == null || _apApproverCompleteDto == null)
                return;

            var reasonField = new FieldIdentifier(_apApproverCompleteDto, nameof(_apApproverCompleteDto.APWithheldReason));
            _messageStore.Clear(reasonField);

            var withheld = _apApproverCompleteDto.APWithheldAmount.GetValueOrDefault(0m);
            if (withheld != 0m && string.IsNullOrWhiteSpace(_apApproverCompleteDto.APWithheldReason))
            {
                _messageStore.Add(reasonField, "Withheld reason is required when Withheld Amount is not zero.");
            }

            _editContext.NotifyValidationStateChanged();
        }
        #endregion

        #region Change-detection helpers
        // When current user is Accounts Payable we show only changed fields in the UI.
        public bool ShowOnlyChanges => RoleHelper.IsAccountPayableRole(_CurrentRoleName);

        public bool APApprovedAmountChanged => _apApproverCompleteDto?.APApprovedAmount != _originalAPApprovedAmount;
        public bool APWithheldAmountChanged => _apApproverCompleteDto?.APWithheldAmount != _originalAPWithheldAmount;
        public bool APReviewCommentsChanged => !string.Equals(_apApproverCompleteDto?.APReviewComments, _originalAPReviewComments, StringComparison.Ordinal);

        public IEnumerable<string> GetChangedFields()
        {
            if (APApprovedAmountChanged) yield return nameof(_apApproverCompleteDto.APApprovedAmount);
            if (APWithheldAmountChanged) yield return nameof(_apApproverCompleteDto.APWithheldAmount);
            if (APReviewCommentsChanged) yield return nameof(_apApproverCompleteDto.APReviewComments);
            if (!string.Equals(_apApproverCompleteDto?.APReviewStatus, _originalAPReviewStatus, StringComparison.OrdinalIgnoreCase))
                yield return nameof(_apApproverCompleteDto.APReviewStatus);
        }
        #endregion

        #region Permissions / read-only
        private bool IsApproverReviewRequired = true;

        private bool CanEditApprovedAmount()
        {
            // If user is Accounts Payable, allow editing when invoice is in the AP approver step and not completed.
            if (RoleHelper.IsAccountPayableRole(_CurrentRoleName))
            {
                var invStatusLocal = _invoiceDto?.InvoiceStatus?.Trim();
                if (string.Equals(invStatusLocal, "With AP Approver", StringComparison.OrdinalIgnoreCase) && !IsAPApproverReviewCompleted())
                    return true;
                return false;
            }

            // must be Approver
            if (!_isApApprover)
                return false;

            //// must be assigned to this invoice
            //if (!_isInvAssigned)
            //    return false;

            // role must indicate Approver
            //if (string.IsNullOrWhiteSpace(_CurrentRoleName) || !_CurrentRoleName.Contains("APApprover", StringComparison.OrdinalIgnoreCase))
            //    return false;

            // invoice must be in "With APApprover" status
            var invStatus = _invoiceDto?.InvoiceStatus?.Trim();
            if (!string.Equals(invStatus, "With AP Approver", StringComparison.OrdinalIgnoreCase))
                return false;

            // if server indicates review already completed, disallow edit
            if (IsAPApproverReviewCompleted())
                return false;

            return true;
        }

        private bool IsAPApproverReviewCompleted()
        {
            var status = _invoiceDto?.APReviewStatus?.Trim();
            if (string.IsNullOrWhiteSpace(status))
                return false;

            return status.Equals("Approved", StringComparison.OrdinalIgnoreCase)
                || status.Equals("Rejected", StringComparison.OrdinalIgnoreCase)
                || status.Equals("Completed", StringComparison.OrdinalIgnoreCase);
        }

        // UI read-only helper used by markup
        private bool IsReadOnly => _apApproverSaved || IsAPApproverReviewCompleted();
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

                var status = _apApproverCompleteDto.APReviewStatus?.Trim();
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
                    if (!_apApproverCompleteDto.APApprovedAmount.HasValue)
                    {
                        Snackbar.Add("Approved amount is required when status is Approved.", Severity.Warning);
                        return;
                    }

                    var approvedValue = _apApproverCompleteDto.APApprovedAmount.GetValueOrDefault(0m);
                    if (approvedValue <= 0m)
                    {
                        Snackbar.Add("Approved amount must be greater than zero.", Severity.Warning);
                        return;
                    }

                    if (approvedValue > total)
                    {
                        Snackbar.Add("Approved amount cannot exceed invoice total.", Severity.Warning);
                        // clamp and show recalculation
                        _apApproverCompleteDto.APApprovedAmount = total;
                        _apApproverCompleteDto.APWithheldAmount = 0m;
                        StateHasChanged();
                        return;
                    }

                    // recalc withheld
                    _apApproverCompleteDto.APWithheldAmount = total - approvedValue;
                }
                else // Rejected
                {
                    // Remarks / comment required on rejection
                    if (string.IsNullOrWhiteSpace(_apApproverCompleteDto.APReviewComments))
                    {
                        Snackbar.Add("Remarks are required when status is Rejected.", Severity.Warning);
                        return;
                    }

                    // Per requirement: when Rejected both approved and withheld should be 0
                    _apApproverCompleteDto.APApprovedAmount = 0m;
                    _apApproverCompleteDto.APWithheldAmount = 0m;
                }

                // If withheld amount is non-zero, withheld reason must be provided
                if (_apApproverCompleteDto.APWithheldAmount.GetValueOrDefault(0m) != 0m
                    && string.IsNullOrWhiteSpace(_apApproverCompleteDto.APWithheldReason))
                {
                    Snackbar.Add("Withheld reason is required when withheld amount is not zero.", Severity.Warning);
                    // ensure validation message is shown in form (OnValidationRequested already wired for submit)
                    ValidateWithheldReason();
                    return;
                }

                // Ensure invoice id is set
                _apApproverCompleteDto.InvoiceId = _invoiceDto.Id;

                // If ApproverID not provided, set from cascading employee id if available
                if (_apApproverCompleteDto.APReviewerId == Guid.Empty && _LoggedInEmployeeID != Guid.Empty)
                    _apApproverCompleteDto.APReviewerId = _LoggedInEmployeeID;

                // Persist via repository (returns updated InvoiceDto)
                var refreshed = await InvoiceRepository.UpdateInvoiceAPReview(_apApproverCompleteDto);
                if (refreshed != null)
                {
                    // Re-fetch full invoice from server to ensure all display fields (like APReviewerName) are populated
                    try
                    {
                        var full = await InvoiceRepository.GetInvoiceById(refreshed.Id);
                        _invoiceDto = full ?? refreshed;
                    }
                    catch (Exception ex)
                    {
                        // If re-fetch fails, fall back to the update response
                        Logger.LogWarning(ex, "Failed to re-fetch invoice after AP approver update; using response object.");
                        _invoiceDto = refreshed;
                    }

                    MapFromInvoiceDto();
                }

                // Lock UI
                _apApproverSaved = true;

                Snackbar.Add("AP Approver review saved successfully.", Severity.Success);

                // Notify parent via EventCallback so parent can refresh data
                if (OnSaved.HasDelegate)
                    await OnSaved.InvokeAsync(_invoiceDto);

                if (this is IInvoiceAPApproverReviewHandlers handlers)
                    await handlers.SaveAsync();

                StateHasChanged();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error saving AP Approver review for Invoice ID {InvoiceId}", _invoiceDto?.Id);
                Snackbar.Add("An error occurred while saving the AP Approver review.", Severity.Error);
            }
        }

        private Task Cancel()
        {
            if (this is IInvoiceAPApproverReviewHandlers handlers)
                return handlers.CancelAsync();

            return Task.CompletedTask;
        }

        private interface IInvoiceAPApproverReviewHandlers
        {
            Task SaveAsync();
            Task CancelAsync();
        }
        #endregion
    }
}
