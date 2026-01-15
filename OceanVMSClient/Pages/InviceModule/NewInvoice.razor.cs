using Blazored.LocalStorage;
using Entities.Models.POModule;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using OceanVMSClient.HttpRepoInterface.InvoiceModule;
using OceanVMSClient.HttpRepoInterface.PoModule;
using OceanVMSClient.HttpRepoInterface.POModule;
using Shared.DTO.POModule;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using MudBlazor;
using System.Globalization; // added

namespace OceanVMSClient.Pages.InviceModule
{
    public partial class NewInvoice
    {
        [CascadingParameter]
        public Task<AuthenticationState> AuthState { get; set; } = default!;

        private InvoiceDto _invoiceDto = new InvoiceDto();
        private InvoiceForCreationDto _invoiceForCreationDto = new InvoiceForCreationDto();
        [Inject] public NavigationManager NavigationManager { get; set; }
        [Inject] public ILocalStorageService LocalStorage { get; set; } = default!
;
        [Inject] public ILogger<NewInvoice> Logger { get; set; }
        [Inject] public IInvoiceRepository InvoiceRepository { get; set; }
        [Inject] public IVendorRepository VendorRepository { get; set; }
        [Inject] public IPurchaseOrderRepository PurchaseOrderRepository { get; set; }

        // Inject dialog service
        [Inject] public IDialogService DialogService { get; set; } = default!;

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

        // Selected vendor instance bound to the MudAutocomplete
        private VendorDto? _selectedVendor;
        private VendorDto? SelectedVendor
        {
            get => _selectedVendor;
            set
            {
                if (_selectedVendor != value)
                {
                    _selectedVendor = value;
                    if (_selectedVendor?.Id.HasValue == true)
                    {
                        VendorID = _selectedVendor.Id.Value;
                        _invoiceDto.VendorId = VendorID;
                        isVendorProvided = true;
                        VendorName = _selectedVendor.VendorName ?? string.Empty;
                    }
                    else
                    {
                        VendorID = Guid.Empty;
                        _invoiceDto.VendorId = VendorID;
                        isVendorProvided = false;
                        VendorName = string.Empty;
                    }
                }
            }
        }

        // Purchase order details for right-side display
        private PurchaseOrderDto? PurchaseOrderDetails { get; set; }

        private string PoDateText =>
            PurchaseOrderDetails?.SAPPODate is DateTime d ? d.ToString("dd-MMM-yy") : "—";

        private string ProjectNameText => PurchaseOrderDetails?.ProjectName ?? "—";

        // Format PO amounts as currency with dot decimal separator and ₹ symbol.
        // Uses InvariantCulture to ensure '.' as decimal separator and two decimals.
        private static string FormatCurrencyWithSymbol(decimal value) =>
            $"₹{value.ToString("N2", CultureInfo.InvariantCulture)}";

        private string PoValueText => PurchaseOrderDetails != null
            ? FormatCurrencyWithSymbol(PurchaseOrderDetails.ItemValue)
            : "₹0.00";

        private string PoTaxText => PurchaseOrderDetails != null
            ? FormatCurrencyWithSymbol(PurchaseOrderDetails.GSTTotal)
            : "₹0.00";

        private string PoTotalText => PurchaseOrderDetails != null
            ? FormatCurrencyWithSymbol(PurchaseOrderDetails.TotalValue)
            : "₹0.00";

        private string PrevInvoiceCountText => PurchaseOrderDetails?.PreviousInvoiceCount?.ToString() ?? "0";

        // Previous invoice value with currency symbol
        private string PrevInvoiceValueText => PurchaseOrderDetails != null && PurchaseOrderDetails.PreviousInvoiceValue.HasValue
            ? FormatCurrencyWithSymbol(PurchaseOrderDetails.PreviousInvoiceValue.Value)
            : "₹0.00";

        // Invoice balance with currency symbol
        private string InvoiceBalanceValueText => PurchaseOrderDetails != null && PurchaseOrderDetails.InvoiceBalanceValue.HasValue
            ? FormatCurrencyWithSymbol(PurchaseOrderDetails.InvoiceBalanceValue.Value)
            : "₹0.00";
        private string PreviousInvoiceValueText => PurchaseOrderDetails != null && PurchaseOrderDetails.PreviousInvoiceValue.HasValue
            ? FormatCurrencyWithSymbol(PurchaseOrderDetails.PreviousInvoiceValue.Value)
            : "₹0.00";

