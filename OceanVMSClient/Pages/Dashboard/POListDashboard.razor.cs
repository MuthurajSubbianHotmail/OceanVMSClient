using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using OceanVMSClient.Features;
using OceanVMSClient.Helpers;
using OceanVMSClient.HttpRepo.Authentication;
using OceanVMSClient.HttpRepoInterface.PoModule;
using Shared.DTO.POModule;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OceanVMSClient.Pages.Dashboard
{
    public partial class POListDashboard
    {
        [Inject] public IPurchaseOrderRepository Repository { get; set; } = default!;
        [Inject] public ILocalStorageService LocalStorage { get; set; } = default!;
        [Inject] public HttpInterceptorService Interceptor { get; set; } = default!;

        [CascadingParameter] public Task<AuthenticationState> AuthenticationStateTask { get; set; } = default!;

        private MudTable<PurchaseOrderDto>? _table;
        private PurchaseOrderParameters _purchaseOrderParameters = new PurchaseOrderParameters();

        // user context
        private string? _userType;
        private Guid? _vendorId;
        private Guid? _employeeId;

        protected override async Task OnInitializedAsync()
        {
            var authState = await AuthenticationStateTask;
            var user = authState.User;
            var ctx = await user.LoadUserContextAsync(LocalStorage);

            _userType = ctx.UserType;
            _vendorId = ctx.VendorId;
            _employeeId = ctx.EmployeeId;

            await base.OnInitializedAsync();
        }
        private string FormatDate(DateTime? date)
        {
            return date.HasValue ? date.Value.ToString("dd-MMM-yy") : string.Empty;
        }
        private async Task<TableData<PurchaseOrderDto>> GetServerData(TableState state, CancellationToken cancellationToken)
        {
            // ensure interceptor registered if you have one listening for HTTP events
            Interceptor?.RegisterEvent();

            // we want latest 10 POs
            _purchaseOrderParameters.PageSize = 10;
            _purchaseOrderParameters.PageNumber = 1;
            // ask server to order by date descending. Adjust the field name if your API expects a different token.
            _purchaseOrderParameters.OrderBy = "sapPODate desc";

            PagingResponse<PurchaseOrderDto> response;

            if (string.Equals(_userType, "VENDOR", StringComparison.OrdinalIgnoreCase))
            {
                response = await Repository.GetAllPurchaseOrdersOfVendorAsync(_vendorId ?? Guid.Empty, _purchaseOrderParameters);
            }
            else
            {
                response = await Repository.GetAllPurchaseOrdersOfApproversAsync(_employeeId ?? Guid.Empty, _purchaseOrderParameters);
            }

            var items = response.Items?.ToList() ?? new List<PurchaseOrderDto>();

            return new TableData<PurchaseOrderDto>
            {
                Items = items,
                TotalItems = response.MetaData?.TotalCount ?? items.Count
            };
        }
    }
}