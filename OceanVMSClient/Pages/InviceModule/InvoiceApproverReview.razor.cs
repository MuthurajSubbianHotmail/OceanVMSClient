using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using OceanVMSClient.HttpRepoInterface.InvoiceModule;
using Shared.DTO.POModule;
using System;
using System.Threading.Tasks;
using System.Linq;

namespace OceanVMSClient.Pages.InviceModule
{
    public partial class InvoiceApproverReview
    {
        // Component parameters (inputs)
        [Parameter] public InvoiceDto? _invoiceDto { get; set; }
        [Parameter] public PurchaseOrderDto? _PODto { get; set; }

        // EventCallback to notify parent that the review was saved
        [Parameter] public EventCallback<InvoiceDto?> OnSaved { get; set; }

        // Internal DTO used when completing approver review
        [Parameter] public InvApproverReviewCompleteDto _approverCompleteDto { get; set; } = new();

        // repository + UI feedback + logger
        [Inject] private IInvoiceRepository InvoiceRepository { get; set; } = default!;
        [Inject] private ISnackbar Snackbar { get; set; } = default!;
        [Inject] private ILogger<InvoiceApproverReview> Logger { get; set; } = default!;

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
        [Parameter] public bool _isApprover { get; set; } = false;
        private (Guid? PreviousReviewerId, string? PreviousReviewerName, decimal? PrevApprovedAmount, decimal? PrevWithheldAmount) _previousReviewInfo = (null, null, null, null);

        // small state
        private bool _approverSaved = false;
        private bool _isSubmitting = false;

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
            _previousReviewInfo = InvoiceReviewHelpers.GetPreviousReviewInfo(_invoiceDto, "approver");

            // Map server invoice values into working DTO when component opens
            MapFromInvoiceDto();

            // ensure EditContext tracks current working DTO so DataAnnotationsValidator works
            if (_editContext == null || _editContext.Model != _approverCompleteDto)
            {
                _editContext = new EditContext(_approverCompleteDto);
                _messageStore = new ValidationMessageStore(_editContext);
                _editContextSubscribed = false; // force subscription below
            }

            // subscribe editContext events once
            if (_editContext != null && !_editContextSubscribed)
            {
                // validate on form submit
                _editContext.OnValidationRequested += (sender, args) =>
                {
                    ValidateWithheldReason();
                    ValidateRejectedRemarks();
                    ValidateApprovedAmount();
                };

                // DO NOT validate on every field change — only on blur or submit per request
                _editContextSubscribed = true;
            }

            // Apply defaults based on current ApproverReviewStatus
            EnsureDefaultApproverAmounts();

            // If server already indicates review completed, lock UI
            _approverSaved = IsApproverReviewCompleted();
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
            if (_invoiceDto == null || _approverCompleteDto == null)
                return;

            // populate only when the working DTO is empty (to avoid overwriting user edits)
            if (_approverCompleteDto.InvoiceId == Guid.Empty || _approverCompleteDto.InvoiceId != _invoiceDto.Id)
            {
                _approverCompleteDto.InvoiceId = _invoiceDto.Id;
                _approverCompleteDto.ApproverID = _invoiceDto.ApproverID ?? Guid.Empty;
                _approverCompleteDto.ApproverApprovedAmount = _invoiceDto.ApproverApprovedAmount;
                _approverCompleteDto.ApproverWithheldAmount = _invoiceDto.ApproverWithheldAmount;
                _approverCompleteDto.ApproverWithheldReason = _invoiceDto.ApproverWithheldReason;
                _approverCompleteDto.ApproverReviewComment = _invoiceDto.ApproverReviewComment;
                _approverCompleteDto.ApproverReviewStatus = _invoiceDto.ApproverReviewStatus;
            }
        }

