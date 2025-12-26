using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using MudBlazor;
using OceanVMSClient.HttpRepoInterface.InvoiceModule;
using Shared.DTO.POModule;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OceanVMSClient.Pages.POModule
{
    public partial class InvoiceList
    {
        [Inject] public IInvoiceRepository InvoiceRepository { get; set; } = default!;
        [Inject] public ISnackbar Snackbar { get; set; } = default!;
        [Inject] public ILogger<InvoiceList> Logger { get; set; } = default!;
        private InvoiceParameters _invoiceParameters = new InvoiceParameters();

        // Add this field to the class to fix CS0103
        private MudDateRangePicker? _date_range_picker;

        // ServerData provider for MudTable
        private async Task<TableData<InvoiceDto>> GetServerData(TableState state, CancellationToken cancellationToken)
        {
            try
            {
                // prepare query parameters from UI state
                _invoiceParameters.PageSize = state.PageSize;
                _invoiceParameters.PageNumber = state.Page + 1;
                _invoiceParameters.SAPPONumber = string.IsNullOrWhiteSpace(sapPoNo) ? null : sapPoNo;
                _invoiceParameters.InvoiceRefNo = string.IsNullOrWhiteSpace(invoiceRefNo) ? null : invoiceRefNo;
                _invoiceParameters.VendorName = string.IsNullOrWhiteSpace(vendorName) ? null : vendorName;
                if (minInvoiceTotal.HasValue || maxInvoiceTotal.HasValue)
                {
                    _invoiceParameters.MinTotalValue = minInvoiceTotal;
                    _invoiceParameters.MaxTotalValue = maxInvoiceTotal;
                }
                else
                {
                    _invoiceParameters.MinTotalValue = null;
                    _invoiceParameters.MaxTotalValue = null;
                }
                _invoiceParameters.InvStartDate = _invoice_date_range?.Start ?? default;
                _invoiceParameters.InvEndDate = _invoice_date_range?.End ?? default;

                var response = await InvoiceRepository.GetAllInvoices(_invoiceParameters);

                return new TableData<InvoiceDto>
                {
                    Items = response.Items?.ToList() ?? new List<InvoiceDto>(),
                    TotalItems = response.MetaData?.TotalCount ?? 0
                };
            }
            catch (OperationCanceledException)
            {
                return new TableData<InvoiceDto> { Items = new List<InvoiceDto>(), TotalItems = 0 };
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error loading invoices");
                Snackbar.Add("Failed to load invoices.", Severity.Error);
                return new TableData<InvoiceDto> { Items = new List<InvoiceDto>(), TotalItems = 0 };
            }
        }

        private Task OnSearch()
        {
            if (_table != null)
                return _table.ReloadServerData();
            return Task.CompletedTask;
        }

        private async Task OnDateRangeSearch(DateRange dateRange)
        {
            _invoice_date_range = dateRange;
            if (_date_range_picker != null)
                await _date_range_picker.CloseAsync();
            await OnSearch();
        }

        private async Task ClearDateRange()
        {
            _invoice_date_range = null;
            if (_date_range_picker != null)
                await _date_range_picker.CloseAsync();
            await OnSearch();
        }

        // map invoice status to chip color
        private Color GetInvoiceChipColor(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return Color.Default;

            var s = status.Trim().ToLowerInvariant();
            return s switch
            {
                "approved" or "paid" or "fully invoiced" => Color.Success,
                "rejected" or "declined" or "overdue" => Color.Error,
                "pending" or "awaiting approval" or "awaiting" => Color.Warning,
                "part invoiced" or "partially paid" or "partial" => Color.Info,
                "cancelled" or "void" => Color.Secondary,
                _ => Color.Secondary
            };
        }

        public class InvoiceQueryParameters
        {
            public int PageNumber { get; set; } = 1;
            public int PageSize { get; set; } = 15;
            public string? InvoiceRefNo { get; set; }
            public DateTime? InvoiceFromDate { get; set; }
            public DateTime? InvoiceToDate { get; set; }
            public string? SAPPoNo { get; set; }
            public decimal? MinInvoiceTotal { get; set; }
            public decimal? MaxInvoiceTotal { get; set; }
            public string? VendorName { get; set; }
        }
    }
}