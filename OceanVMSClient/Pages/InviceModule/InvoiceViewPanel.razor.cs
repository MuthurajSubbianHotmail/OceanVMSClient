using Microsoft.AspNetCore.Components;
using MudBlazor;
using Shared.DTO.POModule;
using System.Globalization;

namespace OceanVMSClient.Pages.InviceModule
{
    public partial class InvoiceViewPanel
    {
        [Parameter]
        public InvoiceDto? _invoiceDto { get; set; }

        [Parameter]
        public PurchaseOrderDto? _PODto { get; set; }

        // Route parameter for the invoice to view
        [Parameter]
        public string? InvoiceId { get; set; }

        [CascadingParameter] public Margin _margin { get; set; } = Margin.Dense!;
        [CascadingParameter] public Variant _variant { get; set; } = Variant.Text!;
        [CascadingParameter] public Color _labelColor { get; set; } = Color.Default!;
        [CascadingParameter] public Color _valueColor { get; set; } = Color.Default!;
        [CascadingParameter] public Typo _labelTypo { get; set; } = Typo.subtitle2!;
        [CascadingParameter] public Typo _valueTypo { get; set; } = Typo.body2!;


        private string PurchaseOrderNumber => _PODto?.SAPPONumber ?? "—";
        private string PurchaseOrderDate => _PODto?.SAPPODate is DateTime d ? d.ToString("dd-MMM-yy") : "—";

        // Purchase order details for right-side display

        private string VendorName => _invoiceDto?.VendorName ?? "—";
        private string PoDateText =>
            _PODto?.SAPPODate is DateTime d ? d.ToString("dd-MMM-yy") : "—";

        private string ProjectNameText => _PODto?.ProjectName ?? "—";

        // Format amounts with currency symbol and dot decimal separator
        private static string FormatCurrency(decimal? value)
        {
            if (!value.HasValue) return "—";
            return $"₹{value.Value.ToString("N2", CultureInfo.InvariantCulture)}";
        }

        private string PoValueText => _PODto != null ? FormatCurrency(_PODto.ItemValue) : "₹0.00";

        private string PoTaxText => _PODto != null ? FormatCurrency(_PODto.GSTTotal) : "₹0.00";

        private string PoTotalText => _PODto != null ? FormatCurrency(_PODto.TotalValue) : "₹0.00";

        private string PrevInvoiceCountText => _PODto?.PreviousInvoiceCount?.ToString() ?? "0";

        private string PrevInvoiceValueText => _PODto != null && _PODto.PreviousInvoiceValue.HasValue
            ? FormatCurrency(_PODto.PreviousInvoiceValue.Value)
            : "₹0.00";

        private string InvoiceBalanceValueText => _PODto != null && _PODto.InvoiceBalanceValue.HasValue
            ? FormatCurrency(_PODto.InvoiceBalanceValue.Value)
            : "₹0.00";

        // Keep same mapping used in InvoiceList child row for chip color
        private Color GetInvoiceChipColor(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return Color.Default;

            var s = status.Trim().ToLowerInvariant();
            return s switch
            {
                "approved" or "paid" or "fully invoiced" => Color.Success,
                "rejected" or "declined" or "overdue" => Color.Error,
                "submitted" or "awaiting approval" or "awaiting" => Color.Default,
                "with initiator" or "with checker" or "with validator" or "with approver" or "under review" => Color.Warning,
                "part invoiced" or "partially paid" or "partial" => Color.Info,
                "cancelled" or "void" => Color.Secondary,
                _ => Color.Secondary
            };
        }
    }
}
