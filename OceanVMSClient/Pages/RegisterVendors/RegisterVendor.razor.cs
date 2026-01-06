using Blazored.LocalStorage;
using Entities.Models;
using Entities.Models.Setup;
using Entities.Models.VendorReg;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;
using MudBlazor;
using OceanVMSClient.Helpers;
using OceanVMSClient.HttpRepoInterface.VendorRegistration;
using OceanVMSClient.Pages.VendorRegistration;
using OceanVMSClient.SharedComp;
using Shared.DTO.VendorReg;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions; // add near top with other usings
using System.Threading.Tasks;

namespace OceanVMSClient.Pages.RegisterVendors
{
    public partial class RegisterVendor
    {
        // Parameters
        [Parameter] public Guid VendorRegistrationFormId { get; set; }

        // Injected services
        [Inject] public ISnackbar Snackbar { get; set; } = default!;
        [Inject] public IDialogService DialogService { get; set; } = default!;
        [Inject] public NavigationManager NavigationManager { get; set; } = default!;
        [Inject] public ILogger<RegisterVendor> Logger { get; set; } = default!;

        // Repositories
        [Inject] public required IVendorRegistrationRepository VendorRegistrationFormRepository { get; set; }
        [Inject] public ICompanyOwnershipRepository CompanyOwnershipRepository { get; set; } = default!;
        [Inject] public IVendorServiceRepository VendorServiceRepository { get; set; } = default!;

        // Local storage (fallback for claims)
        [Inject] public ILocalStorageService LocalStorage { get; set; } = default!;

        // Cascading parameters
        [CascadingParameter] public Task<AuthenticationState> AuthState { get; set; } = default!;

        // EditContext and validation store
        private EditContext _editContext = null!;
        private ValidationMessageStore? _messageStore;
        private EventHandler<FieldChangedEventArgs>? _clearHandler;

        // JSON logging options
        private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

        // UI helpers
        private List<BreadcrumbItem> _items = new()
        {
            new BreadcrumbItem("Home", href: "/", disabled: false),
            new BreadcrumbItem("Registration List", href: "/vendor-registrations", disabled: false),
        };
        private Typo _inputTypo = Typo.caption;
        private Margin _inputMargin = Margin.Dense;
        private Variant _inputVariant = Variant.Text;

        // User context
        private string? _userType;
        private Guid? _vendorId;
        private Guid? _vendorContactId;
        private Guid? _employeeId;
        private string? _userRole;

        // UI state
        private bool _isLoading;
        private int _activeStep;
        private bool _completed;
        private bool _isSaving;
        private bool _isReadOnly = false;
        private bool _isReviewer = false;
        private bool _isApprover = false;
        private bool _isReviewLocked = true;
        private bool _isApproverLocked = true;
        private string _reviewStatus = "Pending";
        private string _approvalStatus = "Pending";
        private string _formEditMode = "Create";
        private bool _submitDisabled = false;

        // Dynamic requirement flags
        private bool _isAadharRequired;
        private bool _isPANRequired;
        private bool _isTANRequired;
        private bool _isGSTRequired;
        private bool _isUDYAMRequired;
        private bool _isCINRequired;
        private bool _isPFRequired;
        private bool _isESIRequired;
        private bool _IsMSMERegistered;

        // Model & lookups
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
        private List<CompanyOwnership> _companyOwnerships { get; set; } = new();
        private List<VendorService> _vendorServices { get; set; } = new();
        private IEnumerable<string> _selectedServices { get; set; } = new HashSet<string>();
        private string _selectedCompanyOwnershipType = string.Empty;

        // Section field lists
        private readonly List<string> _registrationSection1Fields = new()
        {
            nameof(VendorRegistrationFormDto.PANNo),
            nameof(VendorRegistrationFormDto.PANCardURL),
            nameof(VendorRegistrationFormDto.TANNo),
            nameof(VendorRegistrationFormDto.TANCertURL),
            nameof(VendorRegistrationFormDto.GSTNO),
            nameof(VendorRegistrationFormDto.GSTRegistrationCertURL),
            nameof(VendorRegistrationFormDto.CIN),
            nameof(VendorRegistrationFormDto.CINCertURL)
        };
        private readonly List<string> _registrationSection2Fields = new()
        {
            nameof(VendorRegistrationFormDto.AadharNo),
            nameof(VendorRegistrationFormDto.AadharDocURL),
            nameof(VendorRegistrationFormDto.UDYAMRegNo),
            nameof(VendorRegistrationFormDto.UDYAMRegCertURL),
            nameof(VendorRegistrationFormDto.PFNo),
            nameof(VendorRegistrationFormDto.PFRegCertURL),
            nameof(VendorRegistrationFormDto.ESIRegNo),
            nameof(VendorRegistrationFormDto.ESIRegCertURL)

        };
        private readonly List<string> _bankAccountSectionFields = new()
        {
            nameof(VendorRegistrationFormDto.BankName),
            nameof(VendorRegistrationFormDto.BankBranch),
            nameof(VendorRegistrationFormDto.IFSCCode),
            nameof(VendorRegistrationFormDto.AccountNumber),
            nameof(VendorRegistrationFormDto.AccountName)
        };

        // Section validity flags
        private bool _isRegistrationSection1Valid = true;
        private bool _isRegistrationSection2Valid = true;
        private bool _isBankAccountSectionValid = true;
        private bool _isOrganizationSectionValid = true;
        private bool _isAddressSectionValid = true;
        private bool _isContactSectionValid = true;

        public RegisterVendor()
        {
            // DO NOT initialize EditContext here. Initializing in ctor causes InvokeAsync/StateHasChanged
            // calls to fail because the RenderHandle isn't assigned yet.
        }

