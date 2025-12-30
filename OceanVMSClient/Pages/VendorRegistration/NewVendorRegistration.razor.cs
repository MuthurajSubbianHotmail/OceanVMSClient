using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;
using MudBlazor;
using Entities.Models.Setup;
using Entities.Models.VendorReg;
using OceanVMSClient.HttpRepoInterface.VendorRegistration;
using Shared.DTO.POModule;
using Shared.DTO.VendorReg;
using System.Security.Claims;
using OceanVMSClient.SharedComp;

namespace OceanVMSClient.Pages.VendorRegistration
{
    public partial class NewVendorRegistration
    {
        // --- Cascading parameters / user context ---
        [CascadingParameter] private Task<AuthenticationState>? AuthenticationStateTask { get; set; }

        // --- Injected services ---
        [Inject] public ILogger<NewVendorRegistration> Logger { get; set; } = default!;
        [Inject] public required IVendorRegistrationRepository VendorRegistrationFormRepository { get; set; }
        [Inject] public ICompanyOwnershipRepository CompanyOwnershipRepository { get; set; } = default!;
        [Inject] public IVendorServiceRepository VendorServiceRepository { get; set; } = default!;
        [Inject] public ISnackbar Snackbar { get; set; } = default!;
        [Inject] public NavigationManager NavigationManager { get; set; } = default!;
        [Inject] public IDialogService DialogService { get; set; } = default!;

        // --- Component parameters ---
        [Parameter] public Guid VendorRegistrationFormId { get; set; }

        // --- Model / EditContext for validation ---
        private VendorRegistrationForm _vendorReg = new()
        {
            Id = Guid.NewGuid(),
            ResponderFName = string.Empty,
            ResponderLName = string.Empty,
            ResponderDesignation = string.Empty,
            ResponderMobileNo = string.Empty,
            ResponderEmailId = string.Empty,
            OrganizationName = string.Empty,
            IsMSMERegistered = false,
            CompanyOwnershipId = new Guid("D1E4C053-49B6-410C-BC78-2D54A9991870"),
            CompanyOwnership = new CompanyOwnership
            {
                Id = new Guid("D1E4C053-49B6-410C-BC78-2D54A9991870"),
                CompanyOwnershipType = "Private Limited"
            },
        };
        private EditContext _editContext;
        private ValidationMessageStore? _messageStore;

        // --- Lookups and selection state ---
        private List<CompanyOwnership> _companyOwnerships { get; set; } = new();
        private List<VendorService> _vendorServices { get; set; } = new();
        private IEnumerable<string> _selectedServices { get; set; } = new HashSet<string>();

        // --- UI state ---
        private int _activeStep;
        private bool _completed;
        private bool _isSaving;
        private bool _isReadOnly = false;

        // Dynamic required flags (driven by selected company ownership)
        private bool _isAadharRequired;
        private bool _isPANRequired;
        private bool _isTANRequired;
        private bool _isGSTRequired;
        private bool _isUDYAMRequired;
        private bool _isCINRequired;
        private bool _isPFRequired;
        private bool _isESIRequired;

        // Back-compat property names used by the Razor markup (avoid changing markup)
        // These expose the cleaned-up fields above so existing Razor doesn't need edits.
        private bool _isPANrequired { get => _isPANRequired; set => _isPANRequired = value; }
        private bool _isTANRrequired { get => _isTANRequired; set => _isTANRequired = value; }
        private bool _IsAadharRequired { get => _isAadharRequired; set => _isAadharRequired = value; }

        // convenience: selected ownership text (for UI components that bind to text)
        private string _selectedCompanyOwnershipType = string.Empty;

        // --- ctor: ensure EditContext exists at first render to avoid EditForm errors ---
        public NewVendorRegistration()
        {
            _editContext = new EditContext(_vendorReg);
        }

