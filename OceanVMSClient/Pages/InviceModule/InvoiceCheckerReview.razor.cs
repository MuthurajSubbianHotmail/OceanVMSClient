using Microsoft.AspNetCore.Components;
using MudBlazor;
using OceanVMSClient.HttpRepoInterface.InvoiceModule;
using Shared.DTO.POModule;
using System.Net.Http.Json;

namespace OceanVMSClient.Pages.InviceModule
{
    public partial class InvoiceCheckerReview
    {
        // Component parameters (inputs)
        [Parameter] public InvoiceDto? _invoiceDto { get; set; }
        [Parameter] public PurchaseOrderDto? _PODto { get; set; }

        // Internal DTO used when completing initiator review
        [Parameter] public InvCheckerReviewCompleteDto _CheckercompleteDto { get; set; } = new();

        // injections used for save & feedback
        [Inject] private HttpClient Http { get; set; } = default!;
        [Inject] private ISnackbar Snackbar { get; set; } = default!;
        [Inject] private ILogger<InvoiceCheckerReview> Logger { get; set; } = default!;
        [Inject] private IInvoiceRepository InvoiceRepository { get; set; } = default!;


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
        [Parameter] public bool _isChecker { get; set; } = false;

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

        #region Lifecycle methods
        protected override void OnParametersSet()
        {
            base.OnParametersSet();
            EnsureDefaultCheckerAmounts();
        }
        #endregion


        #region UI color helpers

        private Color GetSLAStatusColor(string? slaStatus) =>
            slaStatus switch
            {
                null => Color.Default,
                var s when s.Equals("Delayed", StringComparison.OrdinalIgnoreCase) => Color.Warning,
                var s when s.Equals("NA", StringComparison.OrdinalIgnoreCase) => Color.Default,
                var s when s.Equals("Within SLA", StringComparison.OrdinalIgnoreCase) => Color.Success,
                _ => Color.Info
            };

        private Color GetStatusColor(string? status) =>
            status switch
            {
                null => Color.Default,
                var s when s.Equals("Approved", StringComparison.OrdinalIgnoreCase) => Color.Success,
                var s when s.Equals("Withheld", StringComparison.OrdinalIgnoreCase) => Color.Warning,
                var s when s.Equals("Rejected", StringComparison.OrdinalIgnoreCase) => Color.Error,
                _ => Color.Info
            };

        private Color GetSLAColor(string? slaStatus) => GetSLAStatusColor(slaStatus);

        #endregion

        #region Validations and defaults
        /// <summary>
        /// If CheckerReviewStatus is "Pending" set defaults:
        ///  - CheckerApprovedAmount = InvoiceTotalValue
        ///  - CheckerWithheldAmount = 0
        /// Safe when DTOs are null.
        /// </summary>
        private void EnsureDefaultCheckerAmounts()
        {
            if (_invoiceDto == null || _CheckercompleteDto == null)
                return;

            if (string.Equals(_invoiceDto.CheckerReviewStatus, "Pending", StringComparison.OrdinalIgnoreCase) && CanEditApprovedAmount())
            {
                var total = _invoiceDto.InvoiceTotalValue;
                _CheckercompleteDto.CheckerApprovedAmount = total;
                _CheckercompleteDto.CheckerWithheldAmount = 0m;
            }
        }

        /// <summary>
        /// Called when user changes the approved amount:
        ///  - Clamp approved to [0, InvoiceTotalValue]
        ///  - Recalculate withheld as InvoiceTotalValue - approved
        /// </summary>
        private void OnCheckerApprovedAmountChanged(decimal? newValue)
        {
            if (_invoiceDto == null || _CheckercompleteDto == null)
                return;

            decimal total = _invoiceDto.InvoiceTotalValue;
            decimal approved = newValue.GetValueOrDefault(0m);

            // enforce bounds
            if (approved < 0m) approved = 0m;
            if (approved > total) approved = total;

            _CheckercompleteDto.CheckerApprovedAmount = approved;
            _CheckercompleteDto.CheckerWithheldAmount = total - approved;

            StateHasChanged();
        }
        #endregion

        #region Permission / editability helpers

        /// <summary>
        /// Returns the boolean value of the nullable flag on the DTO.
        /// Safe to call when the DTO or the property is null.
        /// </summary>
        private bool IsCheckerReviewRequired => _invoiceDto?.IsCheckerReviewRequired ?? false;

