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
    public partial class InvoiceValidatorReview
    {
        // Component parameters (inputs)
        [Parameter] public InvoiceDto? _invoiceDto { get; set; }
        [Parameter] public PurchaseOrderDto? _PODto { get; set; }

        // EventCallback to notify parent that the review was saved
        [Parameter] public EventCallback<InvoiceDto?> OnSaved { get; set; }

        // Internal DTO used when completing validator review
        [Parameter] public InvValidatorReviewCompleteDto _ValidatorcompleteDto { get; set; } = new();

        // repository + UI feedback + logger
        [Inject] private IInvoiceRepository InvoiceRepository { get; set; } = default!;
        [Inject] private ISnackbar Snackbar { get; set; } = default!;
        [Inject] private ILogger<InvoiceValidatorReview> Logger { get; set; } = default!;

        // Cascading parameters (UI theme / user context)
        [CascadingParameter] public Color _valueColor { get; set; } = Color.Default;
        [CascadingParameter] public Typo _labelTypo { get; set; } = Typo.subtitle2;
        [CascadingParameter] public Typo _valueTypo { get; set; } = Typo.body2;

        // Logged-in user context (supplied by parent)
        [Parameter] public Guid _LoggedInEmployeeID { get; set; } = Guid.Empty;
        [Parameter] public string _LoggedInUserType { get; set; } = string.Empty;
        [Parameter] public Guid _LoggedInVendorID { get; set; } = Guid.Empty;
        [Parameter] public string _CurrentRoleName { get; set; } = string.Empty;
        [Parameter] public bool _isInvAssigned { get; set; } = false;
        [Parameter] public bool _isValidator { get; set; } = false;

        // Track previous review amounts/info (from other roles) - helper struct
        private (Guid? PreviousReviewerId, string? PreviousReviewerName, decimal? PrevApprovedAmount, decimal? PrevWithheldAmount) _previousReviewInfo = (null, null, null, null);

        // small state
        private bool _validatorSaved = false;
        private bool _isSubmitting = false;
        private bool _validatorApprovedEdited = false;

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

            // Load previous validator review info before mapping
            _previousReviewInfo = InvoiceReviewHelpers.GetPreviousReviewInfo(_invoiceDto, "validator");

            // Map server invoice values into working DTO when component opens
            MapFromInvoiceDto();

            // ensure EditContext tracks current working DTO so DataAnnotationsValidator works
            if (_editContext == null || _editContext.Model != _ValidatorcompleteDto)
            {
                _editContext = new EditContext(_ValidatorcompleteDto);
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

            // Apply defaults based on current ValidatorReviewStatus
            EnsureDefaultValidatorAmounts();

            // If server already indicates review completed, lock UI
            _validatorSaved = IsValidatorReviewCompleted();
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
            if (_invoiceDto == null || _ValidatorcompleteDto == null)
                return;

            // Only map if the ValidatorID does not match or is empty (approx "new" mapping)
            if (_ValidatorcompleteDto.ValidatorID == Guid.Empty || _ValidatorcompleteDto.ValidatorID != (_invoiceDto.ValidatorD365ID ?? Guid.Empty))
            {
                _ValidatorcompleteDto.ValidatorID = _invoiceDto.ValidatorD365ID ?? Guid.Empty;
                _ValidatorcompleteDto.ValidatorWithheldReason = _invoiceDto.ValidatorWithheldReason;
                _ValidatorcompleteDto.ValidatorReviewComment = _invoiceDto.ValidatorReviewComment;
                _ValidatorcompleteDto.ValidatorReviewStatus = _invoiceDto.ValidatorReviewStatus;

                // Set amounts according to current status and available previous values
                ApplyAmountsBasedOnStatus();
            }
        }

        private void OnValidatorReviewStatusChanged(string? newStatus)
        {
            if (_ValidatorcompleteDto == null)
                return;

            // reset user-edit flag when status changes
            _validatorApprovedEdited = false;

            _ValidatorcompleteDto.ValidatorReviewStatus = newStatus?.Trim();

            if (string.Equals(_ValidatorcompleteDto.ValidatorReviewStatus, "Rejected", StringComparison.OrdinalIgnoreCase))
            {
                _ValidatorcompleteDto.ValidatorApprovedAmount = 0m;
                _ValidatorcompleteDto.ValidatorWithheldAmount = 0m;
            }
            else if (string.Equals(_ValidatorcompleteDto.ValidatorReviewStatus, "Approved", StringComparison.OrdinalIgnoreCase))
            {
                if (_previousReviewInfo.PrevApprovedAmount.HasValue && _previousReviewInfo.PrevWithheldAmount.HasValue)
                {
                    _ValidatorcompleteDto.ValidatorApprovedAmount = _previousReviewInfo.PrevApprovedAmount;
                    _ValidatorcompleteDto.ValidatorWithheldAmount = _previousReviewInfo.PrevWithheldAmount;
                }
                else if (_invoiceDto != null)
                {
                    _ValidatorcompleteDto.ValidatorApprovedAmount = _invoiceDto.InvoiceTotalValue;
                    _ValidatorcompleteDto.ValidatorWithheldAmount = 0m;
                }
            }
            else
            {
                _ValidatorcompleteDto.ValidatorApprovedAmount = 0m;
                _ValidatorcompleteDto.ValidatorWithheldAmount = 0m;
            }

            // clear related validation messages
            if (_messageStore != null)
            {
                var reasonField = new FieldIdentifier(_ValidatorcompleteDto, nameof(_ValidatorcompleteDto.ValidatorWithheldReason));
                _messageStore.Clear(reasonField);

                var commentField = new FieldIdentifier(_ValidatorcompleteDto, nameof(_ValidatorcompleteDto.ValidatorReviewComment));
                if (!string.Equals(_ValidatorcompleteDto.ValidatorReviewStatus, "Rejected", StringComparison.OrdinalIgnoreCase))
                    _messageStore.Clear(commentField);

                if (string.Equals(_ValidatorcompleteDto.ValidatorReviewStatus, "Approved", StringComparison.OrdinalIgnoreCase))
                {
                    var approvedField = new FieldIdentifier(_ValidatorcompleteDto, nameof(_ValidatorcompleteDto.ValidatorApprovedAmount));
                    var withheldField = new FieldIdentifier(_ValidatorcompleteDto, nameof(_ValidatorcompleteDto.ValidatorWithheldAmount));
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
        private void EnsureDefaultValidatorAmounts()
        {
            if (_invoiceDto == null || _ValidatorcompleteDto == null)
                return;

            var status = _invoiceDto.ValidatorReviewStatus?.Trim();
            decimal total = _invoiceDto.InvoiceTotalValue;

            if (string.Equals(status, "Pending", StringComparison.OrdinalIgnoreCase))
            {
                if (!_ValidatorcompleteDto.ValidatorApprovedAmount.HasValue)
                    _ValidatorcompleteDto.ValidatorApprovedAmount = 0m;
                if (!_ValidatorcompleteDto.ValidatorWithheldAmount.HasValue)
                    _ValidatorcompleteDto.ValidatorWithheldAmount = 0m;

                return;
            }

            if (string.Equals(status, "Rejected", StringComparison.OrdinalIgnoreCase))
            {
                if (!_ValidatorcompleteDto.ValidatorApprovedAmount.HasValue)
                    _ValidatorcompleteDto.ValidatorApprovedAmount = 0m;
                if (!_ValidatorcompleteDto.ValidatorWithheldAmount.HasValue)
                    _ValidatorcompleteDto.ValidatorWithheldAmount = 0m;

                return;
            }

            if (string.Equals(status, "Approved", StringComparison.OrdinalIgnoreCase))
            {
                // if both previous approved and withheld are available use them
                if (_previousReviewInfo.PrevApprovedAmount.HasValue && _previousReviewInfo.PrevWithheldAmount.HasValue)
                {
                    if (!_ValidatorcompleteDto.ValidatorApprovedAmount.HasValue)
                        _ValidatorcompleteDto.ValidatorApprovedAmount = _previousReviewInfo.PrevApprovedAmount;
                    if (!_ValidatorcompleteDto.ValidatorWithheldAmount.HasValue)
                        _ValidatorcompleteDto.ValidatorWithheldAmount = _previousReviewInfo.PrevWithheldAmount;
                }
                else
                {
                    // fallback: approved := invoice total, withheld := 0
                    if (!_ValidatorcompleteDto.ValidatorApprovedAmount.HasValue)
                        _ValidatorcompleteDto.ValidatorApprovedAmount = total;
                    if (!_ValidatorcompleteDto.ValidatorWithheldAmount.HasValue)
                        _ValidatorcompleteDto.ValidatorWithheldAmount = 0m;
                }
            }
        }

        private void ApplyAmountsBasedOnStatus()
        {
            if (_invoiceDto == null || _ValidatorcompleteDto == null)
                return;

            var status = _invoiceDto.ValidatorReviewStatus?.Trim();
            decimal total = _invoiceDto.InvoiceTotalValue;

            if (string.Equals(status, "Pending", StringComparison.OrdinalIgnoreCase))
            {
                // Only set defaults when DTO fields are not already set to preserve user edits
                if (!_ValidatorcompleteDto.ValidatorApprovedAmount.HasValue)
                    _ValidatorcompleteDto.ValidatorApprovedAmount = 0m;
                if (!_ValidatorcompleteDto.ValidatorWithheldAmount.HasValue)
                    _ValidatorcompleteDto.ValidatorWithheldAmount = 0m;

                return;
            }

            if (string.Equals(status, "Rejected", StringComparison.OrdinalIgnoreCase))
            {
                // When rejected we explicitly set both to 0 per rules
                _ValidatorcompleteDto.ValidatorApprovedAmount = 0m;
                _ValidatorcompleteDto.ValidatorWithheldAmount = 0m;
                return;
            }

            if (string.Equals(status, "Approved", StringComparison.OrdinalIgnoreCase))
            {
                // Prefer previous review amounts when available. Otherwise set defaults only when DTO fields are unset.
                if (_previousReviewInfo.PrevApprovedAmount.HasValue && _previousReviewInfo.PrevWithheldAmount.HasValue)
                {
                    if (!_ValidatorcompleteDto.ValidatorApprovedAmount.HasValue)
                        _ValidatorcompleteDto.ValidatorApprovedAmount = _previousReviewInfo.PrevApprovedAmount;
                    if (!_ValidatorcompleteDto.ValidatorWithheldAmount.HasValue)
                        _ValidatorcompleteDto.ValidatorWithheldAmount = _previousReviewInfo.PrevWithheldAmount;
                }
                else
                {
                    if (!_ValidatorcompleteDto.ValidatorApprovedAmount.HasValue)
                        _ValidatorcompleteDto.ValidatorApprovedAmount = total;
                    if (!_ValidatorcompleteDto.ValidatorWithheldAmount.HasValue)
                        _ValidatorcompleteDto.ValidatorWithheldAmount = 0m;
                }
            }
        }

        private void OnValidatorWithheldReasonBlur(FocusEventArgs e)
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
            if (_editContext == null || _messageStore == null || _ValidatorcompleteDto == null)
                return;

            var reasonField = new FieldIdentifier(_ValidatorcompleteDto, nameof(_ValidatorcompleteDto.ValidatorWithheldReason));
            _messageStore.Clear(reasonField);

            var withheld = _ValidatorcompleteDto.ValidatorWithheldAmount.GetValueOrDefault(0m);
            if (withheld != 0m && string.IsNullOrWhiteSpace(_ValidatorcompleteDto.ValidatorWithheldReason))
            {
                _messageStore.Add(reasonField, "Withheld reason is required when Withheld Amount is not zero.");
            }

            _editContext.NotifyValidationStateChanged();
        }

        private void OnValidatorApprovedAmountChanged(decimal? newValue)
        {
            if (!CanEditApprovedAmount())
                return;

            if (_invoiceDto == null || _ValidatorcompleteDto == null)
                return;

            decimal total = _invoiceDto.InvoiceTotalValue;
            decimal requestedApproved = newValue ?? _ValidatorcompleteDto.ValidatorApprovedAmount.GetValueOrDefault();

            // mark user edit so status/default logic won't overwrite
            _validatorApprovedEdited = true;

            _ValidatorcompleteDto.ValidatorApprovedAmount = requestedApproved;
            _ValidatorcompleteDto.ValidatorWithheldAmount = total - requestedApproved;

            // clear related validation messages similar to Initiator behavior
            if (_messageStore != null)
            {
                var reasonField = new FieldIdentifier(_ValidatorcompleteDto, nameof(_ValidatorcompleteDto.ValidatorWithheldReason));
                _messageStore.Clear(reasonField);

                var approvedField = new FieldIdentifier(_ValidatorcompleteDto, nameof(_ValidatorcompleteDto.ValidatorApprovedAmount));
                var withheldField = new FieldIdentifier(_ValidatorcompleteDto, nameof(_ValidatorcompleteDto.ValidatorWithheldAmount));

                if (_ValidatorcompleteDto.ValidatorWithheldAmount.GetValueOrDefault(0m) == 0m)
                {
                    _messageStore.Clear(approvedField);
                    _messageStore.Clear(withheldField);
                }

                _editContext?.NotifyValidationStateChanged();
            }

            StateHasChanged();
        }

        private async Task OnValidatorApprovedAmountBlur(FocusEventArgs e)
        {
            // yield to allow Mud numeric field handlers to complete updates
            await Task.Yield();
            ValidateApprovedAmount();
        }

        /// <summary>
        /// Validate Approved and Withheld amounts according to rules:
        /// - sign-aware restrictions
        /// - if no previous amounts exist enforce approved + withheld == total (rounded to 2 decimals)
        /// - approved must not exceed previous approved when previous exists (sign-aware)
        /// Messages are added to the ValidationMessageStore so they appear in ValidationSummary and next to fields.
        /// </summary>
        private void ValidateApprovedAmount()
        {
            if (_editContext == null || _messageStore == null || _ValidatorcompleteDto == null || _invoiceDto == null)
                return;

            var approvedField = new FieldIdentifier(_ValidatorcompleteDto, nameof(_ValidatorcompleteDto.ValidatorApprovedAmount));
            var withheldField = new FieldIdentifier(_ValidatorcompleteDto, nameof(_ValidatorcompleteDto.ValidatorWithheldAmount));

            // Clear previous messages for these fields (defensive)
            _messageStore.Clear(approvedField);
            _messageStore.Clear(withheldField);

            // Determine effective status: prefer working DTO value, fallback to invoice's persisted status
            var status = _ValidatorcompleteDto.ValidatorReviewStatus?.Trim();
            if (string.IsNullOrWhiteSpace(status))
                status = _invoiceDto.ValidatorReviewStatus?.Trim();

            // If the current status is Rejected, amounts are intentionally zero — clear any stale amount messages and skip invariants.
            if (!string.IsNullOrWhiteSpace(status) && status.Equals("Rejected", StringComparison.OrdinalIgnoreCase))
            {
                // Also clear any other leftover messages to be safe
                _messageStore.Clear();
                _editContext.NotifyValidationStateChanged();
                return;
            }

            decimal total = _invoiceDto.InvoiceTotalValue;
            decimal approved = _ValidatorcompleteDto.ValidatorApprovedAmount.GetValueOrDefault(0m);
            decimal withheld = _ValidatorcompleteDto.ValidatorWithheldAmount.GetValueOrDefault(0m);

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
        /// Ensure Remarks (ValidatorReviewComment) is required when status == "Rejected".
        /// Adds/clears messages using the ValidationMessageStore so messages appear in ValidationSummary
        /// and next to the Remarks field.
        /// </summary>
        private void ValidateRejectedRemarks()
        {
            if (_editContext == null || _messageStore == null || _ValidatorcompleteDto == null)
                return;

            var commentField = new FieldIdentifier(_ValidatorcompleteDto, nameof(_ValidatorcompleteDto.ValidatorReviewComment));
            _messageStore.Clear(commentField);

            var status = _ValidatorcompleteDto.ValidatorReviewStatus?.Trim();
            if (!string.IsNullOrWhiteSpace(status)
                && status.Equals("Rejected", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(_ValidatorcompleteDto.ValidatorReviewComment))
            {
                _messageStore.Add(commentField, "Remarks are required when status is Rejected.");
            }

            _editContext.NotifyValidationStateChanged();
        }
        #endregion

        #region Permissions / read-only
        private bool IsValidatorReviewRequired => _invoiceDto?.IsValidatorReviewRequired ?? false;

        private bool CanEditApprovedAmount()
        {
            // Do not allow edits if the invoice itself is in "Rejected" status
            if (IsInvoiceStatusRejected)
                return false;

            // must be validator
            if (!_isValidator)
                return false;

            // must be assigned to this invoice
            if (!_isInvAssigned)
                return false;

            // role must indicate Validator
            if (string.IsNullOrWhiteSpace(_CurrentRoleName) || !_CurrentRoleName.Contains("Validator", StringComparison.OrdinalIgnoreCase))
                return false;

            // invoice must be in "With Validator" status
            var invStatus = _invoiceDto?.InvoiceStatus?.Trim();
            if (!string.Equals(invStatus, "With Validator", StringComparison.OrdinalIgnoreCase))
                return false;

            // if server indicates review already completed, disallow edit
            if (IsValidatorReviewCompleted())
                return false;

            return true;
        }

        private bool IsValidatorReviewCompleted()
        {
            var status = _invoiceDto?.ValidatorReviewStatus?.Trim();
            if (string.IsNullOrWhiteSpace(status))
                return false;

            return status.Equals("Approved", StringComparison.OrdinalIgnoreCase)
                || status.Equals("Rejected", StringComparison.OrdinalIgnoreCase)
                || status.Equals("Completed", StringComparison.OrdinalIgnoreCase);
        }

        // UI read-only helper used by markup
        private bool IsReadOnly => _validatorSaved || IsValidatorReviewCompleted() || IsInvoiceStatusRejected;

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

            if (_validatorSaved)
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

                var status = _ValidatorcompleteDto.ValidatorReviewStatus?.Trim();
                if (string.IsNullOrWhiteSpace(status) ||
                    !(status.Equals("Approved", StringComparison.OrdinalIgnoreCase) || status.Equals("Rejected", StringComparison.OrdinalIgnoreCase)))
                {
                    Snackbar.Add("Please select Approval status (Approved or Rejected).", Severity.Warning);
                    return;
                }

                decimal total = _invoiceDto.InvoiceTotalValue;

                if (status.Equals("Approved", StringComparison.OrdinalIgnoreCase))
                {
                    if (!_ValidatorcompleteDto.ValidatorApprovedAmount.HasValue)
                    {
                        Snackbar.Add("Approved amount is required when status is Approved.", Severity.Warning);
                        return;
                    }
                }
                else // Rejected
                {
                    if (string.IsNullOrWhiteSpace(_ValidatorcompleteDto.ValidatorReviewComment))
                    {
                        Snackbar.Add("Remarks are required when status is Rejected.", Severity.Warning);
                        return;
                    }

                    // When rejected both approved and withheld should be 0
                    _ValidatorcompleteDto.ValidatorApprovedAmount = 0m;
                    _ValidatorcompleteDto.ValidatorWithheldAmount = 0m;
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
                    var approvedValue = _ValidatorcompleteDto.ValidatorApprovedAmount.GetValueOrDefault(0m);

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
                    _ValidatorcompleteDto.ValidatorWithheldAmount = total - approvedValue;
                }

                // Final canonical invariant check (strict equality within 2 decimals) unless Rejected
                var effectiveStatus = _ValidatorcompleteDto.ValidatorReviewStatus?.Trim();
                if (!string.Equals(effectiveStatus, "Rejected", StringComparison.OrdinalIgnoreCase))
                {
                    var approvedFinal = _ValidatorcompleteDto.ValidatorApprovedAmount.GetValueOrDefault(0m);
                    var withheldFinal = _ValidatorcompleteDto.ValidatorWithheldAmount.GetValueOrDefault(0m);
                    if (Math.Round(approvedFinal + withheldFinal - total, 2) != 0m)
                    {
                        Snackbar.Add("Approved Amount + Withheld Amount must equal Invoice Total.", Severity.Warning);
                        return;
                    }
                }

                // Defensive validation: withheld reason must be present when withheld amount != 0
                if (_ValidatorcompleteDto.ValidatorWithheldAmount.GetValueOrDefault(0m) != 0m
                    && string.IsNullOrWhiteSpace(_ValidatorcompleteDto.ValidatorWithheldReason))
                {
                    Snackbar.Add("Withheld reason is required when Withheld Amount is not zero.", Severity.Warning);
                    ValidateWithheldReason(); // ensure message is shown inline
                    return;
                }

                // Ensure invoice id is set
                _ValidatorcompleteDto.InvoiceId = _invoiceDto.Id;

                // If ValidatorID not provided, set from cascading employee id if available
                if (_ValidatorcompleteDto.ValidatorID == Guid.Empty && _LoggedInEmployeeID != Guid.Empty)
                    _ValidatorcompleteDto.ValidatorID = _LoggedInEmployeeID;

                // Persist via repository (returns updated InvoiceDto)
                var refreshed = await InvoiceRepository.UpdateInvoiceValidatorApproval(_ValidatorcompleteDto);
                if (refreshed != null)
                {
                    try
                    {
                        var full = await InvoiceRepository.GetInvoiceById(refreshed.Id);
                        _invoiceDto = full ?? refreshed;
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Failed to re-fetch invoice after validator update; using response object.");
                        _invoiceDto = refreshed;
                    }

                    MapFromInvoiceDto();
                }

                // Lock UI
                _validatorSaved = true;

                Snackbar.Add("Validator review saved successfully.", Severity.Success);

                // Notify parent via EventCallback so parent can refresh data
                if (OnSaved.HasDelegate)
                    await OnSaved.InvokeAsync(_invoiceDto);

                if (this is IInvoiceValidatorReviewHandlers handlers)
                    await handlers.SaveAsync();

                StateHasChanged();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error saving validator review for Invoice ID {InvoiceId}", _invoiceDto?.Id);
                Snackbar.Add("An error occurred while saving the validator review.", Severity.Error);
            }
            finally
            {
                // allow retry only when save failed; when success _validatorSaved should be true and button disabled
                _isSubmitting = false;
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

        private Task OnSupportingFileUploaded(String? url)
        {
            _ValidatorcompleteDto.ValidatorReviewAttachment = url ?? string.Empty;
            return Task.CompletedTask;
        }
    }
}
