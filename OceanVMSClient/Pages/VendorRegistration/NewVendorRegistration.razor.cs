using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
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

namespace OceanVMSClient.Pages.VendorRegistration
{
    public partial class NewVendorRegistration : IDisposable
    {
        // Cascading / injected / parameters
        [CascadingParameter] private Task<AuthenticationState>? AuthenticationStateTask { get; set; }
        [CascadingParameter] public Task<AuthenticationState> AuthState { get; set; } = default!;
        [Inject] public ILogger<NewVendorRegistration> Logger { get; set; } = default!;
        [Inject] public required IVendorRegistrationRepository VendorRegistrationFormRepository { get; set; }
        [Inject] public ICompanyOwnershipRepository CompanyOwnershipRepository { get; set; } = default!;
        [Inject] public IVendorServiceRepository VendorServiceRepository { get; set; } = default!;
        [Inject] public ISnackbar Snackbar { get; set; } = default!;
        [Inject] public NavigationManager NavigationManager { get; set; } = default!;
        [Inject] public IDialogService DialogService { get; set; } = default!;
        [Inject] public ILocalStorageService LocalStorage { get; set; } = default!;
        [Parameter] public Guid VendorRegistrationFormId { get; set; }

        // State / models
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
        private EventHandler<FieldChangedEventArgs>? _clearHandler;

        // lookups / selections
        private List<CompanyOwnership> _companyOwnerships { get; set; } = new();
        private List<VendorService> _vendorServices { get; set; } = new();
        private IEnumerable<string> _selectedServices { get; set; } = new HashSet<string>();
        private string _selectedCompanyOwnershipType = string.Empty;

        // user context
        private string? _userType;
        private Guid? _vendorId;
        private Guid? _vendorContactId;
        private Guid? _employeeId;
        private string? _userRole;

        // UI state
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

        // dynamic required flags
        private bool _isAadharRequired;
        private bool _isPANRequired;
        private bool _isTANRequired;
        private bool _isGSTRequired;
        private bool _isUDYAMRequired;
        private bool _isCINRequired;
        private bool _isPFRequired;
        private bool _isESIRequired;
        private bool _IsMSMERegistered;

        // Back-compat property names used by Razor
        private bool _isPANrequired { get => _isPANRequired; set => _isPANRequired = value; }
        private bool _isTANRrequired { get => _isTANRequired; set => _isTANRequired = value; }
        private bool _IsAadharRequired { get => _isAadharRequired; set => _isAadharRequired = value; }

        // json options reused for logging
        private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

        // ctor: initialize edit context with initial model
        public NewVendorRegistration()
        {
            InitializeEditContext(_vendorReg);
        }

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
                    _vendorReg = await VendorRegistrationFormRepository.GetVendorRegistrationFormByIdAsync(VendorRegistrationFormId);
                    InitializeEditContext(_vendorReg);

                    // default to readonly for view scenarios; may be changed by role logic below
                    _isReadOnly = true;
                    _isReviewLocked = true;
                    _isApproverLocked = true;

                    _reviewStatus = _vendorReg.ReviewStatus?.ToString() ?? "Pending";
                    var reviewerStatus = (_vendorReg.ReviewerStatus ?? "Pending").Trim();
                    var approverStatus = (_vendorReg.ApproverStatus ?? "Pending").Trim();

