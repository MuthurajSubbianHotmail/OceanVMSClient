using Microsoft.AspNetCore.Components;
using MudBlazor;
using Shared.DTO.POModule;

namespace OceanVMSClient.Pages.InviceModule
{
    public partial class InvoiceInitiatorReview
    {
        [Parameter]
        public InvoiceDto? _invoiceDto { get; set; }
        private InvInitiatorReviewCompleteDto _completeDto { get; set; } = new InvInitiatorReviewCompleteDto();
        // Helper: format nullable decimal as currency
        private string FormatCurrency(decimal? value) => value?.ToString("C") ?? "-";

        // Decide chip color from status text (safe when null)
        private Color GetStatusColor(string? status)
        {
            return status switch
            {
                null => Color.Default,
                var s when s.Equals("Approved", StringComparison.OrdinalIgnoreCase) => Color.Success,
                var s when s.Equals("Withheld", StringComparison.OrdinalIgnoreCase) => Color.Warning,
                var s when s.Equals("Rejected", StringComparison.OrdinalIgnoreCase) => Color.Error,
                _ => Color.Info
            };
        }

        // Decide SLA chip color
        private Color GetSLAColor(string? slaStatus)
        {
            return slaStatus switch
            {
                null => Color.Default,
                var s when s.Equals("Delayed", StringComparison.OrdinalIgnoreCase) => Color.Warning,
                var s when s.Equals("NA", StringComparison.OrdinalIgnoreCase) => Color.Default,
                var s when s.Equals("Within SLA", StringComparison.OrdinalIgnoreCase) => Color.Success,
                _ => Color.Info
            };
        }

        // Action handlers - these rely on the page's code-behind to implement actual save/cancel behavior.
        private Task SaveDecision()
        {
            // call into code-behind if present (e.g., OnSave or similar).
            // If you have a Save method in the code-behind, call it here instead.
           
            if (this is IInvoiceInitiatorReviewHandlers handlers)
                return handlers.SaveAsync();

            // Fallback: no-op
            return Task.CompletedTask;
        }

        private Task Cancel()
        {
            if (this is IInvoiceInitiatorReviewHandlers handlers)
                return handlers.CancelAsync();

            return Task.CompletedTask;
        }

        // Interface used to optionally call handlers implemented in code-behind partial class.
        private interface IInvoiceInitiatorReviewHandlers
        {
            Task SaveAsync();
            Task CancelAsync();
        }
    }
}