        // Lifecycle
        protected override async Task OnInitializedAsync()
        {
            _isLoading = true;
            try
            {
                InitializeEditContext(_vendorReg);

                await LoadUserContextAsync();
                _isApprover = !string.IsNullOrWhiteSpace(_userRole) && _userRole.IndexOf("Vendor Approver", StringComparison.OrdinalIgnoreCase) >= 0;
                _isReviewer = !string.IsNullOrWhiteSpace(_userRole) && _userRole.IndexOf("Vendor Validator", StringComparison.OrdinalIgnoreCase) >= 0;

                try
                {
                    await LoadLookupsAsync();
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "LoadLookupsAsync failed during initialization");
                }
                // after lookups are loaded (or after copying loaded model)
                if (_vendorReg.CompanyOwnershipId != Guid.Empty && _companyOwnerships?.Any() == true)
                {
                    var sel = _companyOwnerships.FirstOrDefault(c => c.Id == _vendorReg.CompanyOwnershipId);
                    if (sel != null)
                        _vendorReg.CompanyOwnershipType = sel.CompanyOwnershipType;
                }
                if (VendorRegistrationFormId != Guid.Empty)
                {
                    try
                    {
                        var loaded = await VendorRegistrationFormRepository.GetVendorRegistrationFormByIdAsync(VendorRegistrationFormId);
                        if (loaded != null)
                        {
                            CopyModelValues(loaded, _vendorReg);

                            // Set selected services for multi-select UI
                            if (!string.IsNullOrWhiteSpace(_vendorReg.VendorServices))
                            {
                                var selected = _vendorReg.VendorServices
                                    .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                                    .Select(s => s.Trim())
                                    .Where(s => !string.IsNullOrEmpty(s))
                                    .Distinct()
                                    .ToList();

                                // assign only the vendortypes present on the model
                                _selectedServices = selected;
                            }
                            //Lock/unlock dynamic required fields based on loaded data
                            LockUlockForm();
                            // Notify EditContext that fields changed so components re-evaluate binding/validation
                            foreach (var prop in typeof(VendorRegistrationFormDto).GetProperties().Where(p => p.CanRead))
                            {
                                _editContext?.NotifyFieldChanged(new FieldIdentifier(_vendorReg, prop.Name));
                            }
                            _formEditMode = "Edit";
                            // Re-evaluate section validity after copying values
                            UpdateOrganizationSectionValidity();
                            UpdateAddresSectionValidity();
                            UpdateContactSectionValidity();
                            UpdateRegistrationSection1Validity();
                            UpdateRegistrationSection2Validity();
                            UpdateBankAccountSectionValidity();

                            // Ensure UI updates on renderer thread


                            await InvokeAsync(StateHasChanged);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Failed to load vendor registration during initialization");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to load vendor registration during initialization");
                throw;
            }
            finally
            {
                _isLoading = false;
                // ensure UI refresh when loading completes
                await InvokeAsync(StateHasChanged);
            }
        }

        private void LockUlockForm()
        {
            try
            {
                // safe defaults: lock everything
                _isReadOnly = true;
                _isReviewLocked = true;
                _isApproverLocked = true;
                //_isReviewer = false;
                //_isApprover = false;

                // Resolve auth state synchronously because this runs during init
                var authState = AuthState?.GetAwaiter().GetResult();
                var user = authState?.User;

                // If no user is logged in -> lock review/approval and exit early
                if (user == null || user.Identity == null || user.Identity.IsAuthenticated == false)
                {
                    _isApproverLocked = true;
                    _isReviewLocked = true;
                    _isReadOnly = true;
                    return;
                }

                // determine role/userType from previously loaded context (LoadUserContextAsync sets _userType/_userRole)
                var role = (_userRole ?? string.Empty).Trim();
                //var isVendorType = (!string.IsNullOrWhiteSpace(_userType) && _userType.Contains("VENDOR", StringComparison.OrdinalIgnoreCase))
                //                   || (!string.IsNullOrWhiteSpace(role) && role.IndexOf("VENDOR", StringComparison.OrdinalIgnoreCase) >= 0);

                static bool HasVendorToken(string? input)
                {
                    if (string.IsNullOrWhiteSpace(input)) return false;
                    var separators = new[] { ';', ',', '|', '/' };
                    var parts = input.Split(separators, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim());
                    foreach (var part in parts)
                    {
                        // split part into words by common separators so "Vendor Approver", "VENDOR_APPROVER" and "Vendor-Approver" match
                        var words = part.Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
                        if (words.Any(w => string.Equals(w, "VENDOR", StringComparison.OrdinalIgnoreCase)))
                            return true;
                    }
                    return false;
                }
                var isVendorType = false;
                isVendorType = (!string.IsNullOrWhiteSpace(_userType) && string.Equals(_userType.Trim(), "VENDOR", StringComparison.OrdinalIgnoreCase))
                                   || HasVendorToken(role);

                // compute responder identity (may be used to allow vendor responder edits until approved)
                var username = user.Identity?.Name
                               ?? user.FindFirst("username")?.Value
                               ?? user.FindFirst("email")?.Value
                               ?? user.FindFirst(ClaimTypes.Name)?.Value
                               ?? string.Empty;

                var isResponder = !string.IsNullOrWhiteSpace(username)
                                  && string.Equals(username.Trim(), (_vendorReg.ResponderEmailId ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

                var reviewerStatus = (_vendorReg.ReviewerStatus ?? "Pending").Trim();
                var approverStatus = (_vendorReg.ApproverStatus ?? "Pending").Trim();

                // If user is a vendor type -> lock review/approval and prevent acting as reviewer/approver
                if (isVendorType)
                {
                    _isApproverLocked = true;
                    _isReviewLocked = true;
                    //_isReviewer = false;
                    //_isApprover = false;

                    // vendor responder may edit their own form until it's approved
                    _isReadOnly = isResponder ? !string.Equals(approverStatus, "Approved", StringComparison.OrdinalIgnoreCase) : true;
                    return;
                }

                // Non-vendor flows: core form fields remain read-only (only vendor responder edits core data)
                _isReadOnly = true;

                // Reviewer rules: reviewers cannot approve
                if (_isReviewer)
                {
                    _isReviewLocked = !string.Equals(reviewerStatus, "Pending", StringComparison.OrdinalIgnoreCase);
                    _isApproverLocked = true;
                }

                // Approver rules
                if (_isApprover)
                {
                    if (string.Equals(reviewerStatus, "Rejected", StringComparison.OrdinalIgnoreCase))
                    {
                        _isApproverLocked = true;
                    }
                    else if (string.Equals(reviewerStatus, "Pending", StringComparison.OrdinalIgnoreCase))
                    {
                        // approver may act only when their status is Pending
                        _isApproverLocked = true;
                    } else if (string.Equals(reviewerStatus, "Approved", StringComparison.OrdinalIgnoreCase))
                    {
                        _isApproverLocked = false;
                    }

                    // approver should not change review fields
                    _isReviewLocked = true;
                }
                else
                {
                    // ensure approver section locked when user is not approver
                    _isApproverLocked = true;
                }
            }
            catch
            {
                // conservative fallback
                _isReadOnly = true;
                _isReviewLocked = true;
                _isApproverLocked = true;
                _isReviewer = false;
                _isApprover = false;
            }
        }

        // EditContext initialization
        private void InitializeEditContext(VendorRegistrationFormDto model)
        {
            // unsubscribe previous handlers (if any)
            try
            {
                if (_editContext != null)
                {
                    _editContext.OnFieldChanged -= EditContext_OnFieldChanged;
                    if (_clearHandler != null) _editContext.OnFieldChanged -= _clearHandler;
                    _editContext.OnFieldChanged -= SectionFieldChangedHandler;
                }
            }
            catch
            {
                // ignore unsubscribe problems
            }

            // set new edit context and handlers
            _editContext = new EditContext(model ?? new VendorRegistrationFormDto());
            _editContext.OnFieldChanged += EditContext_OnFieldChanged;

            _messageStore = new ValidationMessageStore(_editContext);
            _clearHandler = (sender, args) =>
            {
                _messageStore?.Clear(args.FieldIdentifier);
                _editContext.NotifyValidationStateChanged();
            };
            _editContext.OnFieldChanged += _clearHandler;

            // Watch field changes to refresh section validity
            _editContext.OnFieldChanged += SectionFieldChangedHandler;

            // Evaluate initial section validity
            UpdateOrganizationSectionValidity();
            UpdateAddresSectionValidity();
            UpdateContactSectionValidity();
            UpdateRegistrationSection1Validity();
            UpdateRegistrationSection2Validity();
            UpdateBankAccountSectionValidity();

            // DO NOT call InvokeAsync(StateHasChanged) here - the render handle may not be assigned.
        }

        private void SectionFieldChangedHandler(object? sender, FieldChangedEventArgs args)
        {
            // Only recalc if the changed field belongs to the organization section
            if (_organizationSectionFields.Contains(args.FieldIdentifier.FieldName))
                UpdateOrganizationSectionValidity();
            if (_addresSectionFields.Contains(args.FieldIdentifier.FieldName))
                UpdateAddresSectionValidity();
            if (_contactSectionFields.Contains(args.FieldIdentifier.FieldName))
                UpdateContactSectionValidity();
            if (_registrationSection1Fields.Contains(args.FieldIdentifier.FieldName))
                UpdateRegistrationSection1Validity();
            if (_registrationSection2Fields.Contains(args.FieldIdentifier.FieldName))
                UpdateRegistrationSection2Validity();
            if (_bankAccountSectionFields.Contains(args.FieldIdentifier.FieldName))
                UpdateBankAccountSectionValidity();
        }

        // Lookups
        private async Task LoadLookupsAsync()
        {
            try
            {
                // Load company ownerships
                _companyOwnerships = await CompanyOwnershipRepository.GetAllCompanyOwnershipsAsync() ?? new List<CompanyOwnership>();

                // Load vendor services
                _vendorServices = await VendorServiceRepository.GetAllVendorServices() ?? new List<VendorService>();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error loading lookups in LoadLookupsAsync");
                Snackbar.Add("Failed to load lookup data.", Severity.Error);
            }
        }

        // Simple property copy helper
        private static void CopyModelValues(VendorRegistrationFormDto source, VendorRegistrationFormDto target)
        {
            if (source == null || target == null) return;

            var props = typeof(VendorRegistrationFormDto).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                        .Where(p => p.CanRead && p.CanWrite);

            foreach (var p in props)
            {
                try
                {
                    var val = p.GetValue(source);
                    p.SetValue(target, val);
                }
                catch
                {
                    // Ignore individual property copy failures - preserve best-effort copy.
                }
            }
        }

        private void EditContext_OnFieldChanged(object? sender, FieldChangedEventArgs e)
        {
            if (e.FieldIdentifier.FieldName == nameof(VendorRegistrationFormDto.IsMSMERegistered))
            {
                //HandleMSMSSelectdChange();
            }
        }

        // Validation: repeated lists used across validation helpers
        private readonly List<string> _organizationSectionFields = new()
        {
            nameof(VendorRegistrationFormDto.OrganizationName),
            nameof(VendorRegistrationFormDto.CompanyOwnershipType),
            nameof(VendorRegistrationFormDto.VendorServices),
            nameof(VendorRegistrationFormDto.CompanyPhoneNo),
            nameof(VendorRegistrationFormDto.CompanyEmailID)
        };
        private readonly List<string> _addresSection_fields_for_decl = new()
        {
            nameof(VendorRegistrationFormDto.RegAddress1),
            nameof(VendorRegistrationFormDto.RegCity)
        };

        // Note: keep the original private lists with their prior names — use the earlier declared _addresSectionFields below for logic.
        private readonly List<string> _addresSectionFields = new()
        {
            nameof(VendorRegistrationFormDto.RegAddress1),
            nameof(VendorRegistrationFormDto.RegCity)
        };

        private readonly List<string> _contactSectionFields = new()
        {
            nameof(VendorRegistrationFormDto.ResponderFName),
            nameof(VendorRegistrationFormDto.ResponderLName),
            nameof(VendorRegistrationFormDto.ResponderDesignation),
            nameof(VendorRegistrationFormDto.ResponderMobileNo),
            nameof(VendorRegistrationFormDto.ResponderEmailId)
        };

        // Validation helpers (organization)
        private void UpdateOrganizationSectionValidity()
        {
            if (_editContext == null || _vendorReg == null)
            {
                _isOrganizationSectionValid = true;
                return;
            }

            var anyInvalid = false;

            foreach (var propName in _organizationSectionFields)
            {
                var prop = typeof(VendorRegistrationFormDto).GetProperty(propName);
                if (prop == null) continue;

                var fieldId = new FieldIdentifier(_vendorReg, propName);
                var value = prop.GetValue(_vendorReg);
                var results = new List<ValidationResult>();
                var context = new ValidationContext(_vendorReg) { MemberName = propName };

                // Run DataAnnotations for the property (do not write messages to the UI here)
                Validator.TryValidateProperty(value, context, results);

                var isEmailField = string.Equals(propName, nameof(VendorRegistrationFormDto.CompanyEmailID), StringComparison.Ordinal);
                var isPhoneField = string.Equals(propName, nameof(VendorRegistrationFormDto.CompanyPhoneNo), StringComparison.Ordinal);

                // Always clear any existing transient messages for email/phone so they don't linger
                if (isEmailField || isPhoneField)
                {
                    _messageStore?.Clear(fieldId);

                    // If DataAnnotation fails or the required check fails, mark section invalid
                    if (results.Any())
                    {
                        anyInvalid = true;
                        continue; // do not add messages here — messages are shown on blur only
                    }
                }
                else
                {
                    if (results.Any())
                    {
                        anyInvalid = true;
                        break;
                    }
                }

                // Required-checks (explicit list + [Required] attributes)
                var isRequired = prop.GetCustomAttributes(typeof(RequiredAttribute), inherit: true).Any()
                                 || propName == nameof(VendorRegistrationFormDto.OrganizationName)
                                 || propName == nameof(VendorRegistrationFormDto.CompanyOwnershipType)
                                 || propName == nameof(VendorRegistrationFormDto.VendorServices)
                                 || propName == nameof(VendorRegistrationFormDto.CompanyPhoneNo)
                                 || propName == nameof(VendorRegistrationFormDto.CompanyEmailID);

                if (isRequired)
                {
                    if (prop.PropertyType == typeof(string))
                    {
                        if (string.IsNullOrWhiteSpace((string?)value))
                        {
                            // For email/phone: mark invalid but do not add messages here (defer to OnBlur handlers)
                            if (isEmailField || isPhoneField)
                            {
                                anyInvalid = true;
                                continue;
                            }

                            anyInvalid = true;
                            break;
                        }
                    }
                    else if (prop.PropertyType == typeof(Guid) || Nullable.GetUnderlyingType(prop.PropertyType) == typeof(Guid))
                    {
                        if (value == null || (Guid)value == Guid.Empty)
                        {
                            anyInvalid = true;
                            break;
                        }
                    }
                }

                // Do not perform immediate format message addition here — that is handled in ValidateEmailOnBlur/ValidatePhoneOnBlur.
            }

            var newState = !anyInvalid;
            if (newState != _isOrganizationSectionValid)
            {
                _isOrganizationSectionValid = newState;
                _ = InvokeAsync(StateHasChanged);
            }
        }

        // Validation: address section
        private void UpdateAddresSectionValidity()
        {
            if (_editContext == null || _vendorReg == null)
            {
                _isAddressSectionValid = true;
                return;
            }

            var anyInvalid = false;

            foreach (var propName in _addresSectionFields)
            {
                var prop = typeof(VendorRegistrationFormDto).GetProperty(propName);
                if (prop == null) continue;

                var value = prop.GetValue(_vendorReg);
                var results = new List<ValidationResult>();
                var context = new ValidationContext(_vendorReg) { MemberName = propName };

                // Run DataAnnotations for the property (do not write messages)
                Validator.TryValidateProperty(value, context, results);
                if (results.Any())
                {
                    anyInvalid = true;
                    break;
                }

                // Treat these specific address fields as required
                var isRequired = prop.GetCustomAttributes(typeof(RequiredAttribute), inherit: true).Any()
                                 || propName == nameof(VendorRegistrationFormDto.RegAddress1)
                                 || propName == nameof(VendorRegistrationFormDto.RegCity);

                if (isRequired)
                {
                    if (prop.PropertyType == typeof(string))
                    {
                        if (string.IsNullOrWhiteSpace((string?)value))
                        {
                            anyInvalid = true;
                            break;
                        }
                    }
                    else if (prop.PropertyType == typeof(Guid) || Nullable.GetUnderlyingType(prop.PropertyType) == typeof(Guid))
                    {
                        if (value == null || (Guid)value == Guid.Empty)
                        {
                            anyInvalid = true;
                            break;
                        }
                    }
                }
            }

            var newState = !anyInvalid;
            if (newState != _isAddressSectionValid)
            {
                _isAddressSectionValid = newState;
                _ = InvokeAsync(StateHasChanged);
            }
        }

        // Validation: contact section
        private void UpdateContactSectionValidity()
        {
            if (_editContext == null || _vendorReg == null)
            {
                _isContactSectionValid = true;
                return;
            }

            var anyInvalid = false;

            foreach (var propName in _contactSectionFields)
            {
                var prop = typeof(VendorRegistrationFormDto).GetProperty(propName);
                if (prop == null) continue;

                var value = prop.GetValue(_vendorReg);
                var results = new List<ValidationResult>();
                var context = new ValidationContext(_vendorReg) { MemberName = propName };

                // Run DataAnnotations for the single property but do NOT write messages to the UI
                Validator.TryValidateProperty(value, context, results);
                if (results.Any())
                {
                    anyInvalid = true;
                    break;
                }

                // Additional simple required checks for contact fields
                var isRequired = prop.GetCustomAttributes(typeof(RequiredAttribute), inherit: true).Any()
                                 || propName == nameof(VendorRegistrationFormDto.ResponderFName)
                                 || propName == nameof(VendorRegistrationFormDto.ResponderMobileNo)
                                 || propName == nameof(VendorRegistrationFormDto.ResponderEmailId);

                if (isRequired)
                {
                    if (prop.PropertyType == typeof(string))
                    {
                        if (string.IsNullOrWhiteSpace((string?)value))
                        {
                            anyInvalid = true;
                            break;
                        }
                    }
                    else if (prop.PropertyType == typeof(Guid) || Nullable.GetUnderlyingType(prop.PropertyType) == typeof(Guid))
                    {
                        if (value == null || (Guid)value == Guid.Empty)
                        {
                            anyInvalid = true;
                            break;
                        }
                    }
                }
            }

            var newState = !anyInvalid;
            if (newState != _isContactSectionValid)
            {
                _isContactSectionValid = newState;
                _ = InvokeAsync(StateHasChanged);
            }
        }



        // Registration Section 1 (New)
        private void UpdateRegistrationSection1Validity()
        {
            if (_editContext == null || _vendorReg == null)
            {
                _isRegistrationSection1Valid = true;
                return;
            }

            var anyInvalid = false;

            foreach (var propName in _registrationSection2Fields)
            {
                var prop = typeof(VendorRegistrationFormDto).GetProperty(propName);
                if (prop == null) continue;

                var value = prop.GetValue(_vendorReg);

                // Run DataAnnotations for the property (if any)
                var results = new List<ValidationResult>();
                var context = new ValidationContext(_vendorReg) { MemberName = propName };
                Validator.TryValidateProperty(value, context, results);
                if (results.Any())
                {
                    anyInvalid = true;
                    break;
                }

                // Determine if this field is required by attribute OR by runtime dynamic flags
                var isRequired = prop.GetCustomAttributes(typeof(RequiredAttribute), inherit: true).Any();

                if (string.Equals(propName, nameof(VendorRegistrationFormDto.PANNo), StringComparison.Ordinal) && _isPANRequired)
                    isRequired = true;
                if (string.Equals(propName, nameof(VendorRegistrationFormDto.PANCardURL), StringComparison.Ordinal) && _isPANRequired)
                    isRequired = true;

                if (string.Equals(propName, nameof(VendorRegistrationFormDto.TANNo), StringComparison.Ordinal) && _isTANRequired)
                    isRequired = true;
                if (string.Equals(propName, nameof(VendorRegistrationFormDto.TANCertURL), StringComparison.Ordinal) && _isTANRequired)
                    isRequired = true;


                if (string.Equals(propName, nameof(VendorRegistrationFormDto.GSTNO), StringComparison.Ordinal) && _isGSTRequired)
                    isRequired = true;
                if (string.Equals(propName, nameof(VendorRegistrationFormDto.GSTRegistrationCertURL), StringComparison.Ordinal) && _isGSTRequired)
                    isRequired = true;


                if (string.Equals(propName, nameof(VendorRegistrationFormDto.CIN), StringComparison.Ordinal) && _isCINRequired)
                    isRequired = true;
                if (string.Equals(propName, nameof(VendorRegistrationFormDto.CINCertURL), StringComparison.Ordinal) && _isCINRequired)
                    isRequired = true;

                if (isRequired)
                {
                    if (prop.PropertyType == typeof(string))
                    {
                        if (string.IsNullOrWhiteSpace((string?)value))
                        {
                            anyInvalid = true;
                            break;
                        }
                    }
                    else if (prop.PropertyType == typeof(Guid) || Nullable.GetUnderlyingType(prop.PropertyType) == typeof(Guid))
                    {
                        if (value == null || (Guid)value == Guid.Empty)
                        {
                            anyInvalid = true;
                            break;
                        }
                    }
                }
            }

            var newState = !anyInvalid;
            if (newState != _isRegistrationSection2Valid)
            {
                _isRegistrationSection2Valid = newState;
                _ = InvokeAsync(StateHasChanged);
            }
        }
        // Registration section 2
        private void UpdateRegistrationSection2Validity()
        {
            if (_editContext == null || _vendorReg == null)
            {
                _isRegistrationSection2Valid = true;
                return;
            }

            var anyInvalid = false;

            foreach (var propName in _registrationSection2Fields)
            {
                var prop = typeof(VendorRegistrationFormDto).GetProperty(propName);
                if (prop == null) continue;

                var value = prop.GetValue(_vendorReg);

                // Run DataAnnotations for the property (if any)
                var results = new List<ValidationResult>();
                var context = new ValidationContext(_vendorReg) { MemberName = propName };
                Validator.TryValidateProperty(value, context, results);
                if (results.Any())
                {
                    anyInvalid = true;
                    break;
                }

                // Determine if this field is required by attribute OR by runtime dynamic flags
                var isRequired = prop.GetCustomAttributes(typeof(RequiredAttribute), inherit: true).Any();

                if (string.Equals(propName, nameof(VendorRegistrationFormDto.AadharNo), StringComparison.Ordinal) && _isAadharRequired)
                    isRequired = true;
                if (string.Equals(propName, nameof(VendorRegistrationFormDto.AadharDocURL), StringComparison.Ordinal) && _isAadharRequired)
                    isRequired = true;

                if (string.Equals(propName, nameof(VendorRegistrationFormDto.UDYAMRegNo), StringComparison.Ordinal) && _isUDYAMRequired)
                    isRequired = true;
                if (string.Equals(propName, nameof(VendorRegistrationFormDto.UDYAMRegCertURL), StringComparison.Ordinal) && _isUDYAMRequired)
                    isRequired = true;

                if (string.Equals(propName, nameof(VendorRegistrationFormDto.PFNo), StringComparison.Ordinal) && _isPFRequired)
                    isRequired = true;
                if (string.Equals(propName, nameof(VendorRegistrationFormDto.PFRegCertURL), StringComparison.Ordinal) && _isPFRequired)
                    isRequired = true;

                if (string.Equals(propName, nameof(VendorRegistrationFormDto.ESIRegNo), StringComparison.Ordinal) && _isESIRequired)
                    isRequired = true;
                if (string.Equals(propName, nameof(VendorRegistrationFormDto.ESIRegCertURL), StringComparison.Ordinal) && _isESIRequired)
                    isRequired = true;

                if (isRequired)
                {
                    if (prop.PropertyType == typeof(string))
                    {
                        if (string.IsNullOrWhiteSpace((string?)value))
                        {
                            anyInvalid = true;
                            break;
                        }
                    }
                    else if (prop.PropertyType == typeof(Guid) || Nullable.GetUnderlyingType(prop.PropertyType) == typeof(Guid))
                    {
                        if (value == null || (Guid)value == Guid.Empty)
                        {
                            anyInvalid = true;
                            break;
                        }
                    }
                }
            }

            var newState = !anyInvalid;
            if (newState != _isRegistrationSection2Valid)
            {
                _isRegistrationSection2Valid = newState;
                _ = InvokeAsync(StateHasChanged);
            }
        }

        // Bank account validation
        private void UpdateBankAccountSectionValidity()
        {
            if (_editContext == null || _vendorReg == null)
            {
                _isBankAccountSectionValid = true;
                return;
            }

            var anyInvalid = false;

            foreach (var propName in _bankAccountSectionFields)
            {
                var fieldId = new FieldIdentifier(_vendorReg, propName);

                // Clear any stale validation messages for this field so we re-evaluate current model state.
                _messageStore?.Clear(fieldId);

                // Validate property using DataAnnotations
                var prop = typeof(VendorRegistrationFormDto).GetProperty(propName);
                if (prop != null)
                {
                    var value = prop.GetValue(_vendorReg);
                    var results = new List<ValidationResult>();
                    var context = new ValidationContext(_vendorReg) { MemberName = propName };
                    Validator.TryValidateProperty(value, context, results);

                    var isRequired = prop.GetCustomAttributes(typeof(RequiredAttribute), inherit: true).Any()
                                     || propName == nameof(VendorRegistrationFormDto.BankName)
                                     || propName == nameof(VendorRegistrationFormDto.BankBranch)
                                     || propName == nameof(VendorRegistrationFormDto.IFSCCode)
                                     || propName == nameof(VendorRegistrationFormDto.AccountName)
                                     || propName == nameof(VendorRegistrationFormDto.AccountNumber)
                                     || propName == nameof(VendorRegistrationFormDto.CancelledChequeURL);

                    if (isRequired && prop.PropertyType == typeof(string))
                    {
                        if (string.IsNullOrWhiteSpace((string?)value))
                        {
                            anyInvalid = true;
                            continue;
                        }
                    }
                }
            }

            // Ensure validation UI refreshes with the messages we (re)added/cleared above.
            _editContext.NotifyValidationStateChanged();

            var newState = !anyInvalid;
            if (newState != _isBankAccountSectionValid)
            {
                _isBankAccountSectionValid = newState;
                _ = InvokeAsync(StateHasChanged);
            }
        }

        // Upload handlers
        private async Task OnPANUploaded(string? url)
        {
            _vendorReg.PANCardURL = url ?? string.Empty;
            await Task.CompletedTask;
        }

        private async Task OnTANUploaded(string? url)
        {
            _vendorReg.TANCertURL = url ?? string.Empty;
            await Task.CompletedTask;
        }

        private async Task OnGSTUploaded(string? url)
        {
            _vendorReg.GSTRegistrationCertURL = url ?? string.Empty;
            await Task.CompletedTask;
        }

        private async Task OnCINUploaded(string? url)
        {
            _vendorReg.CINCertURL = url ?? string.Empty;
            await Task.CompletedTask;
        }

        private Task OnAADHARUploaded(string? url)
        {
            _vendorReg.AadharDocURL = url ?? string.Empty;
            return Task.CompletedTask;
        }

        private async Task OnUDYAMUploaded(string? url)
        {
            _vendorReg.UDYAMRegCertURL = url ?? string.Empty;
            await Task.CompletedTask;
        }

        private async Task OnPFUploaded(string? url)
        {
            _vendorReg.PFRegCertURL = url ?? string.Empty;
            await Task.CompletedTask;
        }

        private async Task OnESIUploaded(string? url)
        {
            _vendorReg.ESIRegCertURL = url ?? string.Empty;
            await Task.CompletedTask;
        }

        private async Task OnCancelChequeUploaded(string? url)
        {
            _vendorReg.CancelledChequeURL = url ?? string.Empty;
            await Task.CompletedTask;
        }

        // Company ownership change
        //private async Task OnCompanyOwnershipChanged()
        //{
        //    var companyOwnershipType = _vendorReg.CompanyOwnershipType?.Trim() ?? string.Empty;
        //    var match = _companyOwnerships.FirstOrDefault(c => string.Equals(c.CompanyOwnershipType, companyOwnershipType, StringComparison.OrdinalIgnoreCase));
        //    if (match != null)
        //    {
        //        _vendorReg.CompanyOwnershipId = match.Id;
        //        _vendorReg.CompanyOwnershipType = match.CompanyOwnershipType;
        //        ApplyCompanyOwnershipRequirements(match.Id);
        //    }
        //    else
        //    {
        //        _vendorReg.CompanyOwnershipId = Guid.Empty;
        //    }

        //    StateHasChanged();
        //    await Task.CompletedTask;
        //}
        // Replace the existing parameterless method with this:
        private async Task OnCompanyOwnershipChanged(string? selectedType)
        {
            var companyOwnershipType = (selectedType ?? string.Empty).Trim();

            // keep the model in sync so the select displays correctly
            _vendorReg.CompanyOwnershipType = companyOwnershipType;
            _selectedCompanyOwnershipType = companyOwnershipType;

            var match = _companyOwnerships.FirstOrDefault(c =>
                string.Equals(c.CompanyOwnershipType?.Trim(), companyOwnershipType, StringComparison.OrdinalIgnoreCase));

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

            await InvokeAsync(StateHasChanged);
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

            // ownership-change can affect section validity
            UpdateOrganizationSectionValidity();
            UpdateRegistrationSection1Validity();

            // <-- ensure registration section 2 re-checks because dynamic flags changed
            UpdateRegistrationSection2Validity();
        }

        private async Task HandleMSMESelectedChange(bool newValue)
        {
            _vendorReg.IsMSMERegistered = newValue;
            _IsMSMERegistered = newValue;
            _isUDYAMRequired = _vendorReg.IsMSMERegistered == true;

            UpdateOrganizationSectionValidity();

            // MSME toggling affects UDYAM requirement -> re-evaluate registration section 2
            UpdateRegistrationSection2Validity();

            await Task.CompletedTask;
        }

        // Per-field validation helper
        private Task ValidateField(string propertyName)
        {
            if (_editContext == null || _vendorReg == null)
                return Task.CompletedTask;

            var fieldId = new FieldIdentifier(_vendorReg, propertyName);
            _messageStore?.Clear(fieldId);

            var prop = typeof(VendorRegistrationFormDto).GetProperty(propertyName);
            if (prop != null)
            {
                var value = prop.GetValue(_vendorReg);
                var results = new List<ValidationResult>();
                var context = new ValidationContext(_vendorReg) { MemberName = propertyName };

                // Run DataAnnotations for the property
                Validator.TryValidateProperty(value, context, results);
                foreach (var r in results)
                    _messageStore?.Add(fieldId, r.ErrorMessage ?? string.Empty);

                // Compute requiredness from attributes and known dynamic flags / UI expectations
                var isRequired = prop.GetCustomAttributes(typeof(RequiredAttribute), inherit: true).Any();

                // Treat contact fields as required by UI logic (consistent with UpdateContactSectionValidity)
                if (string.Equals(propertyName, nameof(VendorRegistrationFormDto.ResponderFName), StringComparison.Ordinal) ||
                    string.Equals(propertyName, nameof(VendorRegistrationFormDto.ResponderMobileNo), StringComparison.Ordinal) ||
                    string.Equals(propertyName, nameof(VendorRegistrationFormDto.ResponderEmailId), StringComparison.Ordinal))
                {
                    isRequired = true;
                }


                // For registration section1 dynamic flags
                if (string.Equals(propertyName, nameof(VendorRegistrationFormDto.PANNo), StringComparison.Ordinal) && _isPANRequired)
                    isRequired = true;
                if (string.Equals(propertyName, nameof(VendorRegistrationFormDto.PANCardURL), StringComparison.Ordinal) && _isPANRequired)
                    isRequired = true;

                if (string.Equals(propertyName, nameof(VendorRegistrationFormDto.TANNo), StringComparison.Ordinal) && _isTANRequired)
                    isRequired = true;
                if (string.Equals(propertyName, nameof(VendorRegistrationFormDto.TANCertURL), StringComparison.Ordinal) && _isTANRequired)
                    isRequired = true;

                if (string.Equals(propertyName, nameof(VendorRegistrationFormDto.GSTNO), StringComparison.Ordinal) && _isGSTRequired)
                    isRequired = true;
                if (string.Equals(propertyName, nameof(VendorRegistrationFormDto.GSTRegistrationCertURL), StringComparison.Ordinal) && _isGSTRequired)
                    isRequired = true;

                if (string.Equals(propertyName, nameof(VendorRegistrationFormDto.CIN), StringComparison.Ordinal) && _isCINRequired)
                    isRequired = true;
                if (string.Equals(propertyName, nameof(VendorRegistrationFormDto.CINCertURL), StringComparison.Ordinal) && _isCINRequired)
                    isRequired = true;

                // For registration section2 dynamic flags
                if (string.Equals(propertyName, nameof(VendorRegistrationFormDto.AadharNo), StringComparison.Ordinal) && _isAadharRequired)
                    isRequired = true;

                if (string.Equals(propertyName, nameof(VendorRegistrationFormDto.UDYAMRegNo), StringComparison.Ordinal) && _isUDYAMRequired)
                    isRequired = true;
                if (string.Equals(propertyName, nameof(VendorRegistrationFormDto.PFNo), StringComparison.Ordinal) && _isPFRequired)
                    isRequired = true;
                if (string.Equals(propertyName, nameof(VendorRegistrationFormDto.ESIRegNo), StringComparison.Ordinal) && _isESIRequired)
                    isRequired = true;

                if (isRequired)
                {
                    if (prop.PropertyType == typeof(string))
                    {
                        if (string.IsNullOrWhiteSpace((string?)value))
                            _messageStore?.Add(fieldId, "This field is required.");
                    }
                    else if (prop.PropertyType == typeof(Guid) || Nullable.GetUnderlyingType(prop.PropertyType) == typeof(Guid))
                    {
                        if (value == null || (Guid)value == Guid.Empty)
                            _messageStore?.Add(fieldId, "This field is required.");
                    }
                }
            }

            // Refresh validation UI
            _editContext.NotifyValidationStateChanged();

            // Recompute section validity for the changed field
            if (_contactSectionFields.Contains(propertyName))
                UpdateContactSectionValidity();
            else if (_organizationSectionFields.Contains(propertyName))
                UpdateOrganizationSectionValidity();
            else if (_registrationSection2Fields.Contains(propertyName))
                UpdateRegistrationSection2Validity();
            else if (_registrationSection1Fields.Contains(propertyName))
                UpdateRegistrationSection1Validity();
            else if (_bankAccountSectionFields.Contains(propertyName))
                UpdateBankAccountSectionValidity();

            return Task.CompletedTask;
        }

        // Email & phone blur validators
        private Task ValidateEmailOnBlur(string CalledFrom)
        {
            if (_editContext == null || _vendorReg == null) return Task.CompletedTask;

            var fieldName = nameof(VendorRegistrationFormDto.CompanyEmailID);
            var fieldId = new FieldIdentifier(_vendorReg, fieldName);

            // Clear previous messages for this field
            _messageStore?.Clear(fieldId);

            var email = _vendorReg.CompanyEmailID ?? string.Empty;
            var results = new List<ValidationResult>();
            var context = new ValidationContext(_vendorReg) { MemberName = fieldName };

            // Run any DataAnnotation validation for the property
            Validator.TryValidateProperty(email, context, results);
            foreach (var r in results)
                _messageStore?.Add(fieldId, r.ErrorMessage ?? string.Empty);

            // UI-level format check (only add message if the field is non-empty or if it's required and empty)
            if (!string.IsNullOrWhiteSpace(email))
            {
                var emailPattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";
                if (!Regex.IsMatch(email, emailPattern))
                    _messageStore?.Add(fieldId, "Enter a valid email address.");
            }
            else
            {
                // if the property is required, show required message on blur
                var prop = typeof(VendorRegistrationFormDto).GetProperty(fieldName);
                var isRequired = prop?.GetCustomAttributes(typeof(RequiredAttribute), inherit: true).Any() ?? false;
                if (isRequired)
                    _messageStore?.Add(fieldId, "This field is required.");
            }

            _editContext.NotifyValidationStateChanged();

            // update section boolean as well
            if (CalledFrom == "Contact")
                UpdateContactSectionValidity();
            else if (CalledFrom == "Organization")
                UpdateOrganizationSectionValidity();
            return Task.CompletedTask;
        }

        private Task ValidatePhoneOnBlur(string CalledFrom)
        {
            if (_editContext == null || _vendorReg == null) return Task.CompletedTask;

            var fieldName = nameof(VendorRegistrationFormDto.CompanyPhoneNo);
            var fieldId = new FieldIdentifier(_vendorReg, fieldName);

            _messageStore?.Clear(fieldId);

            var phone = _vendorReg.CompanyPhoneNo ?? string.Empty;
            var results = new List<ValidationResult>();
            var context = new ValidationContext(_vendorReg) { MemberName = fieldName };

            Validator.TryValidateProperty(phone, context, results);
            foreach (var r in results)
                _messageStore?.Add(fieldId, r.ErrorMessage ?? string.Empty);

            if (!string.IsNullOrWhiteSpace(phone))
            {
                // Accept optional '+' then 7-15 digits (adjust if you need different rules)
                var phonePattern = @"^\+?[0-9]{7,15}$";
                if (!Regex.IsMatch(phone, phonePattern))
                    _messageStore?.Add(fieldId, "Enter a valid phone number (7-15 digits, optional leading '+').");
            }
            else
            {
                var prop = typeof(VendorRegistrationFormDto).GetProperty(fieldName);
                var isRequired = prop?.GetCustomAttributes(typeof(RequiredAttribute), inherit: true).Any() ?? false;
                if (isRequired)
                    _messageStore?.Add(fieldId, "This field is required.");
            }

            _editContext.NotifyValidationStateChanged();
            if (CalledFrom == "Organization")
                UpdateOrganizationSectionValidity();
            else if (CalledFrom == "Contact")
                UpdateContactSectionValidity();

            return Task.CompletedTask;
        }

        // Submit handler and validation
        private async Task HandleSubmit(EditContext editContext)
        {
            var validationResults = new List<ValidationResult>();
            var validationContext = new ValidationContext(_vendorReg, serviceProvider: null, items: null);
            Validator.TryValidateObject(_vendorReg, validationContext, validationResults, validateAllProperties: true);

            if (_isPANRequired && string.IsNullOrWhiteSpace(_vendorReg.PANNo))
                validationResults.Add(new ValidationResult("PAN is required for the selected ownership.", new[] { nameof(_vendorReg.PANNo) }));
            if (_isPANRequired && string.IsNullOrWhiteSpace(_vendorReg.PANCardURL))
                validationResults.Add(new ValidationResult("PAN Card document is required for the selected ownership.", new[] { nameof(_vendorReg.PANCardURL) }));


            if (_isTANRequired && string.IsNullOrWhiteSpace(_vendorReg.TANNo))
                validationResults.Add(new ValidationResult("TAN No is required for the selected ownership.", new[] { nameof(_vendorReg.TANNo) }));
            if (_isTANRequired && string.IsNullOrWhiteSpace(_vendorReg.TANCertURL))
                validationResults.Add(new ValidationResult("TAN Certificate document is required for the selected ownership.", new[] { nameof(_vendorReg.TANCertURL) }));

            if (_isGSTRequired && string.IsNullOrWhiteSpace(_vendorReg.GSTNO))
                validationResults.Add(new ValidationResult("GST No is required for the selected ownership.", new[] { nameof(_vendorReg.GSTNO) }));

            if (_isGSTRequired && string.IsNullOrWhiteSpace(_vendorReg.GSTRegistrationCertURL))
                validationResults.Add(new ValidationResult("GST Registration Certificate document is required for the selected ownership.", new[] { nameof(_vendorReg.GSTRegistrationCertURL) }));

            if (_isCINRequired && string.IsNullOrWhiteSpace(_vendorReg.CIN))
                validationResults.Add(new ValidationResult("CIN is required for the selected ownership.", new[] { nameof(_vendorReg.CIN) }));
            if (_isCINRequired && string.IsNullOrWhiteSpace(_vendorReg.CINCertURL))
                validationResults.Add(new ValidationResult("CIN Certificate document is required for the selected ownership.", new[] { nameof(_vendorReg.CINCertURL) }));


            if (_isAadharRequired && string.IsNullOrWhiteSpace(_vendorReg.AadharNo))
                validationResults.Add(new ValidationResult("Aadhar is required for the selected ownership.", new[] { nameof(_vendorReg.AadharNo) }));
            if (_isAadharRequired && string.IsNullOrWhiteSpace(_vendorReg.AadharDocURL))
                validationResults.Add(new ValidationResult("Aadhar document is required for the selected ownership.", new[] { nameof(_vendorReg.AadharDocURL) }));

            if (_isUDYAMRequired && string.IsNullOrWhiteSpace(_vendorReg.UDYAMRegNo))
                validationResults.Add(new ValidationResult("UDYAM No is required for the selected ownership.", new[] { nameof(_vendorReg.UDYAMRegNo) }));
            if (_isUDYAMRequired && string.IsNullOrWhiteSpace(_vendorReg.UDYAMRegCertURL))
                validationResults.Add(new ValidationResult("UDYAM Registration Certificate document is required for the selected ownership.", new[] { nameof(_vendorReg.UDYAMRegCertURL) }));


            if (_isPFRequired && string.IsNullOrWhiteSpace(_vendorReg.PFNo))
                validationResults.Add(new ValidationResult("PF No is required for the selected ownership.", new[] { nameof(_vendorReg.PFNo) }));
            if (_isPFRequired && string.IsNullOrWhiteSpace(_vendorReg.PFRegCertURL))
                validationResults.Add(new ValidationResult("PF Registration Certificate document is required for the selected ownership.", new[] { nameof(_vendorReg.PFRegCertURL) }));

            if (_isESIRequired && string.IsNullOrWhiteSpace(_vendorReg.ESIRegNo))
                validationResults.Add(new ValidationResult("ESI No is required for the selected ownership.", new[] { nameof(_vendorReg.ESIRegNo) }));
            if (_isESIRequired && string.IsNullOrWhiteSpace(_vendorReg.ESIRegCertURL))
                validationResults.Add(new ValidationResult("ESI Registration Certificate document is required for the selected ownership.", new[] { nameof(_vendorReg.ESIRegCertURL) }));

            _messageStore?.Clear();
            if (validationResults.Any())
            {
                foreach (var result in validationResults)
                {
                    var message = result.ErrorMessage ?? string.Empty;
                    var members = (result.MemberNames ?? Enumerable.Empty<string>()).ToList();

                    if (members.Count == 0)
                    {
                        _messageStore?.Add(new FieldIdentifier(_vendorReg, nameof(_vendorReg.OrganizationName)), message);
                    }
                    else
                    {
                        foreach (var member in members)
                        {
                            _messageStore?.Add(new FieldIdentifier(_vendorReg, member), message);
                        }
                    }
                }

                _editContext?.NotifyValidationStateChanged();

                UpdateOrganizationSectionValidity();
                UpdateAddresSectionValidity();
                UpdateContactSectionValidity();
                UpdateRegistrationSection1Validity();
                UpdateRegistrationSection2Validity();
                UpdateBankAccountSectionValidity();

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
                    NavigationManager.NavigateTo($"/vendor-registrations");
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
                    NavigationManager.NavigateTo($"/vendor-registrations");
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
            if (_userType == null || _userType == "VENDOR")
            {
                // vendor responder cannot edit if approver has approved
                _isReadOnly = string.Equals(approverStatus, "Approved", StringComparison.OrdinalIgnoreCase);
                _isApproverLocked = true;
                _isReviewLocked = true;
            }
            StateHasChanged();
            return Task.CompletedTask;
        }

        private Task OnApproverStatusChanged(string? value)
        {
            _vendorReg.ApproverStatus = value;

            var approverStatus = (_vendorReg.ApproverStatus ?? "Pending").Trim();
            //if (!string.Equals(approverStatus, "Pending", StringComparison.OrdinalIgnoreCase))
            //    _isApproverLocked = true;

            StateHasChanged();
            return Task.CompletedTask;
        }
        // Cancel navigation
        private void OnCancel() => NavigationManager.NavigateTo($"/vendor-registrations/{_vendorReg.Id}");

        // DTO builders
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

        // Map model to DTO (shared)
        private void ApplyCommonProperties(dynamic dto)
        {
            // copy common properties from _vendorReg into dto using dynamic to reduce repetition
            dto.OrganizationName = _vendorReg.OrganizationName;
            dto.ResponderFName = _vendorReg.ResponderFName;
            dto.ResponderLName = _vendorReg.ResponderLName;
            dto.ResponderDesignation = _vendorReg.ResponderDesignation;
            dto.ResponderMobileNo = _vendorReg.ResponderMobileNo;
            dto.ResponderEmailId = _vendorReg.ResponderEmailId;
            //dto.serviceTypeIds = _vendorReg.serviceTypeIds;
            dto.VendorServices = _vendorReg.VendorServices;
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
            // inside ApplyCommonProperties(dynamic dto)
            dto.Experience1ProjectCost = 0; // will be null if not set in form
            dto.Experience2ProjectCost = 0;
            dto.Experience3ProjectCost = 0;
        }

        // Logging helper
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

        // Create / Update calls
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

        // User context loader
        private async Task LoadUserContextAsync()
        {
            var authState = await AuthState;
            var user = authState?.User;

            if (user != null && user.Identity != null && user.Identity.IsAuthenticated)
            {
                _userType = user.GetClaimValue("userType");
                _vendorId = ParseGuid(user.GetClaimValue("vendorPK") ?? user.GetClaimValue("vendorId"));
                _vendorContactId = ParseGuid(user.GetClaimValue("vendorContactId") ?? user.GetClaimValue("vendorContact"));
                _employeeId = ParseGuid(user.GetClaimValue("empPK") ?? user.GetClaimValue("EmployeeId"));
                _userRole = user.GetClaimValue("role");
            }
            else
            {
                _userType = await LocalStorage.GetItemAsync<string>("userType");
                _vendorId = ParseGuid(await LocalStorage.GetItemAsync<string>("vendorPK"));
                _vendorContactId = ParseGuid(await LocalStorage.GetItemAsync<string>("vendorContactId"));
                _employeeId = ParseGuid(await LocalStorage.GetItemAsync<string>("empPK"));
                _userRole = await LocalStorage.GetItemAsync<string>("userRole");
            }
        }

        private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var g) ? g : (Guid?)null;
    }
}