                    if (!string.IsNullOrWhiteSpace(_userType) && string.Equals(_userType, "VENDOR", StringComparison.OrdinalIgnoreCase))
                    {
                        // vendor may edit their own registration
                        _isReadOnly = false;
                        _formEditMode = "Edit";
                        _vendorReg.ReviewerStatus = "Pending";
                        _vendorReg.ApproverStatus = "Pending";
                        _isReviewLocked = true;
                        _isApproverLocked = true;
                    }
                    else
                    {
                        // reviewer / approver rules
                        if (string.Equals(reviewerStatus, "Pending", StringComparison.OrdinalIgnoreCase) && _isReviewer)
                            _isReviewLocked = false;

                        if (_isApprover
                            && string.Equals(reviewerStatus, "Approved", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(approverStatus, "Pending", StringComparison.OrdinalIgnoreCase))
                        {
                            _isApproverLocked = false;
                        }
                        else if (_isReviewer)
                        {
                            _isApproverLocked = true;
                        }
                        else
                        {
                            _formEditMode = "View";
                        }

                        if (_userType == "VENDOR")
                        {
                            _submitDisabled = string.Equals(_vendorReg.ApproverStatus, "Approved", StringComparison.OrdinalIgnoreCase);
                            if (!_submitDisabled) _formEditMode = "Edit";
                        }

                        //MapSelectedServices();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Initialization failed for NewVendorRegistration");
                Snackbar.Add("Failed to initialize form lookups.", Severity.Error);
            }
        }

        // Initialize / rebind EditContext and validation message clearing handler
        private void InitializeEditContext(VendorRegistrationFormDto model)
        {
            // unsubscribe previous handlers (if any)
            if (_editContext != null)
            {
                _editContext.OnFieldChanged -= EditContext_OnFieldChanged;
                if (_clearHandler != null) _editContext.OnFieldChanged -= _clearHandler;
            }
            
            _editContext = new EditContext(model);
            _editContext.OnFieldChanged += EditContext_OnFieldChanged;

            _messageStore = new ValidationMessageStore(_editContext);
            _clearHandler = (sender, args) =>
            {
                _messageStore?.Clear(args.FieldIdentifier);
                _editContext.NotifyValidationStateChanged();
            };
            _editContext.OnFieldChanged += _clearHandler;
        }

        private async Task LoadLookupsAsync()
        {
            var ownershipsTask = CompanyOwnershipRepository.GetAllCompanyOwnershipsAsync();
            var servicesTask = VendorServiceRepository.GetAllVendorServices();

            await Task.WhenAll(ownershipsTask, servicesTask);

            _companyOwnerships = ownershipsTask.Result?.ToList() ?? new List<CompanyOwnership>();
            _vendorServices = servicesTask.Result?.ToList() ?? new List<VendorService>();
        }

        private Task OnReviewStatusChanged(Entities.Models.VendorReg.ReviewStatus status)
        {
            _vendorReg.ReviewStatus = status;

            switch (status)
            {
                case Entities.Models.VendorReg.ReviewStatus.Pending:
                    _isReviewLocked = !_isReviewer;
                    break;
                case Entities.Models.VendorReg.ReviewStatus.Approved:
                case Entities.Models.VendorReg.ReviewStatus.Rejected:
                    _isReviewLocked = true;
                    break;
            }

            StateHasChanged();
            return Task.CompletedTask;
        }

        //private void MapSelectedServices()
        //{
        //    if (string.IsNullOrWhiteSpace(_vendorReg.VendorType))
        //    {
        //        _selectedServices = Array.Empty<string>();
        //        return;
        //    }

        //    _selectedServices = _vendorReg.VendorType
        //        .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
        //        .Select(s => s.Trim())
        //        .Where(s => !string.IsNullOrEmpty(s))
        //        .Distinct()
        //        .ToList();
        //}

        private async Task HandleMSMSSelectdChange(bool newValue)
        {
            //_IsMSMERegistered = _vendorReg.IsMSMERegistered == true;
            
            _vendorReg.IsMSMERegistered = newValue;
            _IsMSMERegistered = newValue;
            _isUDYAMRequired = _vendorReg.IsMSMERegistered == true;

            await Task.CompletedTask;
        }

        private void OnCompanyOwnershipChanged()
        {
            var companyOwnershipType = _vendorReg.CompanyOwnershipType?.Trim() ?? string.Empty;
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
        }