        /// <summary>
        /// Ensure default amounts reflect business rules:
        /// - Pending  => Approved = 0, Withheld = 0
        /// - Rejected => Approved = 0, Withheld = 0
        /// - Approved => Use previous amounts if available (both), otherwise Approved = InvoiceTotalValue, Withheld = 0
        /// This only sets defaults when the working DTO amounts are null (so user edits are preserved).
        /// </summary>
        private void EnsureDefaultApproverAmounts()
        {
            if (_invoiceDto == null || _approverCompleteDto == null)
                return;

            var status = _invoiceDto.ApproverReviewStatus?.Trim();
            decimal total = _invoiceDto.InvoiceTotalValue;

            if (string.Equals(status, "Pending", StringComparison.OrdinalIgnoreCase))
            {
                if (!_approverCompleteDto.ApproverApprovedAmount.HasValue)
                    _approverCompleteDto.ApproverApprovedAmount = 0m;
                if (!_approverCompleteDto.ApproverWithheldAmount.HasValue)
                    _approverCompleteDto.ApproverWithheldAmount = 0m;

                return;
            }

            if (string.Equals(status, "Rejected", StringComparison.OrdinalIgnoreCase))
            {
                if (!_approverCompleteDto.ApproverApprovedAmount.HasValue)
                    _approverCompleteDto.ApproverApprovedAmount = 0m;
                if (!_approverCompleteDto.ApproverWithheldAmount.HasValue)
                    _approverCompleteDto.ApproverWithheldAmount = 0m;

                return;
            }

            if (string.Equals(status, "Approved", StringComparison.OrdinalIgnoreCase))
            {
                // if both previous approved and withheld are available use them
                if (_previousReviewInfo.PrevApprovedAmount.HasValue && _previousReviewInfo.PrevWithheldAmount.HasValue)
                {
                    if (!_approverCompleteDto.ApproverApprovedAmount.HasValue)
                        _approverCompleteDto.ApproverApprovedAmount = _previousReviewInfo.PrevApprovedAmount;
                    if (!_approverCompleteDto.ApproverWithheldAmount.HasValue)
                        _approverCompleteDto.ApproverWithheldAmount = _previousReviewInfo.PrevWithheldAmount;
                }
                else
                {
                    // fallback: approved := invoice total, withheld := 0
                    if (!_approverCompleteDto.ApproverApprovedAmount.HasValue)
                        _approverCompleteDto.ApproverApprovedAmount = total;
                    if (!_approverCompleteDto.ApproverWithheldAmount.HasValue)
                        _approverCompleteDto.ApproverWithheldAmount = 0m;
                }
            }
        }

        /// <summary>
        /// Handler wired to the Approval select so UI updates amounts immediately when status changes.
        /// </summary>
        private void OnApproverReviewStatusChanged(string? newStatus)
        {
            if (_approverCompleteDto == null)
                return;

            _approverCompleteDto.ApproverReviewStatus = newStatus?.Trim();

            if (string.Equals(_approverCompleteDto.ApproverReviewStatus, "Rejected", StringComparison.OrdinalIgnoreCase))
            {
                // Per requirement: both should be 0 when rejected
                _approverCompleteDto.ApproverApprovedAmount = 0m;
                _approverCompleteDto.ApproverWithheldAmount = 0m;
            }
            else if (string.Equals(_approverCompleteDto.ApproverReviewStatus, "Approved", StringComparison.OrdinalIgnoreCase))
            {
                // Prefer previous review amounts when available, otherwise use invoice total
                if (_previousReviewInfo.PrevApprovedAmount.HasValue && _previousReviewInfo.PrevWithheldAmount.HasValue)
                {
                    _approverCompleteDto.ApproverApprovedAmount = _previousReviewInfo.PrevApprovedAmount;
                    _approverCompleteDto.ApproverWithheldAmount = _previousReviewInfo.PrevWithheldAmount;
                }
                else if (_invoiceDto != null)
                {
                    _approverCompleteDto.ApproverApprovedAmount = _invoiceDto.InvoiceTotalValue;
                    _approverCompleteDto.ApproverWithheldAmount = 0m;
                }
            }
            else
            {
                // Pending or other -> reset to 0
                _approverCompleteDto.ApproverApprovedAmount = 0m;
                _approverCompleteDto.ApproverWithheldAmount = 0m;
            }

            // Clear withheld-reason validation while changing status
            if (_messageStore != null)
            {
                var reasonField = new FieldIdentifier(_approverCompleteDto, nameof(_approverCompleteDto.ApproverWithheldReason));
                _messageStore.Clear(reasonField);
                _editContext?.NotifyValidationStateChanged();
            }

            StateHasChanged();
        }

        private void OnApproverApprovedAmountChanged(decimal? newValue)
        {
            if (!CanEditApprovedAmount())
                return;

            if (_invoiceDto == null || _approverCompleteDto == null)
                return;

            decimal total = _invoiceDto.InvoiceTotalValue;
            decimal approved = newValue.GetValueOrDefault(0m);

            if (approved < 0m) approved = 0m;
            if (approved > total) approved = total;

            _approverCompleteDto.ApproverApprovedAmount = approved;
            _approverCompleteDto.ApproverWithheldAmount = total - approved;

            // Clear withheld-reason validation while user is changing amounts (message will show only on blur or submit)
            if (_messageStore != null)
            {
                var reasonField = new FieldIdentifier(_approverCompleteDto, nameof(_approverCompleteDto.ApproverWithheldReason));
                _messageStore.Clear(reasonField);
                _editContext?.NotifyValidationStateChanged();
            }

            StateHasChanged();
        }