        /// <summary>
        /// Returns true when the current user/context should be allowed to edit the Approved Amount.
        /// Conditions:
        ///  - DTO flag IsInitiatorReviewRequired must be true
        ///  - Invoice is assigned (_isInvAssigned)
        ///  - Current role contains "Initiator"
        ///  - Initiator review hasn't been marked as Completed
        /// </summary>
        private bool CanEditApprovedAmount()
        {
            if (!_isChecker)
                return false;
            if (!IsCheckerReviewRequired)
                return false;

            //if (!_isInvAssigned)
            //    return false;

            //if (string.IsNullOrWhiteSpace(_CurrentRoleName) || !_CurrentRoleName.Contains("Checker", StringComparison.OrdinalIgnoreCase))
            //    return false;

            // If the initiator review status indicates completion/blocking, disallow edit
            if (string.Equals(_invoiceDto?.CheckerReviewStatus, "Completed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(_invoiceDto?.CheckerReviewStatus, "Approved", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(_invoiceDto?.CheckerReviewStatus, "Rejected", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }
        /// <summary>
        /// Returns true when other fields (withheld amount/reason/remarks/status) should be editable.
        /// Default: editable when initiator review is NOT required (so full edit available).
        /// </summary>
        private bool CanEditOtherFields() => !IsCheckerReviewRequired && _isInvAssigned;

        #endregion

        #region Actions

        private async Task SaveDecision()
        {
            try
            {
                if (_invoiceDto == null)
                {
                    Snackbar.Add("Invoice data missing.", Severity.Error);
                    return;
                }

                // Validate selection
                var status = _CheckercompleteDto.CheckerReviewStatus?.Trim();
                if (string.IsNullOrWhiteSpace(status) ||
                    !(string.Equals(status, "Approved", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(status, "Rejected", StringComparison.OrdinalIgnoreCase)))
                {
                    Snackbar.Add("Please select Approval status: Approved or Rejected.", Severity.Warning);
                    return;
                }

                // Ensure invoice id is present on DTO to send to server
                _CheckercompleteDto.InvoiceId = _invoiceDto.Id;

                // Bounds & recalculation
                decimal total = _invoiceDto.InvoiceTotalValue;
                decimal approved = _CheckercompleteDto.CheckerApprovedAmount.GetValueOrDefault(0m);

                if (approved < 0m)
                {
                    Snackbar.Add("Approved amount cannot be negative.", Severity.Warning);
                    return;
                }

                if (approved > total)
                {
                    Snackbar.Add("Approved amount cannot exceed invoice total.", Severity.Warning);
                    // keep DTO/UI consistent by clamping and showing recalculation
                    approved = total;
                    _CheckercompleteDto.CheckerApprovedAmount = approved;
                    _CheckercompleteDto.CheckerWithheldAmount = total - approved;
                    StateHasChanged();
                    return;
                }

                _CheckercompleteDto.CheckerWithheldAmount = total - approved;

                // Call repository to persist changes (ensure IInvoiceRepository is injected on this component)
                await InvoiceRepository.UpdateInvoiceCheckerReview(_CheckercompleteDto);

                // Re-fetch fresh invoice from server so UI reflects server-side values
                var refreshed = await InvoiceRepository.GetInvoiceById(_invoiceDto.Id);
                if (refreshed != null)
                {
                    _invoiceDto = refreshed;

                    // Update local complete DTO from refreshed invoice so the section shows server values
                    _CheckercompleteDto.CheckerApprovedAmount = refreshed.CheckerApprovedAmount;
                    _CheckercompleteDto.CheckerWithheldAmount = refreshed.CheckerWithheldAmount;
                    _CheckercompleteDto.CheckerWithheldReason = refreshed.CheckerWithheldReason;
                    _CheckercompleteDto.CheckerReviewStatus = refreshed.CheckerReviewStatus;
                    _CheckercompleteDto.CheckerReviewComment = refreshed.CheckerReviewComment;
                    _CheckercompleteDto.CheckerID = refreshed.CheckerID ?? _CheckercompleteDto.CheckerID;
                }

                Snackbar.Add("Checker review saved successfully.", Severity.Success);

                // Notify parent/handlers if implemented
                if (this is IInvoiceCheckerReviewHandlers handlers)
                    await handlers.SaveAsync();

                StateHasChanged();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error saving checker review for Invoice ID {InvoiceId}", _invoiceDto?.Id);
                Snackbar.Add("An error occurred while saving the checker review. Please try again.", Severity.Error);
            }
        }

        // Keep Cancel to call handlers if available
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
