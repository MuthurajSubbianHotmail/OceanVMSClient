using Microsoft.AspNetCore.Components;
using MudBlazor;
using OceanVMSClient.HttpRepo.POModule;
using OceanVMSClient.HttpRepoInterface.POModule;
using Shared.DTO.POModule;

namespace OceanVMSClient.Pages.InviceModule
{
    public partial class InvoiceInitiatorReview
    {
        // Component parameters (inputs)
        [Parameter] public InvoiceDto? _invoiceDto { get; set; }
        [Parameter] public PurchaseOrderDto? _PODto { get; set; }

        // Internal DTO used when completing initiator review
        private InvInitiatorReviewCompleteDto _completeDto { get; set; } = new InvInitiatorReviewCompleteDto();

        // Cascading parameters (UI theme / user context)
        [CascadingParameter] public Margin _margin { get; set; } = Margin.Dense;
        [CascadingParameter] public Variant _variant { get; set; } = Variant.Text;
        [CascadingParameter] public Color _labelColor { get; set; } = Color.Default;
        [CascadingParameter] public Color _valueColor { get; set; } = Color.Default;
        [CascadingParameter] public Typo _labelTypo { get; set; } = Typo.subtitle2;
        [CascadingParameter] public Typo _valueTypo { get; set; } = Typo.body2;

        // Logged-in user context (supplied by parent)
        [CascadingParameter] public Guid _LoggedInEmployeeID { get; set; } = Guid.Empty;
        [CascadingParameter] public string _LoggedInUserType { get; set; } = string.Empty;
        [CascadingParameter] public Guid _LoggedInVendorID { get; set; } = Guid.Empty;
        [CascadingParameter] public string _CurrentRoleName { get; set; } = string.Empty;
        [CascadingParameter] public bool _isInvAssigned { get; set; } = false;

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

        #region Permission / editability helpers

        /// <summary>
        /// Returns the boolean value of the nullable flag on the DTO.
        /// Safe to call when the DTO or the property is null.
        /// </summary>
        private bool IsInitiatorReviewRequired => _invoiceDto?.IsInitiatorReviewRequired ?? false;

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
            if (!IsInitiatorReviewRequired)
                return false;

            if (!_isInvAssigned)
                return false;

            if (string.IsNullOrWhiteSpace(_CurrentRoleName) || !_CurrentRoleName.Contains("Initiator", StringComparison.OrdinalIgnoreCase))
                return false;

            // If the initiator review status indicates completion/blocking, disallow edit
            if (string.Equals(_invoiceDto?.InitiatorReviewStatus, "Completed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(_invoiceDto?.InitiatorReviewStatus, "Approved", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(_invoiceDto?.InitiatorReviewStatus, "Rejected", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Returns true when other fields (withheld amount/reason/remarks/status) should be editable.
        /// Default: editable when initiator review is NOT required (so full edit available).
        /// </summary>
        private bool CanEditOtherFields() => !IsInitiatorReviewRequired && _isInvAssigned;

        #endregion

        #region Actions

        private Task SaveDecision()
        {
            if (this is IInvoiceInitiatorReviewHandlers handlers)
                return handlers.SaveAsync();

            return Task.CompletedTask;
        }

        private Task Cancel()
        {
            if (this is IInvoiceInitiatorReviewHandlers handlers)
                return handlers.CancelAsync();

            return Task.CompletedTask;
        }

        private interface IInvoiceInitiatorReviewHandlers
        {
            Task SaveAsync();
            Task CancelAsync();
        }

        #endregion
    }
}

