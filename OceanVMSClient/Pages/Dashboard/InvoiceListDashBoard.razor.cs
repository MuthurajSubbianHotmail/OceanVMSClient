using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using OceanVMSClient.Features;
using OceanVMSClient.Helpers;
using OceanVMSClient.HttpRepo.Authentication;
using OceanVMSClient.HttpRepoInterface.InvoiceModule;
using OceanVMSClient.Pages.InviceModule;
using Shared.DTO.POModule;
using System.Runtime.CompilerServices;

namespace OceanVMSClient.Pages.Dashboard
{
    public partial class InvoiceListDashBoard
    {
        [Inject] public IInvoiceRepository Repository { get; set; } = default!;
        [Inject] public HttpInterceptorService Interceptor { get; set; } = default!;
        [Inject] public NavigationManager Navigation { get; set; } = default!;
        [Inject ] public ILocalStorageService LocalStorage { get; set; } = default!;
        [CascadingParameter] public Task<AuthenticationState> AuthenticationStateTask { get; set; } = default!;

        private MudTable<InvoiceDto>? _table;
        private InvoiceParameters _invoiceParameters = new InvoiceParameters();
        // user context
        private string? _userType;
        private Guid? _vendorId;
        private Guid? _employeeId;

        private string invoiceViewPage = string.Empty;
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

        private string FormatDate(DateTime? date)
        {
            return date.HasValue ? date.Value.ToString("dd-MMM-yy") : string.Empty;
        }

        private async Task<TableData<InvoiceDto>> GetServerData(TableState state, CancellationToken cancellationToken)
        {
            // ensure interceptor registered if you have one listening for HTTP events
            Interceptor?.RegisterEvent();
            // we want latest 10 Invoices
            _invoiceParameters.PageSize = 10;
            _invoiceParameters.PageNumber = 1;
            // ask server to order by date descending. Adjust the field name if your API expects a different token.
            _invoiceParameters.OrderBy = "invoiceDate desc";
            // fetch data from server
            PagingResponse<InvoiceDto> response;
            if (string.Equals(_userType, "VENDOR", StringComparison.OrdinalIgnoreCase))
            {
                response = await Repository.GetInvoicesByVendorId(_vendorId ?? Guid.Empty, _invoiceParameters);
            }
            else
            {
                response = await Repository.GetInvoicesByApproverEmployeeId(_employeeId ?? Guid.Empty, _invoiceParameters); 
            }

           var items = response.Items.ToList() ?? new List<InvoiceDto>();
            return new TableData<InvoiceDto>()
            {
                Items = items,
                TotalItems = response.MetaData.TotalCount
            };
        }

        private Color GetSLAChipColor(string reviewSLAStatus)
        {
            return reviewSLAStatus.ToLower() switch
            {
                "within sla" => Color.Success,
                "delayed" => Color.Warning,
                "escalated" => Color.Error,
                "NA" => Color.Default,
                _ => Color.Default
            };
        }
    }
}