        // --- Lifecycle ---
        protected override async Task OnInitializedAsync()
        {
            try
            {
                await LoadLookupsAsync();

                if (VendorRegistrationFormId != Guid.Empty)
                {
                    // load existing registration and rebind edit context
                    _vendorReg = await VendorRegistrationFormRepository.GetVendorRegistrationFormByIdAsync(VendorRegistrationFormId);
                    _editContext = new EditContext(_vendorReg);
                }

                // ensure ownership mapping and selected services are in sync
                MapOwnershipSelection();
                MapSelectedServices();

                // validation message store + cleanup on field change
                _messageStore = new ValidationMessageStore(_editContext);
                _editContext.OnFieldChanged += (sender, args) =>
                {
                    _messageStore?.Clear(args.FieldIdentifier);
                    _editContext.NotifyValidationStateChanged();
                };
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Initialization failed for NewVendorRegistration");
                Snackbar.Add("Failed to initialize form lookups.", Severity.Error);
            }
        }

        // --- Initialization helpers ---
        private async Task LoadLookupsAsync()
        {
            var ownershipsTask = CompanyOwnershipRepository.GetAllCompanyOwnershipsAsync();
            var servicesTask = VendorServiceRepository.GetAllVendorServices();

            await Task.WhenAll(ownershipsTask, servicesTask);

            _companyOwnerships = (ownershipsTask.Result ?? new List<CompanyOwnership>()).ToList();
            _vendorServices = (servicesTask.Result ?? new List<VendorService>()).ToList();
        }

        private void MapOwnershipSelection()
        {
            if (_companyOwnerships?.Any() != true)
                return;

            if (_vendorReg.CompanyOwnershipId == Guid.Empty)
            {
                var first = _companyOwnerships.First();
                _vendorReg.CompanyOwnershipId = first.Id;
                _vendorReg.CompanyOwnership = first;
            }
            else
            {
                _vendorReg.CompanyOwnership = _companyOwnerships.FirstOrDefault(c => c.Id == _vendorReg.CompanyOwnershipId)
                                            ?? _vendorReg.CompanyOwnership;
            }

            _selectedCompanyOwnershipType = _vendorReg.CompanyOwnership?.CompanyOwnershipType ?? string.Empty;
            ApplyCompanyOwnershipRequirements(_vendorReg.CompanyOwnershipId);
        }

        private void MapSelectedServices()
        {
            if (string.IsNullOrWhiteSpace(_vendorReg.VendorType))
            {
                _selectedServices = Array.Empty<string>();
                return;
            }

            _selectedServices = _vendorReg.VendorType
                .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct()
                .ToList();
        }

        // --- Ownership change handlers ---
        private Task OnCompanyOwnershipChanged(string companyOwnershipType)
        {
            var match = _companyOwnerships.FirstOrDefault(c => string.Equals(c.CompanyOwnershipType, companyOwnershipType, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                _vendorReg.CompanyOwnershipId = match.Id;
                _vendorReg.CompanyOwnership = match;
                ApplyCompanyOwnershipRequirements(match.Id);
            }
            else
            {
                _vendorReg.CompanyOwnershipId = Guid.Empty;
            }

            StateHasChanged();
            return Task.CompletedTask;
        }

        private void ApplyCompanyOwnershipRequirements(Guid id)
        {
            var sel = _companyOwnerships.FirstOrDefault(c => c.Id == id);
            _isPANRequired = sel?.PAN_Required ?? false;
            _isTANRequired = sel?.TAN_Required ?? false;
            _isGSTRequired = sel?.GST_Required ?? false;
            _isCINRequired = sel?.CIN_Required ?? false;
            _isUDYAMRequired = sel?.UDYAM_Required ?? false;
            _isPFRequired = sel?.PF_Required ?? false;
            _isESIRequired = sel?.ESI_Required ?? false;
            _isAadharRequired = sel?.AADHAR_Required ?? false;
        }

        // --- Submit / validation flow ---
        private async Task HandleSubmit(EditContext editContext)
        {
            // perform DataAnnotations validation manually and collect messages
            var validationResults = new List<ValidationResult>();
            var validationContext = new ValidationContext(_vendorReg, serviceProvider: null, items: null);
            Validator.TryValidateObject(_vendorReg, validationContext, validationResults, validateAllProperties: true);

            // conditional rules driven by ownership flags
            if (_isPANRequired && string.IsNullOrWhiteSpace(_vendorReg.PANNo))
                validationResults.Add(new ValidationResult("PAN is required for the selected ownership.", new[] { nameof(_vendorReg.PANNo) }));

            if (_isAadharRequired && string.IsNullOrWhiteSpace(_vendorReg.AadharNo))
                validationResults.Add(new ValidationResult("Aadhar is required for the selected ownership.", new[] { nameof(_vendorReg.AadharNo) }));

            if (_isGSTRequired && string.IsNullOrWhiteSpace(_vendorReg.GSTNO))
                validationResults.Add(new ValidationResult("GST No is required for the selected ownership.", new[] { nameof(_vendorReg.GSTNO) }));

            var messages = validationResults
                .Where(r => !string.IsNullOrWhiteSpace(r.ErrorMessage))
                .Select(r => r.ErrorMessage!)
                .Distinct()
                .ToList();

            if (messages.Any())
            {
                // show dialog with validation messages only (no inline EditContext messages)
                Snackbar.Add("Please fix validation errors.", Severity.Warning);
                var parameters = new DialogParameters { ["Messages"] = messages };
                var options = new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Small, FullWidth = true };
                DialogService.Show<ValidationErrorsDialog>("Validation errors", parameters, options);
                return;
            }

            // Ask for user confirmation before saving
            var confirmed = await DialogService.ShowMessageBox(
                "Confirm save",
                "Are you sure you want to save this vendor registration?",
                yesText: "Yes",
                noText: "No");

            if (confirmed != true)
            {
                // user cancelled
                return;
            }

            // proceed to save when validation passes and user confirmed
            await CreateVendorRegistrationAsync();
        }