        private void ApplyCompanyOwnershipRequirements(Guid id)
        {
            var sel = _companyOwnerships.FirstOrDefault(c => c.Id == id);
            _isPANRequired = sel?.PAN_Required ?? false;
            _isTANRequired = sel?.TAN_Required ?? false;
            _isGSTRequired = sel?.GST_Required ?? false;
            _isCINRequired = sel?.CIN_Required ?? false;
            //_isUDYAMRequired = sel?.UDYAM_Required ?? false;
            _isPFRequired = sel?.PF_Required ?? false;
            _isESIRequired = sel?.ESI_Required ?? false;
            _isAadharRequired = sel?.AADHAR_Required ?? false;
        }

        private async Task HandleSubmit(EditContext editContext)
        {
            var validationResults = new List<ValidationResult>();
            var validationContext = new ValidationContext(_vendorReg, serviceProvider: null, items: null);
            Validator.TryValidateObject(_vendorReg, validationContext, validationResults, validateAllProperties: true);

            if (_isPANRequired && string.IsNullOrWhiteSpace(_vendorReg.PANNo))
                validationResults.Add(new ValidationResult("PAN is required for the selected ownership.", new[] { nameof(_vendorReg.PANNo) }));

            if (_isAadharRequired && string.IsNullOrWhiteSpace(_vendorReg.AadharNo))
                validationResults.Add(new ValidationResult("Aadhar is required for the selected ownership.", new[] { nameof(_vendorReg.AadharNo) }));

            if (_isGSTRequired && string.IsNullOrWhiteSpace(_vendorReg.GSTNO))
                validationResults.Add(new ValidationResult("GST No is required for the selected ownership.", new[] { nameof(_vendorReg.GSTNO) }));

            // Clear previous messages and populate the ValidationMessageStore so field-level validation appears immediately
            _messageStore?.Clear();
            if (validationResults.Any())
            {
                foreach (var result in validationResults)
                {
                    var message = result.ErrorMessage ?? string.Empty;
                    var members = (result.MemberNames ?? Enumerable.Empty<string>()).ToList();

                    if (members.Count == 0)
                    {
                        // model-level error: attach to top-level field (use OrganizationName as generic placeholder)
                        _messageStore?.Add(new FieldIdentifier(_vendorReg, nameof(_vendorReg.OrganizationName)), message);
                    }
                    else
                    {
                        foreach (var member in members)
                        {
                            // Ensure property name matches the DTO property (case-sensitive)
                            _messageStore?.Add(new FieldIdentifier(_vendorReg, member), message);
                        }
                    }
                }

                // Notify the EditContext so UI updates immediately
                _editContext?.NotifyValidationStateChanged();

                // Also show the dialog summary (existing behavior)
                var messages = validationResults
                    .Where(r => !string.IsNullOrWhiteSpace(r.ErrorMessage))
                    .Select(r => r.ErrorMessage!)
                    .Distinct()
                    .ToList();

                Snackbar.Add("Please fix validation errors.", Severity.Warning);
                var parameters = new DialogParameters { ["ValidationErrors"] = messages };
                var options = new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Small, FullWidth = true };
                DialogService.Show<ValidationErrorsDialog>("Validation errors", parameters, options);
                return;
            }

            var confirmed = await DialogService.ShowMessageBox(
                "Confirm save",
                "Are you sure you want to save this vendor registration?",
                yesText: "Yes",
                noText: "No");

            if (confirmed != true) return;

            if (_formEditMode == "Create")
            {
                await CreateVendorRegistrationAsync();
            }
            else if (_formEditMode == "Edit")
            {
                await UpdateVendorRegistrationAsync();
            }
        }