        // EditContext + validation store
        private EditContext? _editContext;
        private ValidationMessageStore? _messageStore;

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
                        isVendorProvided = true;

                        var vendor = await VendorRepository.GetVendorById(VendorID);
                        if (vendor != null)
                        {
                            VendorName = vendor.VendorName;
                            SelectedVendor = vendor;
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
                            PurchaseOrderDetails = purchaseOrder;
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

        // Search function used by MudAutocomplete.
        private async Task<IEnumerable<VendorDto>> SearchVendors(string value, CancellationToken token)
        {
            try
            {
                if (token.IsCancellationRequested)
                    return Enumerable.Empty<VendorDto>();

                if (string.IsNullOrWhiteSpace(value))
                    return Enumerable.Empty<VendorDto>();

                var vendorParameters = new VendorParameters
                {
                    PageNumber = 1,
                    PageSize = 10,
                    SearchTerm = value
                };

                var response = await VendorRepository.GetAllVendors(vendorParameters);
                var items = response?.Items ?? Enumerable.Empty<VendorDto>();

                // apply client-side filter to ensure match
                return items.Where(x => !string.IsNullOrWhiteSpace(x?.VendorName)
                                        && x.VendorName.Contains(value, StringComparison.OrdinalIgnoreCase));
            }
            catch (OperationCanceledException)
            {
                return Enumerable.Empty<VendorDto>();
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "SearchVendors failed");
                return Enumerable.Empty<VendorDto>();
            }
        }