        // --- Save / repository interaction (map to CreateDto) ---
        private async Task CreateVendorRegistrationAsync()
        {
            if (_isSaving) return;
            _isSaving = true;

            try
            {
                var createDto = new VendorRegistrationFormCreateDto
                {
                    OrganizationName = _vendorReg.OrganizationName,
                    ResponderFName = _vendorReg.ResponderFName,
                    ResponderLName = _vendorReg.ResponderLName,
                    ResponderDesignation = _vendorReg.ResponderDesignation,
                    ResponderMobileNo = _vendorReg.ResponderMobileNo,
                    ResponderEmailId = _vendorReg.ResponderEmailId,
                    VendorType = _vendorReg.VendorType,
                    CompanyOwnershipId = _vendorReg.CompanyOwnershipId,
                    RegAddress1 = _vendorReg.RegAddress1,
                    RegAddress2 = _vendorReg.RegAddress2,
                    RegAddress3 = _vendorReg.RegAddress3,
                    RegCity = _vendorReg.RegCity,
                    RegState = _vendorReg.RegState,
                    RegCountry = _vendorReg.RegCountry,
                    RegPIN = _vendorReg.RegPIN,
                    OperAddSameAsReg = _vendorReg.OperAddSameAsReg,
                    BankName = _vendorReg.BankName,
                    BankBranch = _vendorReg.BankBranch,
                    IFSCCode = _vendorReg.IFSCCode,
                    AccountNumber = _vendorReg.AccountNumber,
                    AccountName = _vendorReg.AccountName,
                    PANNo = _vendorReg.PANNo,
                    PANCardURL = _vendorReg.PANCardURL,
                    TANNo = _vendorReg.TANNo,
                    TANCertURL = _vendorReg.TANCertURL,
                    GSTNO = _vendorReg.GSTNO,
                    GSTRegistrationCertURL = _vendorReg.GSTRegistrationCertURL,
                    AadharNo = _vendorReg.AadharNo,
                    AadharDocURL = _vendorReg.AadharDocURL,
                    UDYAMRegNo = _vendorReg.UDYAMRegNo,
                    UDYAMRegCertURL = _vendorReg.UDYAMRegCertURL,
                    CIN = _vendorReg.CIN,
                    CINCertURL = _vendorReg.CINCertURL,
                    PFNo = _vendorReg.PFNo,
                    PFRegCertURL = _vendorReg.PFRegCertURL,
                    ESIRegNo = _vendorReg.ESIRegNo,
                    ESIRegCertURL = _vendorReg.ESIRegCertURL,
                    CancelledChequeURL = _vendorReg.CancelledChequeURL,
                    WebSite = _vendorReg.WebSite,
                    CompanyPhoneNo = _vendorReg.CompanyPhoneNo,
                    CompanyEmailID = _vendorReg.CompanyEmailID,
                    IsMSMERegistered = _vendorReg.IsMSMERegistered,

                    // Add or adjust any other fields the DTO requires
                };

                // serialize payload for logging
                var jsonOptions = new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
                var payload = System.Text.Json.JsonSerializer.Serialize(createDto, jsonOptions);
                Logger.LogInformation("Creating vendor registration — payload: {Payload}", payload);

                // call repository (accepts create DTO)
                var createdVendorReg = await VendorRegistrationFormRepository.CreateNewVendorRegistration(createDto);

                if (createdVendorReg != null)
                {
                    Logger.LogInformation("CreateNewVendorRegistration returned id={Id}", createdVendorReg.Id);
                    Snackbar.Add("Vendor registration saved.", Severity.Success);
                    NavigationManager.NavigateTo("/vendor-registrations");
                }
                else
                {
                    Logger.LogWarning("CreateNewVendorRegistration returned null.");
                    Snackbar.Add("Save failed: server returned no data.", Severity.Error);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error calling CreateNewVendorRegistration");
                var message = ex.InnerException != null ? $"{ex.Message} | Inner: {ex.InnerException.Message}" : ex.Message;
                Snackbar.Add($"Save failed: {message}", Severity.Error);
                Logger.LogDebug("Failed payload: {Payload}", System.Text.Json.JsonSerializer.Serialize(_vendorReg, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            finally
            {
                _isSaving = false;
            }
        }

        // --- Upload handlers (grouped, small, self-documenting) ---
        private Task OnPANUploaded(string? url) => SetUrl(_vendorReg, nameof(_vendorReg.PANCardURL), url);
        private Task OnTANUploaded(string? url) => SetUrl(_vendorReg, nameof(_vendorReg.TANCertURL), url);
        private Task OnGSTUploaded(string? url) => SetUrl(_vendorReg, nameof(_vendorReg.GSTRegistrationCertURL), url);
        private Task OnAADHARUploaded(string? url) => SetUrl(_vendorReg, nameof(_vendorReg.AadharDocURL), url);
        private Task onCINUploaded(string? url) => SetUrl(_vendorReg, nameof(_vendorReg.CINCertURL), url);
        private Task OnCancelCheqUploaded(string? url) => SetUrl(_vendorReg, nameof(_vendorReg.CancelledChequeURL), url);
        private Task OnUDYAMUploaded(string? url) => SetUrl(_vendorReg, nameof(_vendorReg.UDYAMRegCertURL), url);
        private Task OnESIUploaded(string? url) => SetUrl(_vendorReg, nameof(_vendorReg.ESIRegCertURL), url);
        private Task OnPFUploaded(string? url) => SetUrl(_vendorReg, nameof(_vendorReg.PFRegCertURL), url);

        private Task SetUrl(VendorRegistrationForm model, string propertyName, string? url)
        {
            if (string.IsNullOrEmpty(propertyName))
                return Task.CompletedTask;

            var value = url ?? string.Empty;
            switch (propertyName)
            {
                case nameof(VendorRegistrationForm.PANCardURL): model.PANCardURL = value; break;
                case nameof(VendorRegistrationForm.TANCertURL): model.TANCertURL = value; break;
                case nameof(VendorRegistrationForm.GSTRegistrationCertURL): model.GSTRegistrationCertURL = value; break;
                case nameof(VendorRegistrationForm.AadharDocURL): model.AadharDocURL = value; break;
                case nameof(VendorRegistrationForm.CINCertURL): model.CINCertURL = value; break;
                case nameof(VendorRegistrationForm.CancelledChequeURL): model.CancelledChequeURL = value; break;
                case nameof(VendorRegistrationForm.UDYAMRegCertURL): model.UDYAMRegCertURL = value; break;
                case nameof(VendorRegistrationForm.ESIRegCertURL): model.ESIRegCertURL = value; break;
                case nameof(VendorRegistrationForm.PFRegCertURL): model.PFRegCertURL = value; break;
                default:
                    Logger.LogWarning("Unknown upload property requested: {Property}", propertyName);
                    break;
            }
            return Task.CompletedTask;
        }

        // --- Stepper helpers ---
        private void PrevStep() { if (_activeStep > 0) _activeStep--; }
        private int ActiveStep { get => _activeStep; set { _activeStep = value; StateHasChanged(); } }

        // --- Navigation / misc ---
        private void OnCancel() => NavigationManager.NavigateTo("/vendor-registrations");
    }
}
