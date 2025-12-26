using Blazored.LocalStorage;
using Entities.Models.POModule;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using OceanVMSClient.HttpRepoInterface.InvoiceModule;
using OceanVMSClient.HttpRepoInterface.PoModule;
using OceanVMSClient.HttpRepoInterface.POModule;
using Shared.DTO.POModule;
using System.Runtime.CompilerServices;
using System.Security.Claims;

namespace OceanVMSClient.Pages.InviceModule
{
    public partial class NewInvoice
    {
        [CascadingParameter]
        public Task<AuthenticationState> AuthState { get; set; } = default!;


        private InvoiceDto _invoiceDto = new InvoiceDto();
        private InvoiceForCreationDto _invoiceForCreationDto = new InvoiceForCreationDto();
        [Inject] public NavigationManager NavigationManager { get; set; }
        [Inject] public ILocalStorageService LocalStorage { get; set; } = default!;
        [Inject] public ILogger<NewInvoice> Logger { get; set; }
        [Inject] public IInvoiceRepository InvoiceRepository { get; set; }
        [Inject] public IVendorRepository VendorRepository { get; set; }
        [Inject] public IPurchaseOrderRepository PurchaseOrderRepository { get; set; }

        // Route parameters received from the router are strings in some scenarios
        // (avoid InvalidCast when Blazor supplies boxed string). Parse to Guid below.
        [Parameter]
        public string? VendorId { get; set; }

        [Parameter]
        public string? PurchaseOrderId { get; set; }

        private Guid VendorID;
        private Guid PurchaseOrderID;
        private bool isVendorProvided = false;
        private bool isPurchaseOrderProvided = false;
        private string VendorName = string.Empty;
        private string PurchaseOrderNumber = string.Empty;
        private DateTime PurchaseOrderDate = DateTime.Today;

        // user context
        private string? _userType;
        private Guid? _vendorId;
        private Guid? _vendorContactId;
        private Guid? _employeeId;