        private async Task LockReview()
        {
            if (_vendorReg == null)
            {
                Snackbar.Add("Vendor registration is not loaded.", Severity.Error);
                Logger.LogWarning("LockReview called but _vendorReg was null.");
                return;
            }

            _vendorReviewDto ??= new VendorRegistrationFormReviewDto();
            _vendorReviewDto.ReviewerId = _employeeId ?? Guid.Empty;
            _vendorReviewDto.ReviewDate = DateTime.UtcNow;
            _vendorReviewDto.ReviewerStatus = _vendorReg.ReviewerStatus;
            _vendorReviewDto.ReviewComments = _vendorReg.ReviewComments;

            LogPayload("Creating vendor review", _vendorReviewDto);

            try
            {
                var reviewResult = await VendorRegistrationFormRepository.ReviewVendorRegistrationFormAsync(_vendorReg.Id, _vendorReviewDto);
                if (reviewResult != null)
                {
                    Snackbar.Add("Vendor registration reviewed.", Severity.Success);
                    NavigationManager.NavigateTo($"/vendor-registrations/{_vendorReg.Id}");
                }
                else
                {
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

            _vendorApprovalDto ??= new VendorRegistrationFormApprovalDto();
            _vendorApprovalDto.ApproverId = _employeeId ?? Guid.Empty;
            _vendorApprovalDto.ApprovalDate = DateTime.UtcNow;
            _vendorApprovalDto.ApproverStatus = _vendorReg.ApproverStatus;
            _vendorApprovalDto.ApprovalComments = _vendorReg.ApprovalComments;

            LogPayload("Creating vendor approval", _vendorApprovalDto);

            try
            {
                var approvalResult = await VendorRegistrationFormRepository.ApproveVendorRegistrationFormAsync(_vendorReg.Id, _vendorApprovalDto);
                if (approvalResult != null)
                {
                    Snackbar.Add("Vendor registration approved.", Severity.Success);
                    NavigationManager.NavigateTo($"/vendor-registrations/{_vendorReg.Id}");
                }
                else
                {
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
                var createDto = BuildCreateDto();
                LogPayload("Creating vendor registration", createDto);

                var createdVendorReg = await VendorRegistrationFormRepository.CreateNewVendorRegistration(createDto);

                if (createdVendorReg != null)
                {
                    Snackbar.Add("Vendor registration saved.", Severity.Success);

                    // Show a pleasant dialog informing the user their registration was submitted
                    var parameters = new DialogParameters
                    {
                        ["VendorName"] = _vendorReg.OrganizationName,
                        ["RegistrationId"] = createdVendorReg.Id,
                        ["ResponderEmail"] = _vendorReg.ResponderEmailId
                    };
                    var options = new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.ExtraSmall, FullWidth = true };
                    var dialogRef = DialogService.Show<RegistrationSubmittedDialog>("Registration submitted", parameters, options);

                    // Wait for the dialog to close; caller may return the registration id (if the user clicked "View Details")
                    var result = await dialogRef.Result;
                    // Navigate to the registration details (if user clicked View Details the id is returned; otherwise use created id)
                    var idToNavigate = result.Data is Guid gid ? gid : createdVendorReg.Id;
                    NavigationManager.NavigateTo($"/vendor-registrations/{idToNavigate}");
                }
                else
                {
                    Snackbar.Add("Save failed: server returned no data.", Severity.Error);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error calling CreateNewVendorRegistration");
                var message = ex.InnerException != null ? $"{ex.Message} | Inner: {ex.InnerException.Message}" : ex.Message;
                Snackbar.Add($"Save failed: {message}", Severity.Error);
                Logger.LogDebug("Failed payload: {Payload}", JsonSerializer.Serialize(_vendorReg, _jsonOptions));
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
                var updateDto = BuildUpdateDto();
                LogPayload("Updating vendor registration", updateDto);

                var updatedVendorReg = await VendorRegistrationFormRepository.UpdateVendorRegistration(_vendorReg.Id, updateDto);

                if (updatedVendorReg != null)
                {
                    Snackbar.Add("Vendor registration updated.", Severity.Success);

                    // Show a pleasant dialog informing the user their registration was submitted/updated
                    var parameters = new DialogParameters
                    {
                        ["VendorName"] = _vendorReg.OrganizationName,
                        ["RegistrationId"] = updatedVendorReg.Id,
                        ["ResponderEmail"] = _vendorReg.ResponderEmailId
                    };
                    var options = new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.ExtraSmall, FullWidth = true };
                    var dialogRef = DialogService.Show<RegistrationSubmittedDialog>("Registration updated", parameters, options);

                    var result = await dialogRef.Result;
                    var idToNavigate = result.Data is Guid gid ? gid : updatedVendorReg.Id;
                    NavigationManager.NavigateTo($"/vendor-registrations/{idToNavigate}");
                }
                else
                {
                    Snackbar.Add("Save failed: server returned no data.", Severity.Error);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error calling UpdateVendorRegistration");
                var message = ex.InnerException != null ? $"{ex.Message} | Inner: {ex.InnerException.Message}" : ex.Message;
                Snackbar.Add($"Save failed: {message}", Severity.Error);
                Logger.LogDebug("Failed payload: {Payload}", JsonSerializer.Serialize(_vendorReg, _jsonOptions));
            }
            finally
            {
                _isSaving = false;
            }
        }

        // Upload handlers
        private Task OnPANUploaded(string? url) => SetUrl(_vendorReg, nameof(_vendorReg.PANCardURL), url);
        private Task OnTANUploaded(string? url) => SetUrl(_vendorReg, nameof(_vendorReg.TANCertURL), url);
        private Task OnGSTUploaded(string? url) => SetUrl(_vendorReg, nameof(_vendorReg.GSTRegistrationCertURL), url);
        private Task OnAADHARUploaded(string? url) => SetUrl(_vendorReg, nameof(_vendorReg.AadharDocURL), url);
        private Task OnCINUploaded(string? url) => SetUrl(_vendorReg, nameof(_vendorReg.CINCertURL), url);
        private Task OnCancelCheqUploaded(string? url) => SetUrl(_vendorReg, nameof(_vendorReg.CancelledChequeURL), url);
        private Task OnUDYAMUploaded(string? url) => SetUrl(_vendorReg, nameof(_vendorReg.UDYAMRegCertURL), url);
        private Task OnESIUploaded(string? url) => SetUrl(_vendorReg, nameof(_vendorReg.ESIRegCertURL), url);
        private Task OnPFUploaded(string? url) => SetUrl(_vendorReg, nameof(_vendorReg.PFRegCertURL), url);

        private Task SetUrl(VendorRegistrationFormDto model, string propertyName, string? url)
        {
            if (string.IsNullOrEmpty(propertyName)) return Task.CompletedTask;

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

        // Stepper
        private void PrevStep() { if (_activeStep > 0) _activeStep--; }
        private int ActiveStep { get => _activeStep; set { _activeStep = value; StateHasChanged(); } }

        private void OnCancel() => NavigationManager.NavigateTo($"/vendor-registrations/{_vendorReg.Id}");

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
                _userRole = GetClaimValue(user, "role");
            }
            else
            {
                if (VendorRegistrationFormId == Guid.Empty)
                {
                    _userType = await LocalStorage.GetItemAsync<string>("userType");
                    return;
                }

                NavigationManager.NavigateTo("/");
                return;
            }

            if (string.IsNullOrWhiteSpace(_userType))
                _userType = await LocalStorage.GetItemAsync<string>("userType");

            if (string.Equals(_userType, "VENDOR", StringComparison.OrdinalIgnoreCase))
            {
                _vendorId ??= ParseGuid(await LocalStorage.GetItemAsync<string>("vendorPK"));
                _vendorContactId ??= ParseGuid(await LocalStorage.GetItemAsync<string>("vendorContactId"));
            }
            else
            {
                _employeeId ??= ParseGuid(await LocalStorage.GetItemAsync<string>("empPK"));
            }

            _isApprover = !string.IsNullOrWhiteSpace(_userRole) && _userRole.IndexOf("Vendor Approver", StringComparison.OrdinalIgnoreCase) >= 0;
            _isReviewer = !string.IsNullOrWhiteSpace(_userRole) && _userRole.IndexOf("Vendor Validator", StringComparison.OrdinalIgnoreCase) >= 0;
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

        private Task OnReviewerStatusChanged(string? value)
        {
            _vendorReg.ReviewerStatus = value;

            var reviewerStatus = (_vendorReg.ReviewerStatus ?? "Pending").Trim();
            var approverStatus = (_vendorReg.ApproverStatus ?? "Pending").Trim();

            _isReviewLocked = !(_isReviewer && string.Equals(reviewerStatus, "Pending", StringComparison.OrdinalIgnoreCase));
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

            var approverStatus = (_vendorReg.ApproverStatus ?? "Pending").Trim();
            if (!string.Equals(approverStatus, "Pending", StringComparison.OrdinalIgnoreCase))
                _isApproverLocked = true;

            StateHasChanged();
            return Task.CompletedTask;
        }

        private void EditContext_OnFieldChanged(object? sender, FieldChangedEventArgs e)
        {
            if (e.FieldIdentifier.FieldName == nameof(VendorRegistrationFormDto.IsMSMERegistered))
            {
                //HandleMSMSSelectdChange();
            }
        }

        // Helper: log payload once
        private void LogPayload(string action, object payload)
        {
            try
            {
                var serialized = JsonSerializer.Serialize(payload, _jsonOptions);
                Logger.LogInformation("{Action} — payload: {Payload}", action, serialized);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to serialize payload for logging");
            }
        }

        // Build DTOs with a shared helper to avoid repetitive assignments
        private VendorRegistrationFormCreateDto BuildCreateDto()
        {
            var dto = new VendorRegistrationFormCreateDto();
            ApplyCommonProperties(dto);
            return dto;
        }

        private VendorRegistrationFormUpdateDto BuildUpdateDto()
        {
            var dto = new VendorRegistrationFormUpdateDto();
            ApplyCommonProperties(dto);
            return dto;
        }

        private void ApplyCommonProperties(dynamic dto)
        {
            // copy common properties from _vendorReg into dto using dynamic to reduce repetition
            dto.OrganizationName = _vendorReg.OrganizationName;
            dto.ResponderFName = _vendorReg.ResponderFName;
            dto.ResponderLName = _vendorReg.ResponderLName;
            dto.ResponderDesignation = _vendorReg.ResponderDesignation;
            dto.ResponderMobileNo = _vendorReg.ResponderMobileNo;
            dto.ResponderEmailId = _vendorReg.ResponderEmailId;
            //dto.VendorType = _vendorReg.VendorType;
            dto.CompanyOwnershipId = _vendorReg.CompanyOwnershipId;
            dto.RegAddress1 = _vendorReg.RegAddress1;
            dto.RegAddress2 = _vendorReg.RegAddress2;
            dto.RegAddress3 = _vendorReg.RegAddress3;
            dto.RegCity = _vendorReg.RegCity;
            dto.RegState = _vendorReg.RegState;
            dto.RegCountry = _vendorReg.RegCountry;
            dto.RegPIN = _vendorReg.RegPIN;
            //dto.OperAddSameAsReg = _vendorReg.OperAddSameAsReg;
            dto.BankName = _vendorReg.BankName;
            dto.BankBranch = _vendorReg.BankBranch;
            dto.IFSCCode = _vendorReg.IFSCCode;
            dto.AccountNumber = _vendorReg.AccountNumber;
            dto.AccountName = _vendorReg.AccountName;
            dto.PANNo = _vendorReg.PANNo;
            dto.PANCardURL = _vendorReg.PANCardURL;
            dto.TANNo = _vendorReg.TANNo;
            dto.TANCertURL = _vendorReg.TANCertURL;
            dto.GSTNO = _vendorReg.GSTNO;
            dto.GSTRegistrationCertURL = _vendorReg.GSTRegistrationCertURL;
            dto.AadharNo = _vendorReg.AadharNo;
            dto.AadharDocURL = _vendorReg.AadharDocURL;
            dto.UDYAMRegNo = _vendorReg.UDYAMRegNo;
            dto.UDYAMRegCertURL = _vendorReg.UDYAMRegCertURL;
            dto.CIN = _vendorReg.CIN;
            dto.CINCertURL = _vendorReg.CINCertURL;
            dto.PFNo = _vendorReg.PFNo;
            dto.PFRegCertURL = _vendorReg.PFRegCertURL;
            dto.ESIRegNo = _vendorReg.ESIRegNo;
            dto.ESIRegCertURL = _vendorReg.ESIRegCertURL;
            dto.CancelledChequeURL = _vendorReg.CancelledChequeURL;
            dto.WebSite = _vendorReg.WebSite;
            dto.CompanyPhoneNo = _vendorReg.CompanyPhoneNo;
            dto.CompanyEmailID = _vendorReg.CompanyEmailID;
            dto.IsMSMERegistered = _vendorReg.IsMSMERegistered;
        }

        public void Dispose()
        {
            try
            {
                if (_editContext != null)
                {
                    _editContext.OnFieldChanged -= EditContext_OnFieldChanged;
                    if (_clearHandler != null) _editContext.OnFieldChanged -= _clearHandler;
                }
            }
            catch { /* ignore */ }
        }

        // Add this method inside the NewVendorRegistration partial class (e.g. near other helpers)
        private Task ValidateField(string propertyName)
        {
            if (_messageStore == null || _editContext == null || string.IsNullOrEmpty(propertyName))
                return Task.CompletedTask;

            var field = new FieldIdentifier(_vendorReg, propertyName);
            // clear previous messages for this field
            _messageStore.Clear(field);

            // validate property using DataAnnotations
            var results = new List<ValidationResult>();
            var prop = typeof(VendorRegistrationFormDto).GetProperty(propertyName);
            var value = prop?.GetValue(_vendorReg);

            var context = new ValidationContext(_vendorReg) { MemberName = propertyName };
            try
            {
                Validator.TryValidateProperty(value, context, results);
            }
            catch
            {
                // ignore validator exceptions here - we'll still apply manual checks below
            }

            // Apply dynamic business rules that are not covered by attributes
            if (string.Equals(propertyName, nameof(VendorRegistrationFormDto.PANNo), StringComparison.Ordinal))
            {
                if (_isPANRequired && string.IsNullOrWhiteSpace(_vendorReg.PANNo))
                    results.Add(new ValidationResult("PAN is required for the selected ownership."));
            }
            else if (string.Equals(propertyName, nameof(VendorRegistrationFormDto.AadharNo), StringComparison.Ordinal))
            {
                if (_isAadharRequired && string.IsNullOrWhiteSpace(_vendorReg.AadharNo))
                    results.Add(new ValidationResult("Aadhar is required for the selected ownership."));
            }
            else if (string.Equals(propertyName, nameof(VendorRegistrationFormDto.GSTNO), StringComparison.Ordinal))
            {
                if (_isGSTRequired && string.IsNullOrWhiteSpace(_vendorReg.GSTNO))
                    results.Add(new ValidationResult("GST No is required for the selected ownership."));
            }

            // push any messages to the message store for this field
            foreach (var r in results)
            {
                var message = r.ErrorMessage ?? string.Empty;
                _messageStore.Add(field, message);
            }

            // notify EditContext so field-level UI updates immediately
            _editContext.NotifyValidationStateChanged();
            return Task.CompletedTask;
        }
    }
}
