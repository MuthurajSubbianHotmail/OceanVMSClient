using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using OceanVMSClient.HttpRepoInterface.InvoiceModule;
using Shared.DTO.POModule;
using System;
using System.Threading.Tasks;
using System.Linq;
using System.Globalization;

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
        private (Guid? PreviousReviewerId, string? PreviousReviewerName, decimal? PrevApprovedAmount, decimal? PrevWithheldAmount) _previousReviewInfo = (null, null, null, null);

        // small state
        private bool _initiatorSaved = false;

        // EditContext used for client-side DataAnnotations validation
        private EditContext? _editContext;
        private ValidationMessageStore? _messageStore;
        private bool _editContextSubscribed = false;

        #region Display helpers (computed properties)
        // Use explicit India currency culture for INR symbol
        private static readonly CultureInfo _inCulture = new CultureInfo("en-IN");

        private string FormatCurrency(decimal? value) => value.HasValue ? value.Value.ToString("C", _inCulture) : "-";
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

            // load previous review info early so status rule can use it instead of overwriting user edits
            _previousReviewInfo = InvoiceReviewHelpers.GetPreviousReviewInfo(_invoiceDto, "initiator");

            // ensure EditContext tracks current working DTO so DataAnnotationsValidator works
            if (_editContext == null || _editContext.Model != _InitiatorCompleteDto)
            {
                _editContext = new EditContext(_InitiatorCompleteDto);
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
                };

                // DO NOT validate on every field change — only on blur or submit per request
                _editContextSubscribed = true;
            }

            // Apply defaults if InitiatorReviewStatus == "Pending"
            EnsureDefaultInitiatorAmounts();

            // Ensure amounts reflect current status (Pending/Approved/Rejected)
            ApplyInitiatorStatusRule(_InitiatorCompleteDto?.InitiatorReviewStatus);

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
        /// If InitiatorReviewStatus is "Pending" set defaults:
        ///  - InitiatorApprovedAmount = 0
        ///  - InitiatorWithheldAmount = 0
        /// When status is not pending we do not overwrite existing user-entered values here.
        /// </summary>
        private void EnsureDefaultInitiatorAmounts()
        {
            if (_invoiceDto == null || _InitiatorCompleteDto == null)
                return;

            if (string.Equals(_invoiceDto.InitiatorReviewStatus, "Pending", StringComparison.OrdinalIgnoreCase))
            {
                // set defaults only when amounts are null or invoice indicates Pending — per requirement both should be 0
                if (!_InitiatorCompleteDto.InitiatorApprovedAmount.HasValue || _InitiatorCompleteDto.InitiatorApprovedAmount.GetValueOrDefault() != 0m)
                    _InitiatorCompleteDto.InitiatorApprovedAmount = 0m;

                if (!_InitiatorCompleteDto.InitiatorWithheldAmount.HasValue || _InitiatorCompleteDto.InitiatorWithheldAmount.GetValueOrDefault() != 0m)
                    _InitiatorCompleteDto.InitiatorWithheldAmount = 0m;
            }
        }

        /// <summary>
        /// Apply business rule whenever approval status changes:
        ///  - Pending  => approved = 0, withheld = 0
        ///  - Approved => approved = invoice total, withheld = 0
        ///  - Rejected => approved = 0, withheld = 0
        /// Only clears old validation messages; message display is performed only on blur or submit.
        /// </summary>
        private void ApplyInitiatorStatusRule(string? status)
        {
            if (_InitiatorCompleteDto == null || _invoiceDto == null)
                return;

            var s = status?.Trim();
            if (string.IsNullOrWhiteSpace(s))
                return;

            decimal total = _invoiceDto.InvoiceTotalValue;

            if (s.Equals("Pending", StringComparison.OrdinalIgnoreCase))
            {
                _InitiatorCompleteDto.InitiatorApprovedAmount = 0m;
                _InitiatorCompleteDto.InitiatorWithheldAmount = 0m;
            }
            else if (s.Equals("Approved", StringComparison.OrdinalIgnoreCase))
            {
                // Do not overwrite user-entered values. Apply defaults only when the working DTO fields are not set.
                // Prefer previous review values when available (keeps consistency with other review components).
                if (_previousReviewInfo.PrevApprovedAmount.HasValue && _previousReviewInfo.PrevWithheldAmount.HasValue)
                {
                    if (!_InitiatorCompleteDto.InitiatorApprovedAmount.HasValue)
                        _InitiatorCompleteDto.InitiatorApprovedAmount = _previousReviewInfo.PrevApprovedAmount;
                    if (!_InitiatorCompleteDto.InitiatorWithheldAmount.HasValue)
                        _InitiatorCompleteDto.InitiatorWithheldAmount = _previousReviewInfo.PrevWithheldAmount;
                }
                else
                {
                    if (!_InitiatorCompleteDto.InitiatorApprovedAmount.HasValue)
                        _InitiatorCompleteDto.InitiatorApprovedAmount = total;
                    if (!_InitiatorCompleteDto.InitiatorWithheldAmount.HasValue)
                        _InitiatorCompleteDto.InitiatorWithheldAmount = total - _InitiatorCompleteDto.InitiatorApprovedAmount.GetValueOrDefault(0m);
                }
            }
            else if (s.Equals("Rejected", StringComparison.OrdinalIgnoreCase))
            {
                _InitiatorCompleteDto.InitiatorApprovedAmount = 0m;
                _InitiatorCompleteDto.InitiatorWithheldAmount = 0m;
            }

            // Clear validation messages that should no longer apply:
            if (_messageStore != null && _InitiatorCompleteDto != null)
            {
                // Always clear withheld-reason messages here (we only show them when withheld != 0).
                var reasonField = new FieldIdentifier(_InitiatorCompleteDto, nameof(_InitiatorCompleteDto.InitiatorWithheldReason));
                _messageStore.Clear(reasonField);

                // Remarks are required only for Rejected — clear remark message when status is not Rejected.
                var commentField = new FieldIdentifier(_InitiatorCompleteDto, nameof(_InitiatorCompleteDto.InitiatorReviewComment));
                if (!s.Equals("Rejected", StringComparison.OrdinalIgnoreCase))
                    _messageStore.Clear(commentField);

                // If status is Approved we can also clear amount-related messages (they are no longer applicable).
                if (s.Equals("Approved", StringComparison.OrdinalIgnoreCase))
                {
                    var approvedField = new FieldIdentifier(_InitiatorCompleteDto, nameof(_InitiatorCompleteDto.InitiatorApprovedAmount));
                    var withheldField = new FieldIdentifier(_InitiatorCompleteDto, nameof(_InitiatorCompleteDto.InitiatorWithheldAmount));
                    _messageStore.Clear(approvedField);
                    _messageStore.Clear(withheldField);
                }

                _editContext?.NotifyValidationStateChanged();
            }
        }

        /// <summary>
        /// UI event handler to be wired to the approval status input (select/radio).
        /// </summary>
        private void OnInitiatorReviewStatusChanged(string? status)
        {
            if (_InitiatorCompleteDto == null)
                return;

            _InitiatorCompleteDto.InitiatorReviewStatus = status ?? string.Empty;
            ApplyInitiatorStatusRule(status);

            // notify UI
            StateHasChanged();
        }

        /// <summary>
        /// Called when the Withheld Reason field loses focus — this is one of two moments
        /// we want to show the validation message (the other is on form submit).
        /// </summary>
        private void OnWithheldReasonBlur(FocusEventArgs e)
        {
            ValidateWithheldReason();
        }

        /// <summary>
        /// Called when the Approved Amount field loses focus per Rule6.
        /// </summary>
        private async Task OnApprovedAmountBlur(FocusEventArgs e)
        {
            // Small asynchronous yield to ensure MudNumericField's ValueChanged handler
            // (which updates the DTO and withheld amount) runs before validation.
            await Task.Yield();
            ValidateApprovedAmount();
        }
        /// <summary>
        /// Validate that WithheldReason is present when WithheldAmount != 0.
        /// Uses ValidationMessageStore so messages appear in ValidationSummary and next to fields.
        /// Only adds messages; clearing is done by callers when immediate hide is desired.
        /// </summary>
        private void ValidateWithheldReason()
        {
            if (_editContext == null || _messageStore == null || _InitiatorCompleteDto == null)
                return;

            var reasonField = new FieldIdentifier(_InitiatorCompleteDto, nameof(_InitiatorCompleteDto.InitiatorWithheldReason));
            _messageStore.Clear(reasonField);

            var withheld = _InitiatorCompleteDto.InitiatorWithheldAmount.GetValueOrDefault(0m);
            if (withheld != 0m && string.IsNullOrWhiteSpace(_InitiatorCompleteDto.InitiatorWithheldReason))
            {
                _messageStore.Add(reasonField, "Withheld reason is required when Withheld Amount is not zero.");
            }

            _editContext.NotifyValidationStateChanged();
        }

        private void OnRemarksBlur(FocusEventArgs e)
        {
            ValidateRejectedRemarks();
        }

        /// <summary>
        /// Ensure Remarks (InitiatorReviewComment) is required when status == "Rejected".
        /// Adds/clears messages using the ValidationMessageStore so messages appear in ValidationSummary
        /// and next to the Remarks field.
        /// </summary>
        private void ValidateRejectedRemarks()
        {
            if (_editContext == null || _messageStore == null || _InitiatorCompleteDto == null)
                return;

            var commentField = new FieldIdentifier(_InitiatorCompleteDto, nameof(_InitiatorCompleteDto.InitiatorReviewComment));
            _messageStore.Clear(commentField);

            var status = _InitiatorCompleteDto.InitiatorReviewStatus?.Trim();
            if (!string.IsNullOrWhiteSpace(status)
                && status.Equals("Rejected", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(_InitiatorCompleteDto.InitiatorReviewComment))
            {
                _messageStore.Add(commentField, "Remarks are required when status is Rejected.");
            }

            _editContext.NotifyValidationStateChanged();
        }

        /// <summary>
        /// Validate Approved and Withheld amounts according to rules 1-5 and show messages via _messageStore.
        /// Called on blur of Approved Amount and on Save.
        /// </summary>
        private void ValidateApprovedAmount()
        {
            if (_editContext == null || _messageStore == null || _InitiatorCompleteDto == null || _invoiceDto == null)
                return;

            var approvedField = new FieldIdentifier(_InitiatorCompleteDto, nameof(_InitiatorCompleteDto.InitiatorApprovedAmount));
            var withheldField = new FieldIdentifier(_InitiatorCompleteDto, nameof(_InitiatorCompleteDto.InitiatorWithheldAmount));

            // Clear previous messages for these fields
            _messageStore.Clear(approvedField);
            _messageStore.Clear(withheldField);

            decimal total = _invoiceDto.InvoiceTotalValue;
            decimal approved = _InitiatorCompleteDto.InitiatorApprovedAmount.GetValueOrDefault(0m);
            decimal withheld = _InitiatorCompleteDto.InitiatorWithheldAmount.GetValueOrDefault(0m);

            // Rule1 & Rule2: sign restrictions
            if (total > 0m && approved < 0m)
            {
                _messageStore.Add(approvedField, "Approved Amount cannot be negative when Invoice Total is positive.");
            }
            if (total < 0m && approved > 0m)
            {
                _messageStore.Add(approvedField, "Approved Amount cannot be positive when Invoice Total is negative.");
            }

            // Rule3: if there is no previous approved/withheld amounts enforce approved + withheld == total
            if (!_previousReviewInfo.PrevApprovedAmount.HasValue && !_previousReviewInfo.PrevWithheldAmount.HasValue)
            {
                // allow small rounding differences — compare to 2 decimals
                if (Math.Round(approved + withheld - total, 2) != 0m)
                {
                    _messageStore.Add(approvedField, "Approved Amount + Withheld Amount must equal Invoice Total.");
                }

                // explicit check for approved outside invoice bounds (shows validation only; does not change values)
                if (total > 0m && approved > total)
                {
                    _messageStore.Add(approvedField, $"Approved Amount cannot exceed Invoice Total ({FormatCurrency(total)}).");
                }
                if (total < 0m && approved < total)
                {
                    _messageStore.Add(approvedField, $"Approved Amount cannot be less than Invoice Total ({FormatCurrency(total)}).");
                }
            }

            // Rule5: previous approved amount constraints (5a/5b)
            if (_previousReviewInfo.PrevApprovedAmount.HasValue)
            {
                var prev = _previousReviewInfo.PrevApprovedAmount.Value;
                if (prev > 0m && approved > prev)
                {
                    _messageStore.Add(approvedField, $"Approved Amount cannot exceed previous approved amount ({FormatCurrency(prev)}).");
                }
                if (prev < 0m && approved < prev)
                {
                    _messageStore.Add(approvedField, $"Approved Amount cannot be less than previous approved amount ({FormatCurrency(prev)}).");
                }
            }

            _editContext.NotifyValidationStateChanged();
        }

        private void OnInitiatorApprovedAmountChanged(decimal? newValue)
        {
            // Prevent programmatic/user changes when not allowed
            if (!CanEditApprovedAmount())
                return;

            if (_invoiceDto == null || _InitiatorCompleteDto == null)
                return;

            decimal total = _invoiceDto.InvoiceTotalValue;

            // Use the incoming value if present, otherwise keep the current DTO value (prevents transient resets)
            decimal requestedApproved = newValue ?? _InitiatorCompleteDto.InitiatorApprovedAmount.GetValueOrDefault();

            // IMPORTANT: Do NOT auto-adjust the user's entered approved amount.
            // We only calculate withheld as total - approved so UI shows the implied withheld,
            // but we leave Approved as the user's entry and defer validation to blur/submit.
            _InitiatorCompleteDto.InitiatorApprovedAmount = requestedApproved;
            _InitiatorCompleteDto.InitiatorWithheldAmount = total - requestedApproved;

            // Clear withheld-reason validation while user is changing amounts (message will show only on blur or submit).
            // Also clear amount-related validation messages when the implied withheld becomes zero (those errors no longer apply).
            if (_messageStore != null)
            {
                var reasonField = new FieldIdentifier(_InitiatorCompleteDto, nameof(_InitiatorCompleteDto.InitiatorWithheldReason));
                _messageStore.Clear(reasonField);

                var approvedField = new FieldIdentifier(_InitiatorCompleteDto, nameof(_InitiatorCompleteDto.InitiatorApprovedAmount));
                var withheldField = new FieldIdentifier(_InitiatorCompleteDto, nameof(_InitiatorCompleteDto.InitiatorWithheldAmount));

                if (_InitiatorCompleteDto.InitiatorWithheldAmount.GetValueOrDefault(0m) == 0m)
                {
                    _messageStore.Clear(approvedField);
                    _messageStore.Clear(withheldField);
                }

                _editContext?.NotifyValidationStateChanged();
            }

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

                // Final enforcement of business rules before save
                decimal total = _invoiceDto.InvoiceTotalValue;

                // run approved/withheld validations (Rule6 wants blur, but also final validation on submit)
                ValidateApprovedAmount();
                ValidateWithheldReason();

                if (_editContext != null && _editContext.GetValidationMessages().Any())
                {
                    Snackbar.Add("Please correct validation errors before submitting.", Severity.Warning);
                    return;
                }

                if (status.Equals("Approved", StringComparison.OrdinalIgnoreCase))
                {
                    // When approved, set approved amount to invoice total and withheld to zero
                    //_InitiatorCompleteDto.InitiatorApprovedAmount = total;
                    //_InitiatorCompleteDto.InitiatorWithheldAmount = 0m;
                }
                else // Rejected
                {
                    // Remarks / comment required on rejection
                    if (string.IsNullOrWhiteSpace(_InitiatorCompleteDto.InitiatorReviewComment))
                    {
                        Snackbar.Add("Remarks are required when status is Rejected.", Severity.Warning);
                        return;
                    }

                    // When rejected, force both approved and withheld amounts to 0
                    _InitiatorCompleteDto.InitiatorApprovedAmount = 0m;
                    _InitiatorCompleteDto.InitiatorWithheldAmount = 0m;
                }

                // Defensive validation: withheld reason must be present when withheld amount != 0
                if (_InitiatorCompleteDto.InitiatorWithheldAmount.GetValueOrDefault(0m) != 0m
                    && string.IsNullOrWhiteSpace(_InitiatorCompleteDto.InitiatorWithheldReason))
                {
                    Snackbar.Add("Withheld reason is required when Withheld Amount is not zero.", Severity.Warning);
                    // ensure validation message is shown in form (OnValidationRequested already wired for submit)
                    ValidateWithheldReason();
                    return;
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

