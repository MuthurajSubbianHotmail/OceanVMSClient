using Microsoft.AspNetCore.Components;
using MudBlazor;
using Shared.DTO.POModule;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OceanVMSClient.Pages.InviceModule
{
    public partial class InvStatusComponent
    {
        [Parameter] public InvoiceDto? _invoiceDto { get; set; }
        [Parameter] public PurchaseOrderDto? _PODto { get; set; }
        [Parameter] public string? CurrentTab { get; set; } = null;

        // Styling cascade variables used elsewhere in the app
        public Color _labelColor { get; set; } = Color.Default;

        private string PrevInvoiceCountText => _PODto?.PreviousInvoiceCount?.ToString() ?? "0";
        private string PrevInvoiceValueText => _PODto != null && _PODto.PreviousInvoiceValue.HasValue ? _PODto.PreviousInvoiceValue.Value.ToString("N2") : "0.00";
        private string InvoiceBalanceValueText => _PODto != null && _PODto.InvoiceBalanceValue.HasValue ? _PODto.InvoiceBalanceValue.Value.ToString("N2") : "0.00";

        private string PoValueText => _PODto != null ? _PODto.ItemValue.ToString("N2") : "0.00";
        private string PoTaxText => _PODto != null ? _PODto.GSTTotal.ToString("N2") : "0.00";
        private string PoTotalText => _PODto != null ? _PODto.TotalValue.ToString("N2") : "0.00";

        private string GetFirstReviewStatus()
        {
            if (_invoiceDto == null) return "—";

            // return first non-empty review status in the workflow order
            var list = new[]
            {
                _invoiceDto.InitiatorReviewStatus,
                _invoiceDto.CheckerReviewStatus,
                _invoiceDto.ValidatorReviewStatus,
                _invoiceDto.ApproverReviewStatus,
                _invoiceDto.APReviewStatus
            };

            return list.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "—";
        }
        private Color GetReviewStatusColor()
        {
            var status = GetFirstReviewStatus()?.Trim().ToLowerInvariant();
            return status switch
            {
                "approved" => Color.Success,
                "rejected" => Color.Error,
                "pending" => Color.Warning,
                _ => Color.Info
            };
        }

        private Color GetSLAStatusColor(string? slaStatus)
        {
            if (string.IsNullOrWhiteSpace(slaStatus)) return Color.Default;
            var s = slaStatus.Trim();
            return s.Equals("Delayed", StringComparison.OrdinalIgnoreCase) ? Color.Warning :
                   s.Equals("Within SLA", StringComparison.OrdinalIgnoreCase) ? Color.Success :
                   Color.Default;
        }

        private Color GetInvStatusColor(string? status)
        {
            if (string.IsNullOrWhiteSpace(status)) return Color.Default;
            var s = status.Trim().ToLowerInvariant();
            return s switch
            {
                "approved" or "paid" or "fully invoiced" => Color.Success,
                "rejected" => Color.Error,
                "part invoiced" => Color.Info,
                "over invoiced" => Color.Warning,
                _ => Color.Default
            };
        }
    }
}
