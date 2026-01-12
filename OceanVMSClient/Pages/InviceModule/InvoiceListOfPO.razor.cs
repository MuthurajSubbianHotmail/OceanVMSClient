using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using MudBlazor;
using OceanVMSClient.Helpers;
using OceanVMSClient.HttpRepoInterface.InvoiceModule;
using Shared.DTO.POModule;
using Shared.RequestFeatures; // InvoiceParameters lives here

namespace OceanVMSClient.Pages.InviceModule
{
    public partial class InvoiceListOfPO
    {
        [CascadingParameter] public IMudDialogInstance? DialogInstance { get; set; }

        [Parameter]
        public Guid PurchaseOrderId { get; set; }

        private bool isLoading = true;
        private List<InvoiceDto> _invoices = new();
        private PurchaseOrderDto? PurchaseOrderDetails;

        private string PoNumberText => PurchaseOrderDetails != null ? PurchaseOrderDetails.SAPPONumber ?? "—" : "—";
        private string PoDateText => PurchaseOrderDetails?.SAPPODate is DateTime d ? d.ToString("dd-MMM-yy") : "—";
        private string VendorText => PurchaseOrderDetails?.VendorName ?? "—";
        private string TotalValueText => PurchaseOrderDetails != null ? PurchaseOrderDetails.TotalValue.ToString("N2") : "0.00";
        private string InvoiceBalanceValueText => PurchaseOrderDetails != null && PurchaseOrderDetails.InvoiceBalanceValue.HasValue ? PurchaseOrderDetails.InvoiceBalanceValue.Value.ToString("N2") : "0.00";

        [Inject] private IInvoiceRepository InvoiceRepository { get; set; } = default!;
        [Inject] private OceanVMSClient.HttpRepoInterface.PoModule.IPurchaseOrderRepository PurchaseOrderRepository { get; set; } = default!;
        [Inject] private ISnackbar Snackbar { get; set; } = default!;
        [Inject] private ILogger<InvoiceListOfPO>? Logger { get; set; }
        [Inject] private IJSRuntime JS { get; set; } = default!;
        [Inject] public ILocalStorageService LocalStorage { get; set; } = default!;
        [CascadingParameter] public Task<AuthenticationState> AuthenticationStateTask { get; set; } = default!;
        private string invoiceViewPage = string.Empty;
        private string? _userType;
        private Guid? _vendorId;
        private Guid? _employeeId;
        protected override async Task OnParametersSetAsync()
        {
            await LoadDataAsync();
        }

        protected override async Task OnInitializedAsync()
        {
            var authState = await AuthenticationStateTask;
            var user = authState.User;
            var ctx = await user.LoadUserContextAsync(LocalStorage);
            _userType = ctx.UserType;
            _vendorId = ctx.VendorId;
            _employeeId = ctx.EmployeeId;

            try
            {
                if (string.Equals(_userType, "VENDOR", StringComparison.OrdinalIgnoreCase))
                {
                    invoiceViewPage = "invoiceviewvendor";
                }
                else
                {
                    invoiceViewPage = "invoiceview";
                }
            }
            catch
            {
                _userType = null;
                Console.WriteLine("Failed to retrieve user type from claims/local storage.");
            }
            await base.OnInitializedAsync();
        }
        private async Task LoadDataAsync()
        {
            isLoading = true;
            try
            {
                try
                {
                    PurchaseOrderDetails = await PurchaseOrderRepository.GetPurchaseOrderById(PurchaseOrderId);
                }
                catch (Exception ex)
                {
                    Logger?.LogWarning(ex, "Failed to load purchase order details for {PoId}", PurchaseOrderId);
                }

                var invoiceParams = new InvoiceParameters { PageNumber = 1, PageSize = 1000 };
                var resp = await InvoiceRepository.GetInvoicesByPurchaseOrderId(PurchaseOrderId, invoiceParams);
                _invoices = resp.Items?.ToList() ?? new List<InvoiceDto>();
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Error loading invoices for PO {PoId}", PurchaseOrderId);
                Snackbar.Add("Failed to load invoices.", Severity.Error);
                _invoices = new List<InvoiceDto>();
            }
            finally
            {
                isLoading = false;
                StateHasChanged();
            }
        }

        private void Close()
        {
            // Always close the dialog when used as dialog
            if (DialogInstance != null)
            {
                DialogInstance.Cancel();
                return;
            }
        }

        private void ViewInvoice(Guid id)
        {
            if (DialogInstance != null)
            {
                DialogInstance.Close(DialogResult.Ok(id));
                return;
            }
        }

        private async Task JSInvokeOpen(string url)
        {
            try
            {
                await JS.InvokeVoidAsync("open", url, "_blank");
            }
            catch (Exception ex)
            {
                Logger?.LogWarning(ex, "Failed to open invoice URL");
            }
        }

        private string FormatDate(DateTime? d) => d.HasValue ? d.Value.ToString("dd-MMM-yy") : string.Empty;

        private Color GetInvoiceChipColor(string? status)
        {
            if (string.IsNullOrWhiteSpace(status)) return Color.Default;
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