        private void OnApproverWithheldReasonBlur(FocusEventArgs e)
        {
            ValidateWithheldReason();
        }

        /// <summary>
        /// Validate that WithheldReason is present when WithheldAmount != 0.
        /// Uses ValidationMessageStore so messages appear in ValidationSummary and next to fields.
        /// Only adds messages; clearing is done by callers when immediate hide is desired.
        /// </summary>
        private void ValidateWithheldReason()
        {
            if (_editContext == null || _messageStore == null || _approverCompleteDto == null)
                return;

            var reasonField = new FieldIdentifier(_approverCompleteDto, nameof(_approverCompleteDto.ApproverWithheldReason));
            _messageStore.Clear(reasonField);

            var withheld = _approverCompleteDto.ApproverWithheldAmount.GetValueOrDefault(0m);
            if (withheld != 0m && string.IsNullOrWhiteSpace(_approverCompleteDto.ApproverWithheldReason))
            {
                _messageStore.Add(reasonField, "Withheld reason is required when Withheld Amount is not zero.");
            }

            _editContext.NotifyValidationStateChanged();
        }

        private async Task OnApproverApprovedAmountBlur(FocusEventArgs e)
        {
            // yield to allow Mud field handlers to complete updates
            await Task.Yield();
            ValidateApprovedAmount();
        }

        /// <summary>
        /// Validate Approved and Withheld amounts according to rules (sign-aware, previous-review constraints, equality invariant).
        /// Adds messages to ValidationMessageStore so they appear in ValidationSummary and next to fields.
        /// </summary>
        private void ValidateApprovedAmount()
        {
            if (_editContext == null || _messageStore == null || _approverCompleteDto == null || _invoiceDto == null)
                return;

            var approvedField = new FieldIdentifier(_approverCompleteDto, nameof(_approverCompleteDto.ApproverApprovedAmount));
            var withheldField = new FieldIdentifier(_approverCompleteDto, nameof(_approverCompleteDto.ApproverWithheldAmount));

            // Clear previous messages for these fields
            _messageStore.Clear(approvedField);
            _messageStore.Clear(withheldField);

            var status = _approverCompleteDto.ApproverReviewStatus?.Trim();
            if (string.IsNullOrWhiteSpace(status))
                status = _invoiceDto.ApproverReviewStatus?.Trim();

            // If rejected, skip numeric invariants
            if (!string.IsNullOrWhiteSpace(status) && status.Equals("Rejected", StringComparison.OrdinalIgnoreCase))
            {
                _messageStore.Clear();
                _editContext.NotifyValidationStateChanged();
                return;
            }

            decimal total = _invoiceDto.InvoiceTotalValue;
            decimal approved = _approverCompleteDto.ApproverApprovedAmount.GetValueOrDefault(0m);
            decimal withheld = _approverCompleteDto.ApproverWithheldAmount.GetValueOrDefault(0m);

            // Sign consistency checks
            if (total > 0m)
            {
                if (approved < 0m)
                    _messageStore.Add(approvedField, "Approved Amount cannot be negative when Invoice Total is positive.");
                if (withheld < 0m)
                    _messageStore.Add(withheldField, "Withheld Amount cannot be negative when Invoice Total is positive.");
            }
            else if (total < 0m)
            {
                if (approved > 0m)
                    _messageStore.Add(approvedField, "Approved Amount cannot be positive when Invoice Total is negative (debit note).");
                if (withheld > 0m)
                    _messageStore.Add(withheldField, "Withheld Amount cannot be positive when Invoice Total is negative (debit note).");
            }
            else
            {
                if (approved != 0m)
                    _messageStore.Add(approvedField, "Approved Amount must be zero when Invoice Total is zero.");
                if (withheld != 0m)
                    _messageStore.Add(withheldField, "Withheld Amount must be zero when Invoice Total is zero.");
            }

            // If there are no previous review amounts require strict equality (within 2-decimal tolerance)
            if (!_previousReviewInfo.PrevApprovedAmount.HasValue && !_previousReviewInfo.PrevWithheldAmount.HasValue)
            {
                if (Math.Round(approved + withheld - total, 2) != 0m)
                    _messageStore.Add(approvedField, "Approved Amount + Withheld Amount must equal Invoice Total.");

                if (total > 0m && approved > total)
                    _messageStore.Add(approvedField, $"Approved Amount cannot exceed Invoice Total ({FormatCurrency(total)}).");
                if (total < 0m && approved < total)
                    _messageStore.Add(approvedField, $"Approved Amount cannot be less than Invoice Total ({FormatCurrency(total)}).");
            }

            // Previous-approved constraints (sign-aware)
            if (_previousReviewInfo.PrevApprovedAmount.HasValue)
            {
                var prev = _previousReviewInfo.PrevApprovedAmount.Value;
                if (prev > 0m && approved > prev)
                    _messageStore.Add(approvedField, $"Approved Amount cannot exceed previous approved amount ({FormatCurrency(prev)}).");
                if (prev < 0m && approved < prev)
                    _messageStore.Add(approvedField, $"Approved Amount cannot be less than previous approved amount ({FormatCurrency(prev)}).");
            }

            _editContext.NotifyValidationStateChanged();
        }

