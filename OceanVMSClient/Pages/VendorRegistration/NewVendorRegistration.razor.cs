using Blazored.LocalStorage;
using Entities.Models.Setup;
using Entities.Models.VendorReg;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;
using MudBlazor;
using OceanVMSClient.HttpRepoInterface.VendorRegistration;
using OceanVMSClient.SharedComp;
using Shared.DTO.POModule;
using Shared.DTO.VendorReg;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Globalization; // added

namespace OceanVMSClient.Pages.VendorRegistration
{
    public partial class NewVendorRegistration
    {
        // --- Cascading parameters / user context ---
        [CascadingParameter] private Task<AuthenticationState>? AuthenticationStateTask { get; set; }
        [CascadingParameter] public Task<AuthenticationState> AuthState { get; set; } = default!;
        // --- Injected services ---
        [Inject] public ILogger<NewVendorRegistration> Logger { get; set; } = default!;
        [Inject] public required IVendorRegistrationRepository VendorRegistrationFormRepository { get; set; }
        [Inject] public ICompanyOwnershipRepository CompanyOwnershipRepository { get; set; } = default!;
        [Inject] public IVendorServiceRepository VendorServiceRepository { get; set; } = default!;
        [Inject] public ISnackbar Snackbar { get; set; } = default!;
        [Inject] public NavigationManager NavigationManager { get; set; } = default!;
        [Inject] public IDialogService DialogService { get; set; } = default!;
        [Inject] public ILocalStorageService LocalStorage { get; set; } = default!;
        // --- Component parameters ---
        [Parameter] public Guid VendorRegistrationFormId { get; set; }

        // --- Model / EditContext for validation ---
        private VendorRegistrationFormDto _vendorReg = new()
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
            CompanyOwnershipType = string.Empty
        };
        private VendorRegistrationFormReviewDto? _vendorReviewDto;
        private VendorRegistrationFormApprovalDto? _vendorApprovalDto;
        private EditContext _editContext;
        private ValidationMessageStore? _messageStore;

        // --- Lookups and selection state ---
        private List<CompanyOwnership> _companyOwnerships { get; set; } = new();
        private List<VendorService> _vendorServices { get; set; } = new();
        private IEnumerable<string> _selectedServices { get; set; } = new HashSet<string>();

        // user context
        private string? _userType;
        private Guid? _vendorId;
        private Guid? _vendorContactId;
        private Guid? _employeeId;
        private string? _userRole;

        // --- UI state ---
        private int _activeStep;
        private bool _completed;
        private bool _isSaving;
        private bool _isReadOnly = false;
        private bool _isReviewer = false;
        private bool _isApprover = false;
        private bool _isReviewLocked = true;
        private bool _isApproverLocked = false;