        protected override async Task OnInitializedAsync()
        {
            // create EditContext and ValidationMessageStore early so form renders correctly
            _editContext = new EditContext(_invoiceForCreationDto);
            _messageStore = new ValidationMessageStore(_editContext);
            _editContext.OnFieldChanged += (sender, args) =>
            {
                // clear messages for the field when user edits it
                _messageStore?.Clear(args.FieldIdentifier);
            };

            try
            {
                // Keep existing query-string support (in case vendorId/purchaseOrderId are supplied as query params)
                var uri = NavigationManager.ToAbsoluteUri(NavigationManager.Uri);
                if (QueryHelpers.ParseQuery(uri.Query).TryGetValue("vendorId", out var vendorId) && !StringValues.IsNullOrEmpty(vendorId))
                {
                    VendorID = Guid.Parse(vendorId.ToString());
                    _invoiceDto.VendorId = VendorID;
                    isVendorProvided = true;
                    var vendor = await VendorRepository.GetVendorById(VendorID);
                    if (vendor != null)
                    {
                        VendorName = vendor.VendorName;
                        SelectedVendor = vendor;
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
                        PurchaseOrderDetails = purchaseOrder;
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

        private string? _uploadedFileUrl; // URL returned by the server after upload
        private Task OnInvoiceUploaded(string? url)
        {
            _invoiceForCreationDto.InvoiceFileURL = url ?? string.Empty;
            _uploadedFileUrl = url;
            // clear any validation messages for InvoiceFileURL and refresh validation UI
            _messageStore?.Clear(new FieldIdentifier(_invoiceForCreationDto, nameof(_invoiceForCreationDto.InvoiceFileURL)));
            _editContext?.NotifyValidationStateChanged();

            return Task.CompletedTask;
        }
        private async Task CreateInvoiceAsync()
        {
            try
            {
                // If a file was selected, upload it first and set InvoiceFileURL on DTO
                if (_uploadedInvoiceFile != null && _uploadedInvoiceFile.Length > 0)
                {
                    try
                    {
                        using var content = new MultipartFormDataContent();
                        var fileContent = new ByteArrayContent(_uploadedInvoiceFile);
                        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(_uploadedFileContentType ?? "application/octet-stream");
                        content.Add(fileContent, "file", _uploadedFileName ?? "invoice-file");

                        var url = await InvoiceRepository.UploadInvoiceFile(content);
                        if (!string.IsNullOrWhiteSpace(url))
                        {
                            _invoiceForCreationDto.InvoiceFileURL = url;
                            _uploadedFileUrl = url;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Failed to upload invoice file before creating invoice");
                        _fileValidationMessage = "Failed to upload file. You can try again or create invoice without file.";
                        // abort to let user decide
                        return;
                    }
                }

                var createdInvoice = await InvoiceRepository.CreateInvoice(_invoiceForCreationDto);
                if (createdInvoice != null)
                {
                    NavigationManager.NavigateTo($"/invoices");
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

        // Recalculate invoice total when value or tax changes
        private void RecalculateInvoiceTotal()
        {
            var value = _invoiceForCreationDto.InvoiceValue;
            var tax = _invoiceForCreationDto.InvoiceTaxValue;
            _invoiceForCreationDto.InvoiceTotalValue = value + tax;
        }

        private async Task OnInvoiceValueChanged(decimal? value)
        {
            _invoiceForCreationDto.InvoiceValue = value ?? 0m;
            RecalculateInvoiceTotal();

            // immediate validation: ensure total <= invoice balance (if PO present)
            if (_messageStore != null)
            {
                var field = new FieldIdentifier(_invoiceForCreationDto, nameof(_invoiceForCreationDto.InvoiceTotalValue));
                _messageStore.Clear(field);

                if (PurchaseOrderDetails?.InvoiceBalanceValue.HasValue == true)
                {
                    var balance = PurchaseOrderDetails.InvoiceBalanceValue.Value;
                    if (_invoiceForCreationDto.InvoiceTotalValue > balance)
                    {
                        _messageStore.Add(field, $"Invoice total cannot exceed PO invoice balance ({balance:N2}).");
                    }
                }

                _editContext?.NotifyValidationStateChanged();
            }

            await InvokeAsync(StateHasChanged);
        }

        private async Task OnInvoiceTaxValueChanged(decimal? value)
        {
            _invoiceForCreationDto.InvoiceTaxValue = value ?? 0m;
            RecalculateInvoiceTotal();

            // immediate validation: ensure total <= invoice balance (if PO present)
            if (_messageStore != null)
            {
                var field = new FieldIdentifier(_invoiceForCreationDto, nameof(_invoiceForCreationDto.InvoiceTotalValue));
                _messageStore.Clear(field);

                if (PurchaseOrderDetails?.InvoiceBalanceValue.HasValue == true)
                {
                    var balance = PurchaseOrderDetails.InvoiceBalanceValue.Value;
                    if (_invoiceForCreationDto.InvoiceTotalValue > balance)
                    {
                        _messageStore.Add(field, $"Invoice total cannot exceed PO invoice balance ({balance:N2}).");
                    }
                }

                _editContext?.NotifyValidationStateChanged();
            }

            await InvokeAsync(StateHasChanged);
        }

        private const long MaxFileSize = 3 * 1024 * 1024; // 3 MB
        private byte[]? _uploadedInvoiceFile;
        private string? _uploadedFileName;
        private string? _uploadedFileContentType;
        private string? _fileValidationMessage;

        private async Task OnInputFileChange(InputFileChangeEventArgs e)
        {
            var file = e.File;
            ClearFile();
            if (file == null)
            {
                return;
            }

            if (file.Size > MaxFileSize)
            {
                _fileValidationMessage = "File exceeds 3 MB limit.";
                await InvokeAsync(StateHasChanged);
                return;
            }

            var ct = (file.ContentType ?? string.Empty).ToLowerInvariant();
            if (!(ct == "application/pdf" || ct.StartsWith("image/")))
            {
                _fileValidationMessage = "Only PDF or image files are allowed.";
                await InvokeAsync(StateHasChanged);
                return;
            }

            try
            {
                using var stream = file.OpenReadStream(MaxFileSize);
                using var ms = new System.IO.MemoryStream();
                await stream.CopyToAsync(ms);
                _uploadedInvoiceFile = ms.ToArray();
                _uploadedFileName = file.Name;
                _uploadedFileContentType = file.ContentType;
                _fileValidationMessage = null;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed reading uploaded file");
                _fileValidationMessage = "Failed to read file.";
                ClearFile();
            }

            await InvokeAsync(StateHasChanged);
        }

        private void ClearFile()
        {
            _uploadedInvoiceFile = null;
            _uploadedFileName = null;
            _uploadedFileContentType = null;
            _fileValidationMessage = null;
            _uploadedFileUrl = null;
        }

        // Handle form submit with custom validation
        private async Task HandleSubmit(EditContext editContext)
        {
            // clear previous messages
            _messageStore?.Clear();
            RecalculateInvoiceTotal();

            var valid = ValidateInputs();

            if (!valid)
            {
                _editContext?.NotifyValidationStateChanged();
                return;
            }

            // Show confirmation dialog before creating invoice
            //var confirmed = await ShowConfirmationDialogAsync();
            //if (!confirmed)
            //{
            //    // user cancelled
            //    return;
            //}

            await CreateInvoiceAsync();
        }

        // Show a confirmation dialog summarizing key values; returns true if user confirmed
        //private async Task<bool> ShowConfirmationDialogAsync()
        //{
        //    var parameters = new DialogParameters
        //    {
        //        ["Invoice"] = _invoiceForCreationDto,
        //        ["PurchaseOrder"] = PurchaseOrderDetails,
        //        ["VendorName"] = VendorName,
        //        ["PurchaseOrderNumber"] = PurchaseOrderNumber
        //    };

        //    var options = new DialogOptions
        //    {
        //        MaxWidth = MaxWidth.Small,
        //        FullWidth = true,
        //        CloseButton = true,
        //        //DisableBackdropClick = true
        //    };

        //    var dialogRef = DialogService.Show<ConfirmInvoiceDialog>("Confirm Invoice", parameters, options);
        //    var result = await dialogRef.Result;

        //    // result.Cancelled == false indicates OK; result.Data may contain a boolean
        //    if (result.Cancelled) return false;
        //    if (result.Data is bool b) return b;
        //    return true;
        //}

        // Perform validations and add messages to ValidationMessageStore
        private bool ValidateInputs()
        {
            bool isValid = true;
            if (_messageStore == null || _editContext == null)
                return false;

            // PurchaseOrder: either PurchaseOrderId or PurchaseOrderNumber must be present
            if ((_invoiceForCreationDto.PurchaseOrderId == Guid.Empty)
                && string.IsNullOrWhiteSpace(PurchaseOrderNumber))
            {
                _messageStore.Add(new FieldIdentifier(_invoiceForCreationDto, nameof(_invoiceForCreationDto.PurchaseOrderId)), "Purchase Order is required.");
                isValid = false;
            }

            // Invoice reference required
            if (string.IsNullOrWhiteSpace(_invoiceForCreationDto.InvoiceRefNo))
            {
                _messageStore.Add(new FieldIdentifier(_invoiceForCreationDto, nameof(_invoiceForCreationDto.InvoiceRefNo)), "Invoice reference is required.");
                isValid = false;
            }

            // Invoice total must be > 0
            if (!(_invoiceForCreationDto.InvoiceTotalValue > 0m))
            {
                _messageStore.Add(new FieldIdentifier(_invoiceForCreationDto, nameof(_invoiceForCreationDto.InvoiceTotalValue)), "Invoice total must be greater than zero.");
                isValid = false;
            }

            // Ensure invoice total does not exceed PO invoice balance (when PO details available)
            if (PurchaseOrderDetails?.InvoiceBalanceValue.HasValue == true)
            {
                var balance = PurchaseOrderDetails.InvoiceBalanceValue.Value;
                if (_invoiceForCreationDto.InvoiceTotalValue > balance)
                {
                    _messageStore.Add(new FieldIdentifier(_invoiceForCreationDto, nameof(_invoiceForCreationDto.InvoiceTotalValue)), $"Invoice total cannot exceed PO invoice balance ({balance:N2}).");
                    isValid = false;
                }
            }

            // Invoice file is required (either an uploaded file in memory or an existing file URL)
            var hasInMemoryFile = _uploadedInvoiceFile != null && _uploadedInvoiceFile.Length > 0;
            var hasExistingUrl = !string.IsNullOrWhiteSpace(_invoiceForCreationDto.InvoiceFileURL) || !string.IsNullOrWhiteSpace(_uploadedFileUrl);
            if (!hasInMemoryFile && !hasExistingUrl)
            {
                _messageStore.Add(new FieldIdentifier(_invoiceForCreationDto, nameof(_invoiceForCreationDto.InvoiceFileURL)), "Invoice file is required.");
                isValid = false;
            }
            return isValid;
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