using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using OceanVMSClient.Helpers;
using OceanVMSClient.HttpRepo.Authentication;
using OceanVMSClient.HttpRepoInterface.InvoiceModule;
using OceanVMSClient.HttpRepoInterface.PoModule;
using Shared.DTO.POModule;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using static Shared.DTO.POModule.InvAPApproverReviewCompleteDto;
using System.Threading;

namespace OceanVMSClient.Pages.Dashboard
{
    public partial class InvReviewerDashBoard : IAsyncDisposable
    {
        [Inject] public IInvoiceRepository InvoiceRepository { get; set; } = default!;
        [Inject] public IPurchaseOrderRepository PORepository { get; set; } = default!;
        
        [Inject] public HttpInterceptorService Interceptor { get; set; } = default!;
        [Inject] public ILocalStorageService LocalStorage { get; set; } = default!;
        [Inject] public AuthenticationStateProvider CustomAuthStateProvider { get; set; } = default!;
        [Inject] public NavigationManager NavigationManager { get; set; } = default!;

        [CascadingParameter] public Task<AuthenticationState> AuthenticationStateTask { get; set; } = default!;
        private PurchaseOrderParameters _purchaseOrderParameters = new PurchaseOrderParameters();
        private InvoiceParameters _invoiceParameters = new InvoiceParameters();
        private string? _userType;
        private Guid? _vendorId;
        private Guid? _employeeId;

        private PurchaseOrderInvoiceStatusCountsDto? _invoiceStatusCounts;
        public int? TotalPoCount = 0;
        public int? NotInvoicedPOCount = 0;
        public int? PartInvoicedPOCount = 0;
        public int? FullyInvoicedPOCount = 0;
        public decimal? TotalPOValue = 0;
        public decimal? TotalInvoicedValue = 0;

        public InvoiceStatusSlaCountsDto? InvoiceStatusSlaCounts { get; set; }
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

        // Auto-refresh
        private PeriodicTimer? _refreshTimer;
        private CancellationTokenSource? _autoRefreshCts;
        private Task? _autoRefreshTask;
        private readonly TimeSpan _refreshInterval = TimeSpan.FromMinutes(5);
        private readonly SemaphoreSlim _refreshSemaphore = new SemaphoreSlim(1, 1);

        public DateTime? LastRefreshedAt { get; private set; }
        private string ReviewerName { get; set; } = "Invoice Reviewer";

        private double InvoiceApprovalRate
        {
            get
            {
                var approved = InvApprovedCount ?? 0;
                var total = InvTotalCount ?? 0;
                if (total <= 0) return 0;
                return Math.Min(100, Math.Round((double)approved * 100.0 / total, 1));
            }
        }

        protected override async Task OnInitializedAsync()
        {
            var authState = await AuthenticationStateTask;
            var user = authState.User;
            var ctx = await user.LoadUserContextAsync(LocalStorage);
            _userType = ctx.UserType;
            _employeeId = ctx.EmployeeId;
            if (_employeeId != null)
            {
                await LoadInvoiceStatusSlaCountsAsync();
                await GetPurchaseOrderInvoiceStatusCounts();
            }

            // Initialize reviewer name from claims/localStorage using preferred sources
            await SetReviewerNameFromClaimsOrStorageAsync(user);

            if (_employeeId != null)
            {
                await LoadInvoiceStatusSlaCountsAsync();
                await GetPurchaseOrderInvoiceStatusCounts();
            }

            // record initial load time
            LastRefreshedAt = DateTime.Now;

            // Start background auto-refresh
            StartAutoRefresh();

            // Initialize reviewer name from claims/localStorage using preferred sources
            await SetReviewerNameFromClaimsOrStorageAsync(user);

            // Subscribe to auth state changes ...
            CustomAuthStateProvider.AuthenticationStateChanged += OnAuthenticationStateChanged;
        }

        private static string? GetClaimValueInsensitive(ClaimsPrincipal? user, string claimKey)
        {
            if (user == null) return null;
            var c = user.Claims.FirstOrDefault(x =>
                string.Equals(x.Type, claimKey, StringComparison.OrdinalIgnoreCase)
                || x.Type.EndsWith($"/{claimKey}", StringComparison.OrdinalIgnoreCase)
                || x.Type.EndsWith(claimKey, StringComparison.OrdinalIgnoreCase));
            return c?.Value;
        }