        private string _reviewStatus = "Pending";
        private string _approvalStatus = "Pending";
        private string _formEditMode = "Create";
        private bool _submitDisabled = false;

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
                await LoadUserContextAsync();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to load user context");
                Snackbar.Add("Failed to load user context.", Severity.Error);
            }

            try
            {
                await LoadLookupsAsync();

                if (VendorRegistrationFormId != Guid.Empty)
                {
                    // load existing registration and rebind edit context
                    _vendorReg = await VendorRegistrationFormRepository.GetVendorRegistrationFormByIdAsync(VendorRegistrationFormId);
                    _editContext = new EditContext(_vendorReg);
                    // default to readonly for non-edit scenarios
                    _isReadOnly = true;


                    // common defaults
                    _isReviewLocked = true;
                    _isApproverLocked = true;
                    _reviewStatus = _vendorReg.ReviewStatus?.ToString() ?? "Pending";

                    var reviewerStatus = (_vendorReg.ReviewerStatus ?? "Pending").Trim();
                    var approverStatus = (_vendorReg.ApproverStatus ?? "Pending").Trim();

                    // if current user is a vendor, allow updating the existing form:
                    // - enable editing (vendor updates their registration)
                    // - reset reviewer/approver statuses to Pending so the form can re-enter review flow
                    if (!string.IsNullOrWhiteSpace(_userType) && string.Equals(_userType, "VENDOR", StringComparison.OrdinalIgnoreCase))
                    {
                        _isReadOnly = false;           // vendor can edit
                        _formEditMode = "Edit";       // mark as edit mode (not a simple view)
                        _vendorReg.ReviewerStatus = "Pending";
                        _vendorReg.ApproverStatus = "Pending";

                        // Keep review/approve controls locked (vendor is not reviewer/approver)
                        _isReviewLocked = true;
                        _isApproverLocked = true;
                    }
                    else
                    {
                        // non-vendor flows (reviewer / approver logic)
                        if (string.Equals(reviewerStatus, "Pending", StringComparison.OrdinalIgnoreCase))
                        {
                            if (_isReviewer)
                                _isReviewLocked = false;
                        }

                        // Unlock approver only when reviewer is explicitly "Approved" AND approver is still "Pending"
                        if (_isApprover
                            && string.Equals(reviewerStatus, "Approved", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(approverStatus, "Pending", StringComparison.OrdinalIgnoreCase))
                        {
                            _isApproverLocked = false;
                        }
                        else if (_isReviewer)
                        {
                            // if reviewer is NOT Approved, approver must stay locked
                            _isApproverLocked = true;
                        }
                        else
                        {
                            // not an approver — treat as view-only
                            _formEditMode = "View";
                        }

                        if (_userType == "VENDOR")
                        {
                            if (string.Equals(_vendorReg.ApproverStatus, "Approved", StringComparison.OrdinalIgnoreCase))
                            {
                                _submitDisabled = true;
                            }
                            else
                            {
                                _submitDisabled = false;
                                _formEditMode = "Edit";

                            }
                        }

                        // ensure ownership mapping and selected services are in sync
                        MapSelectedServices();

                        // validation message store + cleanup on field change
                        _messageStore = new ValidationMessageStore(_editContext);
                        _editContext.OnFieldChanged += (sender, args) =>
                        {
                            _messageStore?.Clear(args.FieldIdentifier);
                            _editContext.NotifyValidationStateChanged();
                        };
                    }
                }
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

        // Add this handler to the partial class (near other helpers)
        private Task OnReviewStatusChanged(Entities.Models.VendorReg.ReviewStatus status)
        {
            // ensure model updated (bind already does this, but set explicitly for clarity)
            _vendorReg.ReviewStatus = status;

            // Update UI locks based on selection and current user roles
            // Example rules: reviewers can set Pending and unlock review inputs; once Approved/Rejected, lock again.
            switch (status)
            {
                case Entities.Models.VendorReg.ReviewStatus.Pending:
                    _isReviewLocked = !_isReviewer; // unlock only for reviewers
                    break;
                case Entities.Models.VendorReg.ReviewStatus.Approved:
                case Entities.Models.VendorReg.ReviewStatus.Rejected:
                    _isReviewLocked = true;
                    break;
                default:
                    // keep existing lock state for other statuses
                    break;
            }

            StateHasChanged();
            return Task.CompletedTask;
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
        private void OnMSMERegisteredChanged()
        {
            if (_vendorReg.IsMSMERegistered == true)
            {
                _isUDYAMRequired = true;
            }
            else
            {
                _vendorReg.IsMSMERegistered = false;
                _isUDYAMRequired = false;
            }
        }

        private void OnCompanyOwnershipChanged()
        {
            string companyOwnershipType = _vendorReg.CompanyOwnershipType?.Trim() ?? string.Empty;
            var match = _companyOwnerships.FirstOrDefault(c => string.Equals(c.CompanyOwnershipType, companyOwnershipType, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                _vendorReg.CompanyOwnershipId = match.Id;
                _vendorReg.CompanyOwnershipType = match.CompanyOwnershipType;
                ApplyCompanyOwnershipRequirements(match.Id);
            }
            else
            {
                _vendorReg.CompanyOwnershipId = Guid.Empty;
            }

            StateHasChanged();
            //return Task.CompletedTask;
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
            if (_formEditMode == "Create")
            {
                await CreateVendorRegistrationAsync();
                return;
            }
            else if (_formEditMode == "Edit")
            {
                await UpdateVendorRegistrationAsync();
            }
                // proceed to save when validation passes and user confirmed
                
        }

        // --- Save / repository interaction (map to CreateDto) ---

        private async Task LockReview()
        {
            if (_vendorReg == null)
            {
                Snackbar.Add("Vendor registration is not loaded.", Severity.Error);
                Logger.LogWarning("LockReview called but _vendorReg was null.");
                return;
            }

            // Ensure DTO exists to avoid NullReferenceException
            _vendorReviewDto ??= new VendorRegistrationFormReviewDto();

            try
            {
                _vendorReviewDto.ReviewerId = _employeeId ?? Guid.Empty;
                _vendorReviewDto.ReviewDate = DateTime.UtcNow;
                _vendorReviewDto.ReviewerStatus = _vendorReg.ReviewerStatus;
                _vendorReviewDto.ReviewComments = _vendorReg.ReviewComments;

                var jsonOptions = new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
                var payload = System.Text.Json.JsonSerializer.Serialize(_vendorReviewDto, jsonOptions);
                Logger.LogInformation("Creating vendor review — payload: {Payload}", payload);

                // call repository (accepts create DTO)
                var reviewResult = await VendorRegistrationFormRepository.ReviewVendorRegistrationFormAsync(_vendorReg.Id, _vendorReviewDto);
                if (reviewResult != null)
                {
                    Logger.LogInformation("ReviewVendorRegistrationFormAsync returned: {Result}", reviewResult);
                    Snackbar.Add("Vendor registration reviewed.", Severity.Success);
                    NavigationManager.NavigateTo($"/vendor-registrations/{_vendorReg.Id}");
                }
                else
                {
                    Logger.LogWarning("ReviewVendorRegistrationFormAsync returned null.");
                    Snackbar.Add("Review failed: server returned no data.", Severity.Error);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error calling ReviewVendorRegistrationFormAsync");
                Snackbar.Add($"Review failed: {ex.Message}", Severity.Error);
            }
        }
        private async Task LockApproval()
        {
            if (_vendorReg == null)
            {
                Snackbar.Add("Vendor registration is not loaded.", Severity.Error);
                Logger.LogWarning("LockApproval called but _vendorReg was null.");
                return;
            }

            // Ensure DTO exists to avoid NullReferenceException
            _vendorApprovalDto ??= new VendorRegistrationFormApprovalDto();

            try
            {
                _vendorApprovalDto.ApproverId = _employeeId ?? Guid.Empty;
                _vendorApprovalDto.ApprovalDate = DateTime.UtcNow;
                _vendorApprovalDto.ApproverStatus = _vendorReg.ApproverStatus;
                _vendorApprovalDto.ApprovalComments = _vendorReg.ApprovalComments;
                var jsonOptions = new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
                var payload = System.Text.Json.JsonSerializer.Serialize(_vendorApprovalDto, jsonOptions);
                Logger.LogInformation("Creating vendor approval — payload: {Payload}", payload);
                // call repository (accepts create DTO)
                var approvalResult = await VendorRegistrationFormRepository.ApproveVendorRegistrationFormAsync(_vendorReg.Id, _vendorApprovalDto);
                if (approvalResult != null)
                {
                    Logger.LogInformation("ApproveVendorRegistrationFormAsync returned: {Result}", approvalResult);
                    Snackbar.Add("Vendor registration approved.", Severity.Success);
                    NavigationManager.NavigateTo($"/vendor-registrations/{_vendorReg.Id}");
                }
                else
                {
                    Logger.LogWarning("ApproveVendorRegistrationFormAsync returned null.");
                    Snackbar.Add("Approval failed: server returned no data.", Severity.Error);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error calling ApproveVendorRegistrationFormAsync");
                Snackbar.Add($"Approval failed: {ex.Message}", Severity.Error);
            }
        }



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
                    NavigationManager.NavigateTo($"/vendor-registrations/{createdVendorReg.Id}");
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

        private async Task UpdateVendorRegistrationAsync()
        {
            if (_isSaving) return;
            _isSaving = true;

            try
            {
                var UpdateDto = new VendorRegistrationFormUpdateDto
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
                var payload = System.Text.Json.JsonSerializer.Serialize(UpdateDto, jsonOptions);
                Logger.LogInformation("Creating vendor registration — payload: {Payload}", payload);

                // call repository (accepts create DTO)
                var updatedVendorReg = await VendorRegistrationFormRepository.UpdateVendorRegistration(_vendorReg.Id, UpdateDto);

                if (updatedVendorReg != null)
                {
                    Logger.LogInformation("UpdateVendorRegistration returned id={Id}", updatedVendorReg.Id);
                    Snackbar.Add("Vendor registration updated.", Severity.Success);
                    NavigationManager.NavigateTo($"/vendor-registrations/{updatedVendorReg.Id}");
                }
                else
                {
                    Logger.LogWarning("UpdateVendorRegistration returned null.");
                    Snackbar.Add("Save failed: server returned no data.", Severity.Error);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error calling UpdateVendorRegistration");
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

        private Task SetUrl(VendorRegistrationFormDto model, string propertyName, string? url)
        {
            if (string.IsNullOrEmpty(propertyName))
                return Task.CompletedTask;

            var value = url ?? string.Empty;
            switch (propertyName)
            {
                case nameof(VendorRegistrationFormDto.PANCardURL): model.PANCardURL = value; break;
                case nameof(VendorRegistrationFormDto.TANCertURL): model.TANCertURL = value; break;
                case nameof(VendorRegistrationFormDto.GSTRegistrationCertURL): model.GSTRegistrationCertURL = value; break;
                case nameof(VendorRegistrationFormDto.AadharDocURL): model.AadharDocURL = value; break;
                case nameof(VendorRegistrationFormDto.CINCertURL): model.CINCertURL = value; break;
                case nameof(VendorRegistrationFormDto.CancelledChequeURL): model.CancelledChequeURL = value; break;
                case nameof(VendorRegistrationFormDto.UDYAMRegCertURL): model.UDYAMRegCertURL = value; break;
                case nameof(VendorRegistrationFormDto.ESIRegCertURL): model.ESIRegCertURL = value; break;
                case nameof(VendorRegistrationFormDto.PFRegCertURL): model.PFRegCertURL = value; break;
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
        private void OnCancel() => NavigationManager.NavigateTo($"/vendor-registrations/{_vendorReg.Id}");

        #region User context helpers



        // Pseudocode / Plan:
        // 1. Await the authentication state and obtain the ClaimsPrincipal (user).
        // 2. If not authenticated, navigate to the root and exit.
        // 3. Read claim values for userType, vendorPK/vendorId, vendorContactId/vendorContact, empPK/EmployeeId, and role.
        // 4. If userType is missing, try to read it from local storage.
        // 5. If userType indicates a vendor, ensure vendor-related IDs are retrieved from local storage if missing.
        // 6. If the retrieved role string contains "Vendor Approver" (case-insensitive), set _isApprover = true.
        // 7. Otherwise, leave _isApprover false (default).
        // 8. Keep method async and preserve existing behaviors and fallbacks.
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
                _userRole = GetClaimValue(user, "role");
            }
            else
            {
                // Allow anonymous users to open the page for creating a new registration.
                // If VendorRegistrationFormId is not empty (view/edit existing), require auth and redirect.
                if (VendorRegistrationFormId == Guid.Empty)
                {
                    // try to populate userType from local storage if present, but do not redirect
                    _userType = await LocalStorage.GetItemAsync<string>("userType");
                    return;
                }

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

            // Set approver/reviewer flags
            if (!string.IsNullOrWhiteSpace(_userRole) &&
                _userRole.IndexOf("Vendor Approver", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _isApprover = true;
            }
            else
            {
                _isApprover = false;
            }

            if (!string.IsNullOrWhiteSpace(_userRole) &&
                _userRole.IndexOf("Vendor Validator", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _isReviewer = true;
            }
            else
            {
                _isReviewer = false;
            }
        }

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

        // add these handlers (near other helpers)
        private Task OnReviewerStatusChanged(string? value)
        {
            // update model
            _vendorReg.ReviewerStatus = value;

            // normalize statuses (null => "Pending")
            var reviewerStatus = (_vendorReg.ReviewerStatus ?? "Pending").Trim();
            var approverStatus = (_vendorReg.ApproverStatus ?? "Pending").Trim();

            // review control lock: reviewers can edit when they are reviewers and status is Pending
            _isReviewLocked = !(_isReviewer && string.Equals(reviewerStatus, "Pending", StringComparison.OrdinalIgnoreCase));

            // approver unlock rule: only unlock when current user is approver, reviewer is Approved and approver is still Pending
            _isApproverLocked = !(
                _isApprover
                && string.Equals(reviewerStatus, "Approved", StringComparison.OrdinalIgnoreCase)
                && string.Equals(approverStatus, "Pending", StringComparison.OrdinalIgnoreCase)
            );

            StateHasChanged();
            return Task.CompletedTask;
        }

        private Task OnApproverStatusChanged(string? value)
        {
            _vendorReg.ApproverStatus = value;

            // keep approver lock consistent: if approver changed away from Pending, lock further edits
            var approverStatus = (_vendorReg.ApproverStatus ?? "Pending").Trim();
            if (!string.Equals(approverStatus, "Pending", StringComparison.OrdinalIgnoreCase))
                _isApproverLocked = true;

            StateHasChanged();
            return Task.CompletedTask;
        }
    }
}