        protected override async Task OnParametersSetAsync()
        {
            await base.OnParametersSetAsync();

            try
            {
                // Parse the string route parameters safely to Guid
                if (!string.IsNullOrWhiteSpace(VendorId))
                {
                    if (Guid.TryParse(VendorId, out var parsedVendorId))
                    {
                        VendorID = parsedVendorId;
                        _invoiceDto.VendorId = VendorID;
                        //_invoiceForCreationDto.VendorId = VendorID;
                        isVendorProvided = true;

                        var vendor = await VendorRepository.GetVendorById(VendorID);
                        if (vendor != null)
                        {
                            VendorName = vendor.VendorName;
                        }
                        else
                        {
                            Logger.LogWarning("Vendor with ID {VendorID} not found", VendorID);
                        }
                    }
                    else
                    {
                        Logger.LogWarning("VendorId route parameter could not be parsed as GUID: {VendorId}", VendorId);
                    }
                }

                if (!string.IsNullOrWhiteSpace(PurchaseOrderId))
                {
                    if (Guid.TryParse(PurchaseOrderId, out var parsedPoId))
                    {
                        PurchaseOrderID = parsedPoId;
                        _invoiceDto.PurchaseOrderId = PurchaseOrderID;
                        _invoiceForCreationDto.PurchaseOrderId = PurchaseOrderID;
                        isPurchaseOrderProvided = true;

                        var purchaseOrder = await PurchaseOrderRepository.GetPurchaseOrderById(PurchaseOrderID);
                        if (purchaseOrder != null)
                        {
                            PurchaseOrderNumber = purchaseOrder.SAPPONumber;
                            PurchaseOrderDate = purchaseOrder.SAPPODate;
                        }
                        else
                        {
                            Logger.LogWarning("Purchase Order with ID {PurchaseOrderID} not found", PurchaseOrderID);
                        }
                    }
                    else
                    {
                        Logger.LogWarning("PurchaseOrderId route parameter could not be parsed as GUID: {PurchaseOrderId}", PurchaseOrderId);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error handling route parameters in OnParametersSetAsync");
            }
        }

        protected override async Task OnInitializedAsync()
        {
            try
            {
                // Keep existing query-string support (in case vendorId/purchaseOrderId are supplied as query params)
                var uri = NavigationManager.ToAbsoluteUri(NavigationManager.Uri);
                if (QueryHelpers.ParseQuery(uri.Query).TryGetValue("vendorId", out var vendorId) && !StringValues.IsNullOrEmpty(vendorId))
                {
                    VendorID = Guid.Parse(vendorId.ToString());
                    _invoiceDto.VendorId = VendorID;
                    //_invoiceForCreationDto.VendorId = VendorID;
                    isVendorProvided = true;
                    var vendor = await VendorRepository.GetVendorById(VendorID);
                    if (vendor != null)
                    {
                        VendorName = vendor.VendorName;
                    }
                    else
                    {
                        Logger.LogWarning("Vendor with ID {VendorID} not found", VendorID);
                    }
                }
                if (QueryHelpers.ParseQuery(uri.Query).TryGetValue("purchaseOrderId", out var purchaseOrderId) && !StringValues.IsNullOrEmpty(purchaseOrderId))
                {
                    PurchaseOrderID = Guid.Parse(purchaseOrderId.ToString());
                    _invoiceForCreationDto.PurchaseOrderId = PurchaseOrderID;
                    _invoiceDto.PurchaseOrderId = PurchaseOrderID;
                    isPurchaseOrderProvided = true;
                    var purchaseOrder = await PurchaseOrderRepository.GetPurchaseOrderById(PurchaseOrderID);
                    if (purchaseOrder != null)
                    {
                        PurchaseOrderNumber = purchaseOrder.SAPPONumber;
                        PurchaseOrderDate = purchaseOrder.SAPPODate;
                    }
                    else
                    {
                        Logger.LogWarning("Purchase Order with ID {PurchaseOrderID} not found", PurchaseOrderID);
                    }
                }

                try
                {
                    await LoadUserContextAsync();
                    if (!string.IsNullOrWhiteSpace(_userType) && _userType.ToUpper() == "EMPLOYEE")
                    {
                        _invoiceForCreationDto.InvoiceUploaderEmpID = _employeeId;
                        _invoiceForCreationDto.InvoiceUploader = "EMPLOYEE";
                    }
                    else if (!string.IsNullOrWhiteSpace(_userType) && _userType.ToUpper() == "VENDOR")
                    {
                        _invoiceForCreationDto.InvoiceUploader = "VENDOR";
                    }
                }
                catch
                {
                    _userType = null;
                    Console.WriteLine("Failed to retrieve user type from claims/local storage.");
                }

            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error initializing NewInvoice component");
            }
        }

        private async Task CreateInvoiceAsync()
        {
            try
            {
                var createdInvoice = await InvoiceRepository.CreateInvoice(_invoiceForCreationDto);
                if (createdInvoice != null)
                {
                    NavigationManager.NavigateTo($"/InvoiceModule/InvoiceDetails/{createdInvoice.Id}");
                }
                else
                {
                    Logger.LogError("Failed to create invoice. Repository returned null.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error creating new invoice");
            }
        }
        #region User context helpers



        private async Task LoadUserContextAsync()
        {
            var authState = await AuthState;
            var user = authState.User;

            if (user?.Identity?.IsAuthenticated == true)
            {
                _userType = GetClaimValue(user, "userType");
                _vendorId = ParseGuid(GetClaimValue(user, "vendorPK") ?? GetClaimValue(user, "vendorId"));
                _vendorContactId = ParseGuid(GetClaimValue(user, "vendorContactId") ?? GetClaimValue(user, "vendorContact"));
                _employeeId = ParseGuid(GetClaimValue(user, "empPK") ?? GetClaimValue(user, "EmployeeId"));
            }
            else
            {
                NavigationManager.NavigateTo("/");
                return;
            }

            if (string.IsNullOrWhiteSpace(_userType))
            {
                _userType = await LocalStorage.GetItemAsync<string>("userType");
            }

            if (string.Equals(_userType, "VENDOR", StringComparison.OrdinalIgnoreCase))
            {
                _vendorId ??= ParseGuid(await LocalStorage.GetItemAsync<string>("vendorPK"));
                _vendorContactId ??= ParseGuid(await LocalStorage.GetItemAsync<string>("vendorContactId"));
            }
            else
            {
                _employeeId ??= ParseGuid(await LocalStorage.GetItemAsync<string>("empPK"));
            }
        }

        #endregion

        #region Helpers

        private static string? GetClaimValue(ClaimsPrincipal? user, string claimType)
        {
            if (user == null) return null;
            var claim = user.Claims.FirstOrDefault(c => string.Equals(c.Type, claimType, StringComparison.OrdinalIgnoreCase))
                        ?? user.Claims.FirstOrDefault(c => c.Type.EndsWith($"/{claimType}", StringComparison.OrdinalIgnoreCase))
                        ?? user.Claims.FirstOrDefault(c => c.Type.EndsWith(claimType, StringComparison.OrdinalIgnoreCase));
            return claim?.Value;
        }

        private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var g) ? g : (Guid?)null;
        #endregion
    }
}