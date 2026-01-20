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
        [Inject] private NavigationManager NavigationManager { get; set; } = default!;

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
        private (Guid? PreviousReviewerId, string? PreviousReviewerName, decimal? PrevApprovedAmount, decimal? PrevWithheldAmount) _previousReviewInfo = (null, null, null, null);


        // small state
        private bool _checkerSaved = false;
        private bool _isSubmitting = false;
        private bool _checkerApprovedEdited = false;

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

            // Ensure we load previous review info before mapping
            _previousReviewInfo = InvoiceReviewHelpers.GetPreviousReviewInfo(_invoiceDto, "checker");

            // Map server invoice values into working DTO when component opens
            MapFromInvoiceDto();

            // ensure EditContext tracks current working DTO so DataAnnotationsValidator works
            if (_editContext == null || _editContext.Model != _CheckercompleteDto)
            {
                _editContext = new EditContext(_CheckercompleteDto);
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

            // Apply defaults based on current CheckerReviewStatus
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

            // Use a local variable to track the mapped invoice id, since InvCheckerReviewCompleteDto does not have InvoiceId
            // Only map if the CheckerID does not match or CheckerID is empty (as a proxy for "new" mapping)
            if (_CheckercompleteDto.CheckerID == Guid.Empty || _CheckercompleteDto.CheckerID != (_invoiceDto.CheckerID ?? Guid.Empty))
            {
                _CheckercompleteDto.CheckerID = _invoiceDto.CheckerID ?? Guid.Empty;
                _CheckercompleteDto.CheckerWithheldReason = _invoiceDto.CheckerWithheldReason;
                _CheckercompleteDto.CheckerReviewComment = _invoiceDto.CheckerReviewComment;
                _CheckercompleteDto.CheckerReviewStatus = _invoiceDto.CheckerReviewStatus;

                // Set amounts according to current status and available previous values:
                // - Pending => both 0
                // - Rejected => both 0 (per requested rule)
                // - Approved => if previous approved+withheld available use them; otherwise set approved = InvoiceTotalValue and withheld = 0
                ApplyAmountsBasedOnStatus();
            }
        }

        private void OnCheckerReviewStatusChanged(string? newStatus)
        {
            if (_CheckercompleteDto == null)
                return;

            // reset user-edit flag when status changes
            _checkerApprovedEdited = false;

            _CheckercompleteDto.CheckerReviewStatus = newStatus?.Trim();

            if (string.Equals(_CheckercompleteDto.CheckerReviewStatus, "Rejected", StringComparison.OrdinalIgnoreCase))
            {
                _CheckercompleteDto.CheckerApprovedAmount = 0m;
                _CheckercompleteDto.CheckerWithheldAmount = 0m;
            }
            else if (string.Equals(_CheckercompleteDto.CheckerReviewStatus, "Approved", StringComparison.OrdinalIgnoreCase))
            {
                if (_previousReviewInfo.PrevApprovedAmount.HasValue && _previousReviewInfo.PrevWithheldAmount.HasValue)
                {
                    _CheckercompleteDto.CheckerApprovedAmount = _previousReviewInfo.PrevApprovedAmount;
                    _CheckercompleteDto.CheckerWithheldAmount = _previousReviewInfo.PrevWithheldAmount;
                }
                else if (_invoiceDto != null)
                {
                    _CheckercompleteDto.CheckerApprovedAmount = _invoiceDto.InvoiceTotalValue;
                    _CheckercompleteDto.CheckerWithheldAmount = 0m;
                }
            }
            else
            {
                _CheckercompleteDto.CheckerApprovedAmount = 0m;
                _CheckercompleteDto.CheckerWithheldAmount = 0m;
            }

            // clear related validation messages
            if (_messageStore != null)
            {
                var reasonField = new FieldIdentifier(_CheckercompleteDto, nameof(_CheckercompleteDto.CheckerWithheldReason));
                _messageStore.Clear(reasonField);

                var commentField = new FieldIdentifier(_CheckercompleteDto, nameof(_CheckercompleteDto.CheckerReviewComment));
                if (!string.Equals(_CheckercompleteDto.CheckerReviewStatus, "Rejected", StringComparison.OrdinalIgnoreCase))
                    _messageStore.Clear(commentField);

                if (string.Equals(_CheckercompleteDto.CheckerReviewStatus, "Approved", StringComparison.OrdinalIgnoreCase))
                {
                    var approvedField = new FieldIdentifier(_CheckercompleteDto, nameof(_CheckercompleteDto.CheckerApprovedAmount));
                    var withheldField = new FieldIdentifier(_CheckercompleteDto, nameof(_CheckercompleteDto.CheckerWithheldAmount));
                    _messageStore.Clear(approvedField);
                    _messageStore.Clear(withheldField);
                }

                _editContext?.NotifyValidationStateChanged();
            }

            // enforce "Rejected => Remarks required" immediately
            ValidateRejectedRemarks();

            StateHasChanged();
        }
        /// <summary>
        /// Ensure default amounts reflect business rules:
        /// - Pending => Approved = 0, Withheld = 0
        /// - Rejected => Approved = 0, Withheld = 0
        /// - Approved => Use previous amounts if available (both), otherwise Approved = InvoiceTotalValue, Withheld = 0
        /// This only sets defaults when the working DTO amounts are null (so user edits are preserved).
        /// </summary>
        private void EnsureDefaultCheckerAmounts()
        {
            if (_invoiceDto == null || _CheckercompleteDto == null)
                return;

            var status = _invoiceDto.CheckerReviewStatus?.Trim();
            decimal total = _invoiceDto.InvoiceTotalValue;

            if (string.Equals(status, "Pending", StringComparison.OrdinalIgnoreCase))
            {
                if (!_CheckercompleteDto.CheckerApprovedAmount.HasValue)
                    _CheckercompleteDto.CheckerApprovedAmount = 0m;
                if (!_CheckercompleteDto.CheckerWithheldAmount.HasValue)
                    _CheckercompleteDto.CheckerWithheldAmount = 0m;

                return;
            }

            if (string.Equals(status, "Rejected", StringComparison.OrdinalIgnoreCase))
            {
                if (!_CheckercompleteDto.CheckerApprovedAmount.HasValue)
                    _CheckercompleteDto.CheckerApprovedAmount = 0m;
                if (!_CheckercompleteDto.CheckerWithheldAmount.HasValue)
                    _CheckercompleteDto.CheckerWithheldAmount = 0m;

                return;
            }

            if (string.Equals(status, "Approved", StringComparison.OrdinalIgnoreCase))
            {
                // if both previous approved and withheld are available use them
                if (_previousReviewInfo.PrevApprovedAmount.HasValue && _previousReviewInfo.PrevWithheldAmount.HasValue)
                {
                    if (!_CheckercompleteDto.CheckerApprovedAmount.HasValue)
                        _CheckercompleteDto.CheckerApprovedAmount = _previousReviewInfo.PrevApprovedAmount;
                    if (!_CheckercompleteDto.CheckerWithheldAmount.HasValue)
                        _CheckercompleteDto.CheckerWithheldAmount = _previousReviewInfo.PrevWithheldAmount;
                }
                else
                {
                    // fallback: approved := invoice total, withheld := 0
                    if (!_CheckercompleteDto.CheckerApprovedAmount.HasValue)
                        _CheckercompleteDto.CheckerApprovedAmount = total;
                    if (!_CheckercompleteDto.CheckerWithheldAmount.HasValue)
                        _CheckercompleteDto.CheckerWithheldAmount = 0m;
                }
            }
        }

        private void ApplyAmountsBasedOnStatus()
        {
            if (_invoiceDto == null || _CheckercompleteDto == null)
                return;

            var status = _invoiceDto.CheckerReviewStatus?.Trim();
            decimal total = _invoiceDto.InvoiceTotalValue;

            if (string.Equals(status, "Pending", StringComparison.OrdinalIgnoreCase))
            {
                // Only set defaults when DTO fields are not already set to preserve user edits
                if (!_CheckercompleteDto.CheckerApprovedAmount.HasValue)
                    _CheckercompleteDto.CheckerApprovedAmount = 0m;
                if (!_CheckercompleteDto.CheckerWithheldAmount.HasValue)
                    _CheckercompleteDto.CheckerWithheldAmount = 0m;

                return;
            }

            if (string.Equals(status, "Rejected", StringComparison.OrdinalIgnoreCase))
            {
                // When rejected we explicitly set both to 0 per rules
                _CheckercompleteDto.CheckerApprovedAmount = 0m;
                _CheckercompleteDto.CheckerWithheldAmount = 0m;
                return;
            }

            if (string.Equals(status, "Approved", StringComparison.OrdinalIgnoreCase))
            {
                // Prefer previous review amounts when available. Otherwise set defaults only when DTO fields are unset.
                if (_previousReviewInfo.PrevApprovedAmount.HasValue && _previousReviewInfo.PrevWithheldAmount.HasValue)
                {
                    if (!_CheckercompleteDto.CheckerApprovedAmount.HasValue)
                        _CheckercompleteDto.CheckerApprovedAmount = _previousReviewInfo.PrevApprovedAmount;
                    if (!_CheckercompleteDto.CheckerWithheldAmount.HasValue)
                        _CheckercompleteDto.CheckerWithheldAmount = _previousReviewInfo.PrevWithheldAmount;
                }
                else
                {
                    if (!_CheckercompleteDto.CheckerApprovedAmount.HasValue)
                        _CheckercompleteDto.CheckerApprovedAmount = total;
                    if (!_CheckercompleteDto.CheckerWithheldAmount.HasValue)
                        _CheckercompleteDto.CheckerWithheldAmount = 0m;
                }
            }
        }

        private void OnCheckerWithheldReasonBlur(FocusEventArgs e)
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
            if (_editContext == null || _messageStore == null || _CheckercompleteDto == null)
                return;

            var reasonField = new FieldIdentifier(_CheckercompleteDto, nameof(_CheckercompleteDto.CheckerWithheldReason));
            _messageStore.Clear(reasonField);

            var withheld = _CheckercompleteDto.CheckerWithheldAmount.GetValueOrDefault(0m);
            if (withheld != 0m && string.IsNullOrWhiteSpace(_CheckercompleteDto.CheckerWithheldReason))
            {
                _messageStore.Add(reasonField, "Withheld reason is required when Withheld Amount is not zero.");
            }

            _editContext.NotifyValidationStateChanged();
        }

        private void OnCheckerApprovedAmountChanged(decimal? newValue)
        {
            if (!CanEditApprovedAmount())
                return;

            if (_invoiceDto == null || _CheckercompleteDto == null)
                return;

            decimal total = _invoiceDto.InvoiceTotalValue;
            decimal requestedApproved = newValue ?? _CheckercompleteDto.CheckerApprovedAmount.GetValueOrDefault();

            // mark user edit so status/default logic won't overwrite
            _checkerApprovedEdited = true;

            _CheckercompleteDto.CheckerApprovedAmount = requestedApproved;
            _CheckercompleteDto.CheckerWithheldAmount = total - requestedApproved;

            // clear related validation messages similar to Initiator behavior
            if (_messageStore != null)
            {
                var reasonField = new FieldIdentifier(_CheckercompleteDto, nameof(_CheckercompleteDto.CheckerWithheldReason));
                _messageStore.Clear(reasonField);

                var approvedField = new FieldIdentifier(_CheckercompleteDto, nameof(_CheckercompleteDto.CheckerApprovedAmount));
                var withheldField = new FieldIdentifier(_CheckercompleteDto, nameof(_CheckercompleteDto.CheckerWithheldAmount));

                if (_CheckercompleteDto.CheckerWithheldAmount.GetValueOrDefault(0m) == 0m)
                {
                    _messageStore.Clear(approvedField);
                    _messageStore.Clear(withheldField);
                }

                _editContext?.NotifyValidationStateChanged();
            }

            StateHasChanged();
        }

        private async Task OnCheckerApprovedAmountBlur(FocusEventArgs e)
        {
            // yield to allow Mud numeric field handlers to complete updates
            await Task.Yield();
            ValidateApprovedAmount();
        }

        /// <summary>
        /// Validate Approved and Withheld amounts according to the same rules used in Initiator:
        /// - sign-aware restrictions
        /// - if no previous amounts exist enforce approved + withheld == total (rounded to 2 decimals)
        /// - approved must not exceed previous approved when previous exists (sign-aware)
        /// Messages are added to the ValidationMessageStore so they appear in ValidationSummary and next to fields.
        /// </summary>
        private void ValidateApprovedAmount()
        {
            if (_editContext == null || _messageStore == null || _CheckercompleteDto == null || _invoiceDto == null)
                return;

            var approvedField = new FieldIdentifier(_CheckercompleteDto, nameof(_CheckercompleteDto.CheckerApprovedAmount));
            var withheldField = new FieldIdentifier(_CheckercompleteDto, nameof(_CheckercompleteDto.CheckerWithheldAmount));

            // Clear previous messages for these fields (defensive)
            _messageStore.Clear(approvedField);
            _messageStore.Clear(withheldField);

            // Determine effective status: prefer working DTO value, fallback to invoice's persisted status
            var status = _CheckercompleteDto.CheckerReviewStatus?.Trim();
            if (string.IsNullOrWhiteSpace(status))
                status = _invoiceDto.CheckerReviewStatus?.Trim();

            // If the current status is Rejected, amounts are intentionally zero — clear any stale amount messages and skip invariants.
            if (!string.IsNullOrWhiteSpace(status) && status.Equals("Rejected", StringComparison.OrdinalIgnoreCase))
            {
                // Also clear any other leftover messages to be safe (prevents stale "Approved+Withheld must equal" from earlier runs)
                _messageStore.Clear();
                _editContext.NotifyValidationStateChanged();
                return;
            }

            decimal total = _invoiceDto.InvoiceTotalValue;
            decimal approved = _CheckercompleteDto.CheckerApprovedAmount.GetValueOrDefault(0m);
            decimal withheld = _CheckercompleteDto.CheckerWithheldAmount.GetValueOrDefault(0m);

            // Sign consistency: approved/withheld should follow invoice sign (or be zero)
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
            else // total == 0
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
        /// Ensure Remarks (CheckerReviewComment) is required when status == "Rejected".
        /// Adds/clears messages using the ValidationMessageStore so messages appear in ValidationSummary
        /// and next to the Remarks field.
        /// </summary>
        private void ValidateRejectedRemarks()
        {
            if (_editContext == null || _messageStore == null || _CheckercompleteDto == null)
                return;

            var commentField = new FieldIdentifier(_CheckercompleteDto, nameof(_CheckercompleteDto.CheckerReviewComment));
            _messageStore.Clear(commentField);

            var status = _CheckercompleteDto.CheckerReviewStatus?.Trim();
            if (!string.IsNullOrWhiteSpace(status)
                && status.Equals("Rejected", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(_CheckercompleteDto.CheckerReviewComment))
            {
                _messageStore.Add(commentField, "Remarks are required when status is Rejected.");
            }

            _editContext.NotifyValidationStateChanged();
        }
        #endregion

        #region Permissions / read-only
        private bool IsCheckerReviewRequired => _invoiceDto?.IsCheckerReviewRequired ?? false;

        private bool CanEditApprovedAmount()
        {
            // Do not allow edits if the invoice itself is in "Rejected" status
            if (IsInvoiceStatusRejected)
                return false;

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
        private bool IsReadOnly => _checkerSaved || IsCheckerReviewCompleted() || IsInvoiceStatusRejected;

        // Added helper to detect invoice-level "Rejected" status (place inside the Permissions / read-only region)
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

            if (_checkerSaved) // assuming a saved flag exists similar to _initiatorSaved; adjust name if different
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
                var status = _CheckercompleteDto.CheckerReviewStatus?.Trim();
                if (string.IsNullOrWhiteSpace(status) ||
                    !(status.Equals("Approved", StringComparison.OrdinalIgnoreCase) || status.Equals("Rejected", StringComparison.OrdinalIgnoreCase)))
                {
                    Snackbar.Add("Please select Approval status (Approved or Rejected).", Severity.Warning);
                    return;
                }

                decimal total = _invoiceDto.InvoiceTotalValue;

                if (status.Equals("Approved", StringComparison.OrdinalIgnoreCase))
                {
                    if (!_CheckercompleteDto.CheckerApprovedAmount.HasValue)
                    {
                        Snackbar.Add("Approved amount is required when status is Approved.", Severity.Warning);
                        return;
                    }
                }
                else // Rejected
                {
                    if (string.IsNullOrWhiteSpace(_CheckercompleteDto.CheckerReviewComment))
                    {
                        Snackbar.Add("Remarks are required when status is Rejected.", Severity.Warning);
                        return;
                    }

                    // When rejected both approved and withheld should be 0
                    _CheckercompleteDto.CheckerApprovedAmount = 0m;
                    _CheckercompleteDto.CheckerWithheldAmount = 0m;
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

                // All client validation passed: ensure withheld is consistent (defensive)
                if (status.Equals("Approved", StringComparison.OrdinalIgnoreCase))
                {
                    var approvedValue = _CheckercompleteDto.CheckerApprovedAmount.GetValueOrDefault(0m);

                    // Enforce sign-aware bounds against invoice (do not auto-correct; treat as validation)
                    if (total < 0m && approvedValue < total)
                    {
                        Snackbar.Add("Approved amount cannot be less than invoice total.", Severity.Warning);
                        return;
                    }
                    if (total > 0m && approvedValue > total)
                    {
                        Snackbar.Add("Approved amount cannot exceed invoice total.", Severity.Warning);
                        return;
                    }

                    // Validate against previous approved amount (if present) - sign-aware
                    if (_previousReviewInfo.PrevApprovedAmount.HasValue)
                    {
                        var prev = _previousReviewInfo.PrevApprovedAmount.Value;
                        if (prev > 0m && approvedValue > prev)
                        {
                            Snackbar.Add($"Approved amount cannot exceed previous approved amount ({FormatCurrency(prev)}).", Severity.Warning);
                            return;
                        }
                        if (prev < 0m && approvedValue < prev)
                        {
                            Snackbar.Add($"Approved amount cannot be less than previous approved amount ({FormatCurrency(prev)}).", Severity.Warning);
                            return;
                        }
                    }

                    // Recalculate withheld to ensure persisted object is consistent with the invariant
                    _CheckercompleteDto.CheckerWithheldAmount = total - approvedValue;
                }

                // Replace the existing invariant check with this guarded version:
                var effectiveStatus = _CheckercompleteDto.CheckerReviewStatus?.Trim();
                if (!string.Equals(effectiveStatus, "Rejected", StringComparison.OrdinalIgnoreCase))
                {
                    // Final canonical invariant check (strict equality within 2 decimals)
                    var approvedFinal = _CheckercompleteDto.CheckerApprovedAmount.GetValueOrDefault(0m);
                    var withheldFinal = _CheckercompleteDto.CheckerWithheldAmount.GetValueOrDefault(0m);
                    if (Math.Round(approvedFinal + withheldFinal - total, 2) != 0m)
                    {
                        Snackbar.Add("Approved Amount + Withheld Amount must equal Invoice Total.", Severity.Warning);
                        return;
                    }
                }

                // Defensive validation: withheld reason must be present when withheld amount != 0
                if (_CheckercompleteDto.CheckerWithheldAmount.GetValueOrDefault(0m) != 0m
                    && string.IsNullOrWhiteSpace(_CheckercompleteDto.CheckerWithheldReason))
                {
                    Snackbar.Add("Withheld reason is required when Withheld Amount is not zero.", Severity.Warning);
                    ValidateWithheldReason(); // ensure message is shown inline
                    return;
                }

                // Ensure invoice id is set (fix for the runtime exception)
                _CheckercompleteDto.InvoiceId = _invoiceDto.Id;

                // If CheckerID not provided, set from cascading employee id if available
                if (_CheckercompleteDto.CheckerID == Guid.Empty && _LoggedInEmployeeID != Guid.Empty)
                    _CheckercompleteDto.CheckerID = _LoggedInEmployeeID;

                // Persist via repository (returns updated InvoiceDto)
                var refreshed = await InvoiceRepository.UpdateInvoiceCheckerReview(_CheckercompleteDto);
                if (refreshed != null)
                {
                    try
                    {
                        var full = await InvoiceRepository.GetInvoiceById(refreshed.Id);
                        _invoiceDto = full ?? refreshed;
                    }
                    catch (Exception ex)
                    {
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
                NavigationManager.NavigateTo("/invoices", true);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error saving checker review for Invoice ID {InvoiceId}", _invoiceDto?.Id);
                Snackbar.Add("An error occurred while saving the checker review.", Severity.Error);
            }
            finally
            {
                // allow retry only when save failed; when success _checkerSaved should be true and button disabled
                _isSubmitting = false;
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

        private Task OnSupportingFileUploaded(String? url)
        {
            _CheckercompleteDto.CheckerReviewAttachment = url ?? string.Empty;
            return Task.CompletedTask;
        }
    }
}