        private async Task SetReviewerNameFromClaimsOrStorageAsync(ClaimsPrincipal? user)
        {
            try
            {
                string? name = null;

                // 1) Prefer explicit FullName claim (added at login)
                name = GetClaimValueInsensitive(user, "FullName") ?? GetClaimValueInsensitive(user, "fullName");

                // 2) If missing, prefer combination of FirstName + LastName (claims or local storage)
                if (string.IsNullOrWhiteSpace(name))
                {
                    var first = GetClaimValueInsensitive(user, "FirstName") ?? GetClaimValueInsensitive(user, ClaimTypes.GivenName);
                    var last = GetClaimValueInsensitive(user, "LastName") ?? GetClaimValueInsensitive(user, ClaimTypes.Surname);
                    if (!string.IsNullOrWhiteSpace(first) || !string.IsNullOrWhiteSpace(last))
                        name = $"{first} {last}".Trim();
                }

                // 3) If still missing, check localStorage persisted fullName (login saved it)
                if (string.IsNullOrWhiteSpace(name))
                {
                    var storedFullName = await LocalStorage.GetItemAsync<string>("fullName");
                    if (!string.IsNullOrWhiteSpace(storedFullName))
                        name = storedFullName;
                }

                // 4) Only if we still don't have a friendly full name, fall back to the generic 'name' / Identity.Name claim
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = GetClaimValueInsensitive(user, "name")
                           ?? GetClaimValueInsensitive(user, ClaimTypes.Name)
                           ?? user?.Identity?.Name;
                }

                if (!string.IsNullOrWhiteSpace(name))
                    ReviewerName = name;
            }
            catch
            {
                // ignore and keep default
            }
        }

        // AuthenticationStateChanged handler to keep UI in sync when auth state updates later
        private void OnAuthenticationStateChanged(Task<AuthenticationState> task)
        {
            _ = InvokeAsync(async () =>
            {
                try
                {
                    var authState = await task;
                    await SetReviewerNameFromClaimsOrStorageAsync(authState.User);
                    StateHasChanged();
                }
                catch
                {
                    // swallow exceptions to avoid breaking UI updates
                }
            });
        }

        private async Task LoadInvoiceStatusSlaCountsAsync()
        {
            if (_employeeId != null)
            {
                var invoiceStatusSlaCounts = await InvoiceRepository.GetInvoiceStatusSlaCountsForApproverAsync(_employeeId.Value);

                // save full DTO for UI binding (table)
                InvoiceStatusSlaCounts = invoiceStatusSlaCounts;

                // existing scalar assignments
                InvSubmittedCount = invoiceStatusSlaCounts.SubmittedCount;
                InvInitiatorCount = invoiceStatusSlaCounts.WithInitiatorCount;
                InvCheckerCount = invoiceStatusSlaCounts.WithCheckerCount;
                InvValidatorCount = invoiceStatusSlaCounts.WithValidatorCount;
                InvApproverCount = invoiceStatusSlaCounts.WithApproverCount;
                InvAPApproverCount = invoiceStatusSlaCounts.WithAPReviewCount;
                InvApprovedCount = invoiceStatusSlaCounts.ApprovedCount;
                InvRejectedCount = invoiceStatusSlaCounts.RejectedCount;
                InvTotalCount = invoiceStatusSlaCounts.TotalCount;
                InvUnderReviewCount = InvInitiatorCount + InvCheckerCount + InvValidatorCount + InvApproverCount + InvAPApproverCount;
            }
        }

        private async Task GetPurchaseOrderInvoiceStatusCounts()
        {
            if (_employeeId.HasValue)
            {
                var counts = await PORepository.GetPurchaseOrderInvoiceStatusCountsByReviewerAsync(_employeeId.Value);
                _invoiceStatusCounts = counts;
                TotalPoCount = _invoiceStatusCounts.TotalPoCount;
                NotInvoicedPOCount = _invoiceStatusCounts.NotInvoicedPOCount;
                PartInvoicedPOCount = _invoiceStatusCounts.PartInvoicedPOCount;
                FullyInvoicedPOCount = _invoiceStatusCounts.FullyInvoicedPOCount;
                TotalPOValue = _invoiceStatusCounts.TotalPOValue;
                TotalInvoicedValue = _invoiceStatusCounts.TotalInvoicedValue;
            }
        }

        private void OnSearch() { }
        private void OpenProfile() { }
        private void OpenSettings() { }
        private void Logout() { }

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
        //private async Task OnRefresh()
        //{
        //    // update timestamp and call any reload logic
        //    await Task.CompletedTask;
        //}

        private async Task RefreshDataAsync()
        {
            await LoadInvoiceStatusSlaCountsAsync();
            await GetPurchaseOrderInvoiceStatusCounts();
            // Optionally, add any additional refresh logic here if needed
        }

        private async Task RefreshDataAsync(CancellationToken cancellationToken = default)
        {
            await _refreshSemaphore.WaitAsync(cancellationToken);
            try
            {
                if (_employeeId != null)
                {
                    await LoadInvoiceStatusSlaCountsAsync();
                    await GetPurchaseOrderInvoiceStatusCounts();
                    // add any other refresh work here
                }

                LastRefreshedAt = DateTime.Now;

                await InvokeAsync(StateHasChanged);
            }
            finally
            {
                _refreshSemaphore.Release();
            }
        }

        private void StartAutoRefresh()
        {
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
                        ct.ThrowIfCancellationRequested();
                        await RefreshDataAsync(ct);
                    }
                }
                catch (OperationCanceledException)
                {
                    // expected during shutdown
                }
                catch
                {
                    // swallow or log as appropriate
                }
            }, ct);
        }
        public async ValueTask DisposeAsync()
        {
            try
            {
                try
                {
                    CustomAuthStateProvider.AuthenticationStateChanged -= OnAuthenticationStateChanged;
                }
                catch { /* ignore */ }

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