        private void OnRemarksBlur(FocusEventArgs e)
        {
            ValidateRejectedRemarks();
        }

        /// <summary>
        /// Ensure Remarks (ApproverReviewComment) is required when status == "Rejected".
        /// Adds/clears messages using the ValidationMessageStore so messages appear in ValidationSummary
        /// and next to the Remarks field.
        /// </summary>
        private void ValidateRejectedRemarks()
        {
            if (_editContext == null || _messageStore == null || _approverCompleteDto == null)
                return;

            var commentField = new FieldIdentifier(_approverCompleteDto, nameof(_approverCompleteDto.ApproverReviewComment));
            _messageStore.Clear(commentField);

            var status = _approverCompleteDto.ApproverReviewStatus?.Trim();
            if (!string.IsNullOrWhiteSpace(status)
                && status.Equals("Rejected", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(_approverCompleteDto.ApproverReviewComment))
            {
                _messageStore.Add(commentField, "Remarks are required when status is Rejected.");
            }

            _editContext.NotifyValidationStateChanged();
        }

        private Task OnSupportingFileUploaded(string? url)
        {
            _approverCompleteDto.ApproverReviewAttachment = url ?? string.Empty;
            return Task.CompletedTask;
        }
        #endregion

        #region Permissions / read-only
        private bool IsApproverReviewRequired => _invoiceDto?.IsApproverReviewRequired ?? false;

        private bool CanEditApprovedAmount()
        {
            // Do not allow edits if the invoice itself is in "Rejected" status
            if (IsInvoiceStatusRejected)
                return false;

            // must be Approver
            if (!_isApprover)
                return false;

            // must be assigned to this invoice
            if (!_isInvAssigned)
                return false;

            // role must indicate Approver
            if (string.IsNullOrWhiteSpace(_CurrentRoleName) || !_CurrentRoleName.Contains("Approver", StringComparison.OrdinalIgnoreCase))
                return false;

            // invoice must be in "With Approver" status
            var invStatus = _invoiceDto?.InvoiceStatus?.Trim();
            if (!string.Equals(invStatus, "With Approver", StringComparison.OrdinalIgnoreCase))
                return false;

            // if server indicates review already completed, disallow edit
            if (IsApproverReviewCompleted())
                return false;

            return true;
        }

        private bool IsApproverReviewCompleted()
        {
            var status = _invoiceDto?.ApproverReviewStatus?.Trim();
            if (string.IsNullOrWhiteSpace(status))
                return false;

            return status.Equals("Approved", StringComparison.OrdinalIgnoreCase)
                || status.Equals("Rejected", StringComparison.OrdinalIgnoreCase)
                || status.Equals("Completed", StringComparison.OrdinalIgnoreCase);
        }

        // UI read-only helper used by markup
        private bool IsReadOnly => _approverSaved || IsApproverReviewCompleted() || IsInvoiceStatusRejected;

        // helper to detect invoice-level "Rejected" status
        private bool IsInvoiceStatusRejected =>
            string.Equals(_invoiceDto?.InvoiceStatus?.Trim(), "Rejected", StringComparison.OrdinalIgnoreCase);
        #endregion

        #region Actions
        private async Task SaveDecision()
        {
            // Prevent double submit / show appropriate message
            if (_isSubmitting)
            {
                Snackbar.Add("Review submission is already in progress. Please wait...", Severity.Info);
                return;
            }

            if (_approverSaved)
            {
                Snackbar.Add("Review has already been submitted.", Severity.Info);
                return;
            }

            _isSubmitting = true;
            try
            {
                if (_invoiceDto == null)
                {
                    Snackbar.Add("Invoice not loaded.", Severity.Error);
                    return;
                }

                if (IsInvoiceStatusRejected)
                {
                    Snackbar.Add("This invoice is in 'Rejected' status and cannot be edited.", Severity.Warning);
                    return;
                }

                var status = _approverCompleteDto.ApproverReviewStatus?.Trim();
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
                    if (!_approverCompleteDto.ApproverApprovedAmount.HasValue)
                    {
                        Snackbar.Add("Approved amount is required when status is Approved.", Severity.Warning);
                        return;
                    }

                    var approvedValue = _approverCompleteDto.ApproverApprovedAmount.GetValueOrDefault(0m);
                    if (approvedValue <= 0m)
                    {
                        Snackbar.Add("Approved amount must be greater than zero.", Severity.Warning);
                        return;
                    }

                    if (approvedValue > total)
                    {
                        Snackbar.Add("Approved amount cannot exceed invoice total.", Severity.Warning);
                        // clamp and show recalculation
                        _approverCompleteDto.ApproverApprovedAmount = total;
                        _approverCompleteDto.ApproverWithheldAmount = 0m;
                        StateHasChanged();
                        return;
                    }

                    // recalc withheld
                    _approverCompleteDto.ApproverWithheldAmount = total - approvedValue;
                }
                else // Rejected
                {
                    // Remarks / comment required on rejection
                    if (string.IsNullOrWhiteSpace(_approverCompleteDto.ApproverReviewComment))
                    {
                        Snackbar.Add("Remarks are required when status is Rejected.", Severity.Warning);
                        return;
                    }

                    // Per requested rule: when rejected both approved and withheld should be 0
                    _approverCompleteDto.ApproverApprovedAmount = 0m;
                    _approverCompleteDto.ApproverWithheldAmount = 0m;
                }

                // Run final client-side validations
                ValidateApprovedAmount();
                ValidateWithheldReason();
                ValidateRejectedRemarks();

                // If there are validation messages, do not persist
                if (_editContext != null && _editContext.GetValidationMessages().Any())
                {
                    Snackbar.Add("Please correct validation errors before submitting.", Severity.Warning);
                    return;
                }

                // Defensive validation: withheld reason must be present when withheld amount != 0
                if (_approverCompleteDto.ApproverWithheldAmount.GetValueOrDefault(0m) != 0m
                    && string.IsNullOrWhiteSpace(_approverCompleteDto.ApproverWithheldReason))
                {
                    Snackbar.Add("Withheld reason is required when Withheld Amount is not zero.", Severity.Warning);
                    // ensure validation message is shown inline
                    ValidateWithheldReason();
                    return;
                }

                // Ensure invoice id is set
                _approverCompleteDto.InvoiceId = _invoiceDto.Id;

                // If ApproverID not provided, set from cascading employee id if available
                if (_approverCompleteDto.ApproverID == Guid.Empty && _LoggedInEmployeeID != Guid.Empty)
                    _approverCompleteDto.ApproverID = _LoggedInEmployeeID;

                // Persist via repository (returns updated InvoiceDto)
                var refreshed = await InvoiceRepository.UpdateInvoiceApproverApproval(_approverCompleteDto);
                if (refreshed != null)
                {
                    // Re-fetch full invoice from server to ensure all display fields are populated
                    try
                    {
                        var full = await InvoiceRepository.GetInvoiceById(refreshed.Id);
                        _invoiceDto = full ?? refreshed;
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Failed to re-fetch invoice after approver update; using response object.");
                        _invoiceDto = refreshed;
                    }

                    MapFromInvoiceDto();
                }

                // Lock UI
                _approverSaved = true;

                Snackbar.Add("Approver review saved successfully.", Severity.Success);

                // Notify parent via EventCallback so parent can refresh data
                if (OnSaved.HasDelegate)
                    await OnSaved.InvokeAsync(_invoiceDto);

                if (this is IInvoiceApproverReviewHandlers handlers)
                    await handlers.SaveAsync();

                StateHasChanged();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error saving approver review for Invoice ID {InvoiceId}", _invoiceDto?.Id);
                Snackbar.Add("An error occurred while saving the approver review.", Severity.Error);
            }
            finally
            {
                // allow retry only when save failed; when success _approverSaved should be true and button disabled
                _isSubmitting = false;
            }
        }

        private Task Cancel()
        {
            if (this is IInvoiceApproverReviewHandlers handlers)
                return handlers.CancelAsync();

            return Task.CompletedTask;
        }

        private interface IInvoiceApproverReviewHandlers
        {
            Task SaveAsync();
            Task CancelAsync();
        }
        #endregion
    }
}
