using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using OceanVMSClient.Helpers;
using OceanVMSClient.HttpRepo.Authentication;
using OceanVMSClient.HttpRepoInterface.InvoiceModule;
using OceanVMSClient.HttpRepoInterface.PoModule;
using OceanVMSClient.HttpRepoInterface.POModule;
using Shared.DTO.POModule;
using System.ComponentModel.DataAnnotations.Schema;
using static Shared.DTO.POModule.InvAPApproverReviewCompleteDto;
using OceanVMSClient.HttpRepoInterface.VendorRegistration;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace OceanVMSClient.Pages.Dashboard
{
    public partial class VendorDashBoard
    {
        [Inject] public IVendorRepository VendorRepository { get; set; } = default!;
        [Inject] public IPurchaseOrderRepository PORepository { get; set; } = default!;
        [Inject] public IInvoiceRepository InvoiceRepository { get; set; } = default!;
        [Inject] public IVendorRegistrationRepository VendorRegistrationRepository { get; set; } = default!;
        [Inject] public ILocalStorageService LocalStorage { get; set; } = default!;
        [Inject] public HttpInterceptorService Interceptor { get; set; } = default!;
        [Inject] public NavigationManager NavigationManager { get; set; } = default!;
        [CascadingParameter] public Task<AuthenticationState> AuthenticationStateTask { get; set; } = default!;
        private PurchaseOrderParameters _purchaseOrderParameters = new PurchaseOrderParameters();
        public string? VendorRegistrationStatusMessage { get; private set; }

        private string? _userType;
        private Guid? _vendorId;
        private Guid? _employeeId;

        private VendorDto? _vendor;
        public string? VendorName { get; private set; }
        public string? VendorCode { get; private set; }


        private PurchaseOrderInvoiceStatusCountsDto? _invoiceStatusCounts;
        public int? TotalPoCount = 0;
        public int? NotInvoicedPOCount = 0;
        public int? PartInvoicedPOCount = 0;
        public int? FullyInvoicedPOCount = 0;
        public decimal? TotalPOValue = 0;
        public decimal? TotalInvoicedValue = 0;

        public InvoiceStatusCountsDto? InvoiceStatusCounts;
        public int? InvSubmittedCount = 0;
        public int? InvInitiatorCount = 0;
        public int? InvCheckerCount = 0;
        public int? InvValidatorCount = 0;
        public int? InvApproverCount = 0;
        public int? InvAPApproverCount = 0;
        public int? InvApprovedCount = 0;
        public int? InvRejectedCount = 0;
        public int? InvTotalCount = 0;
        public int? InvUnderReviewCount = 0;

        // Auto-refresh fields
        public DateTime? LastRefreshedAt { get; private set; }
        private PeriodicTimer? _refreshTimer;
        private CancellationTokenSource? _autoRefreshCts;
        private Task? _autoRefreshTask;
        private readonly TimeSpan _refreshInterval = TimeSpan.FromMinutes(5);
        private readonly SemaphoreSlim _refreshSemaphore = new SemaphoreSlim(1, 1);
        protected override async Task OnInitializedAsync()
        {
            var authState = await AuthenticationStateTask;
            var user = authState.User;
            var ctx = await user.LoadUserContextAsync(LocalStorage);

            _userType = ctx.UserType;
            _vendorId = ctx.VendorId;
            _employeeId = ctx.EmployeeId;
            await GetVendorDetails();
            await GetPurchaseOrderInvoiceStatusCounts();
            await GetInvoiceStatusCounts();

            // record initial load time
            LastRefreshedAt = DateTime.Now;

            // Start background auto-refresh after initial load
            StartAutoRefresh();

            await base.OnInitializedAsync();
        }

        private async Task GetVendorDetails()
        {
            var vendor = await VendorRepository.GetVendorById(_vendorId.Value);
            VendorName = vendor?.VendorName ?? "Unknown Vendor";
            VendorCode = vendor?.SAPVendorCode ?? "Unknown Code";

            // Clear previous message
            VendorRegistrationStatusMessage = null;

            // Only consult vendor registration when SAPVendorCode is missing/null/empty
            if (string.IsNullOrWhiteSpace(vendor?.SAPVendorCode))
            {
                try
                {
                    // Get the registration form id (method returns Guid)
                    var regFormId = await VendorRegistrationRepository.GetVendorRegistrationByVendorIdAsync(_vendorId.Value);

                    if (regFormId != Guid.Empty)
                    {
                        var reg = await VendorRegistrationRepository.GetVendorRegistrationFormByIdAsync(regFormId);
                        if (reg != null)
                        {
                            // Defensive: use possible property names; adjust if your DTO uses different names
                            var reviewerStatus = reg.ReviewerStatus?.Trim();
                            var reviewStatus = reg.ReviewerStatus?.Trim();
                            var approverStatus = reg.ApproverStatus?.Trim();

                            // Follow the exact logic requested (check reviewer first, then review, then approver)
                            if (!string.IsNullOrWhiteSpace(reviewerStatus) &&
                                reviewerStatus.Equals("Rejected", StringComparison.OrdinalIgnoreCase))
                            {
                                VendorRegistrationStatusMessage = "Rejected during Review";
                            }
                            else if (!string.IsNullOrWhiteSpace(reviewStatus) &&
                                     reviewStatus.Equals("Pending", StringComparison.OrdinalIgnoreCase))
                            {
                                VendorRegistrationStatusMessage = "Pending Review";
                            }
                            else if (!string.IsNullOrWhiteSpace(reviewStatus) &&
                                     reviewStatus.Equals("Approved", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!string.IsNullOrWhiteSpace(approverStatus) &&
                                    approverStatus.Equals("Pending", StringComparison.OrdinalIgnoreCase))
                                {
                                    VendorRegistrationStatusMessage = "Pending Approval";
                                }
                                else if (!string.IsNullOrWhiteSpace(approverStatus) &&
                                         approverStatus.Equals("Rejected", StringComparison.OrdinalIgnoreCase))
                                {
                                    VendorRegistrationStatusMessage = "Rejected By Approver";
                                }
                                else if (!string.IsNullOrWhiteSpace(approverStatus) &&
                                         approverStatus.Equals("Approved", StringComparison.OrdinalIgnoreCase))
                                {
                                    // SAPVendorCode is still null here (we already checked), so show Approved
                                    VendorRegistrationStatusMessage = "Approved";
                                }
                            }
                            else
                            {
                                // fallback: if DTO contains a combined status field you may want to handle it here
                                VendorRegistrationStatusMessage ??= "Registration status unknown";
                            }
                        }
                    }
                }
                catch
                {
                    // swallow errors to avoid breaking the UI; consider logging
                    VendorRegistrationStatusMessage ??= "Registration status unavailable";
                }
            }

            await InvokeAsync(StateHasChanged);
        }
        private async Task GetPurchaseOrderInvoiceStatusCounts()
        {
            if (_vendorId.HasValue)
            {
                var counts = await PORepository.GetPurchaseOrderInvoiceStatusCountsByVendorAsync(_vendorId.Value);
                _invoiceStatusCounts = counts;
                TotalPoCount = _invoiceStatusCounts.TotalPoCount;
                NotInvoicedPOCount = _invoiceStatusCounts.NotInvoicedPOCount;
                PartInvoicedPOCount = _invoiceStatusCounts.PartInvoicedPOCount;
                FullyInvoicedPOCount = _invoiceStatusCounts.FullyInvoicedPOCount;
                TotalPOValue = _invoiceStatusCounts.TotalPOValue;
                TotalInvoicedValue = _invoiceStatusCounts.TotalInvoicedValue;
            }
        }

        private async Task GetInvoiceStatusCounts()
        {
            if (_vendorId.HasValue)
            {
                var counts = await InvoiceRepository.GetInvoiceStatusCountsByVendorAsync(_vendorId.Value);
                InvoiceStatusCounts = counts;
                InvSubmittedCount = InvoiceStatusCounts.SubmittedCount;
                InvInitiatorCount = InvoiceStatusCounts.WithInitiatorCount;
                InvCheckerCount = InvoiceStatusCounts.WithCheckerCount;
                InvValidatorCount = InvoiceStatusCounts.WithValidatorCount;
                InvApproverCount = InvoiceStatusCounts.WithApproverCount;
                InvAPApproverCount = InvoiceStatusCounts.WithAPReviewCount;
                InvApprovedCount = InvoiceStatusCounts.ApprovedCount;
                InvRejectedCount = InvoiceStatusCounts.RejectedCount;
                InvTotalCount = InvoiceStatusCounts.TotalCount;
                InvUnderReviewCount = InvInitiatorCount + InvCheckerCount + InvValidatorCount + InvApproverCount + InvAPApproverCount;
            }

        }

        private double InvoiceApprovalRate
        {
            get
            {
                // Defensive: handle nullable InvApprovedCount and InvTotalCount
                var approved = InvApprovedCount ?? 0;
                var total = InvTotalCount ?? 0;
                if (total <= 0) return 0;
                return Math.Min(100, Math.Round((double)approved * 100.0 / total, 1));
            }
        }

        private async Task OnRefresh()
        {
            // Trigger manual refresh (button)
            await RefreshDataAsync();
        }

        private void OnInvoicesClick()
        {
            // Navigate to invoices page - adjust navigation logic as per your app structure.
            NavigationManager.NavigateTo("invoices");
        }

        private void OnPurchaseOrdersClick()
        {
            NavigationManager.NavigateTo("polist");
        }

        private async Task RefreshDataAsync(CancellationToken cancellationToken = default)
        {
            // Ensure only one refresh runs at a time
            await _refreshSemaphore.WaitAsync(cancellationToken);
            try
            {
                if (_vendorId.HasValue)
                {
                    await GetPurchaseOrderInvoiceStatusCounts();
                    await GetInvoiceStatusCounts();
                    // If you want to re-fetch vendor details as well uncomment:
                    // await GetVendorDetails();
                }
                // update last-refresh timestamp
                LastRefreshedAt = DateTime.Now;

                // Ensure UI update runs on the synchronization context
                await InvokeAsync(StateHasChanged);
            }
            finally
            {
                _refreshSemaphore.Release();
            }
        }

        private void StartAutoRefresh()
        {
            // Prevent multiple timers
            if (_autoRefreshCts != null) return;

            _autoRefreshCts = new CancellationTokenSource();
            _refreshTimer = new PeriodicTimer(_refreshInterval);
            var ct = _autoRefreshCts.Token;

            _autoRefreshTask = Task.Run(async () =>
            {
                try
                {
                    while (await _refreshTimer!.WaitForNextTickAsync(ct))
                    {
                        // Respect cancellation
                        ct.ThrowIfCancellationRequested();
                        await RefreshDataAsync(ct);
                    }
                }
                catch (OperationCanceledException)
                {
                    // expected on shutdown
                }
                catch
                {
                    // swallow or log as appropriate for your app
                }
            }, ct);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_autoRefreshCts != null)
                {
                    _autoRefreshCts.Cancel();
                    _refreshTimer?.Dispose();
                    if (_autoRefreshTask != null)
                    {
                        try
                        {
                            await _autoRefreshTask;
                        }
                        catch { /* ignore */ }
                    }
                    _autoRefreshCts.Dispose();
                    _autoRefreshCts = null;
                }
            }
            finally
            {
                _refreshSemaphore.Dispose();
            }
        }
    }
}
