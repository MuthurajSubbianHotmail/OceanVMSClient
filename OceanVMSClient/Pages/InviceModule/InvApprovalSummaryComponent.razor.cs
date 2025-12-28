using Microsoft.AspNetCore.Components;
using MudBlazor;
using Shared.DTO.POModule;

namespace OceanVMSClient.Pages.InviceModule
{
    public partial class InvApprovalSummaryComponent
    {
        [Parameter] public InvoiceDto? _invoiceDto { get; set; }
        [Parameter] public PurchaseOrderDto? _PODto { get; set; }
        [Parameter] public string? CurrentTab { get; set; } = null;
        // Row model for table display
        private sealed class ApprovalRow
        {
            public string Role { get; init; } = string.Empty;
            public string Subtitle { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
            public string Status { get; init; } = string.Empty;
            public decimal? Approved { get; init; }
            public decimal? Withheld { get; init; }
        }

        // Build rows from invoice DTO
        private IEnumerable<ApprovalRow> ApprovalRows
        {
            get
            {
                if (_invoiceDto == null) return Array.Empty<ApprovalRow>();

                var rows = new List<ApprovalRow>
                {
                    new ApprovalRow
                    {
                        Role = "Initiator",
                        Subtitle = "Initiator review",
                        Status = _invoiceDto.InitiatorReviewStatus,
                        Name = _invoiceDto.InitiatorReviewerName ?? string.Empty,
                        Approved = _invoiceDto.InitiatorApprovedAmount,
                        Withheld = _invoiceDto.InitiatorWithheldAmount
                    },
                    new ApprovalRow
                    {
                        Role = "Checker",
                        Subtitle = "Checker review",
                        Status = _invoiceDto.CheckerReviewStatus,
                        Name = _invoiceDto.CheckerName ?? string.Empty,
                        Approved = _invoiceDto.CheckerApprovedAmount,
                        Withheld = _invoiceDto.CheckerWithheldAmount
                    },
                    new ApprovalRow
                    {
                        Role = "Validator",
                        Subtitle = "Validator review",
                        Status = _invoiceDto.ValidatorReviewStatus,
                        Name = _invoiceDto.ValidatorName ?? string.Empty,
                        Approved = _invoiceDto.ValidatorApprovedAmount,
                        Withheld = _invoiceDto.ValidatorWithheldAmount
                    },
                    new ApprovalRow
                    {
                        Role = "Approver",
                        Subtitle = "Approver review",
                        Status = _invoiceDto.ApproverReviewStatus,
                        Name = _invoiceDto.ApproverName ?? string.Empty,
                        Approved = _invoiceDto.ApproverApprovedAmount,
                        Withheld = _invoiceDto.ApproverWithheldAmount
                    },
                    new ApprovalRow
                    {
                        Role = "AP Approver",
                        Subtitle = "Accounts Payable",
                        Status = _invoiceDto.APReviewStatus,
                        Name = _invoiceDto.APReviewerName ?? string.Empty,
                        Approved = _invoiceDto.APApprovedAmount,
                        Withheld = _invoiceDto.APWithheldAmount
                    }
                };

                return rows;
            }
        }

        private static string FormatAmount(decimal? value) => value.HasValue ? value.Value.ToString("N2") : "—";

        private static string GetInitials(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1) return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpperInvariant();
            return (parts[0][0].ToString() + parts[^1][0].ToString()).ToUpperInvariant();
        }

       

      
    }
}
