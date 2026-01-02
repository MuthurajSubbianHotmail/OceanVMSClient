using Blazored.LocalStorage;
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
        [Parameter] public Guid VendorRegistrationFormId { get; set; }

        //Common Services
        [Inject] public ISnackbar Snackbar { get; set; } = default!;
        [Inject] public IDialogService DialogService { get; set; } = default!;
        [Inject] public NavigationManager NavigationManager { get; set; } = default!;
        [Inject] public ILogger<NewVendorRegistration> Logger { get; set; } = default!;

        //Repositories
        [Inject] public required IVendorRegistrationRepository VendorRegistrationFormRepository { get; set; }
        [Inject] public ICompanyOwnershipRepository CompanyOwnershipRepository { get; set; } = default!;
        [Inject] public IVendorServiceRepository VendorServiceRepository { get; set; } = default!;

        // local storage for ClaimsHelper fallback
        [Inject] public ILocalStorageService LocalStorage { get; set; } = default!;

        // Claims / auth state (provided by host)
        [CascadingParameter] public Task<AuthenticationState> AuthState { get; set; } = default!;

        // suppress CS8618 - will be assigned in OnInitializedAsync
        private EditContext _editContext = null!;

        // UI helpers moved from razor to avoid duplicate symbols
        private List<BreadcrumbItem> _items = new()
        {
            new BreadcrumbItem("Home", href: "/", disabled: false),
            new BreadcrumbItem("Registration List", href: "/vendor-registrations", disabled: false),
        };
        private Typo _inputTypo = Typo.caption;
        private Margin _inputMargin = Margin.Dense;
        private Variant _inputVariant = Variant.Text;

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

        //Private Variable Which need to be Edited

        //Private Variables for field requirement checks
        private bool _isAadharRequired;
        private bool _isPANRequired;
        private bool _isTANRequired;
        private bool _isGSTRequired;
        private bool _isUDYAMRequired;
        private bool _isCINRequired;
        private bool _isPFRequired;
        private bool _isESIRequired;
        private bool _IsMSMERegistered;
        private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
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
        private List<CompanyOwnership> _companyOwnerships { get; set; } = new();
        private List<VendorService> _vendorServices { get; set; } = new();

        private IEnumerable<string> _selectedServices { get; set; } = new HashSet<string>();
        private string _selectedCompanyOwnershipType = string.Empty;

        // Fields for Registration Details 1 (PAN/TAN/GST/CIN)
        private readonly List<string> _registrationSection1Fields = new()
        {
            nameof(VendorRegistrationFormDto.PANNo),
            nameof(VendorRegistrationFormDto.TANNo),
            nameof(VendorRegistrationFormDto.GSTNO),
            nameof(VendorRegistrationFormDto.CIN)
        };
        private bool _isRegistrationSection1Valid = true;

        // Fields for Registration Details 2 (Aadhar/UDYAM/PF/ESI)
        private readonly List<string> _registrationSection2Fields = new()
        {
            nameof(VendorRegistrationFormDto.AadharNo),
            nameof(VendorRegistrationFormDto.UDYAMRegNo),
            nameof(VendorRegistrationFormDto.PFNo),
            nameof(VendorRegistrationFormDto.ESIRegNo)
        };
        private bool _isRegistrationSection2Valid = true;

        // Fields for Bank Account section
        private readonly List<string> _bankAccountSectionFields = new()
        {
            nameof(VendorRegistrationFormDto.BankName),
            nameof(VendorRegistrationFormDto.BankBranch),
            nameof(VendorRegistrationFormDto.IFSCCode),
            nameof(VendorRegistrationFormDto.AccountNumber),
            nameof(VendorRegistrationFormDto.AccountName)
        };
        private bool _isBankAccountSectionValid = true;

        // Add this private field to the class
        private EventHandler<FieldChangedEventArgs>? _clearHandler;
        private ValidationMessageStore? _messageStore;

        public RegisterVendor()
        {
            // DO NOT initialize EditContext here. Initializing in ctor causes InvokeAsync/StateHasChanged
            // calls to fail because the RenderHandle isn't assigned yet.
        }

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
        protected override async Task OnInitializedAsync()
        {
            // Initialize EditContext on the renderer lifecycle (not in ctor) so it can register handlers safely.
            InitializeEditContext(_vendorReg);

            await LoadUserContextAsync();

            // Load lookups first (they don't replace the EditContext/model)
            try
            {
                await LoadLookupsAsync();
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "LoadLookupsAsync failed during initialization");
            }

            // If we need to load an existing registration from server, DO NOT replace `_vendorReg`
            // (replacing it causes Blazor event handler id mismatches). Instead copy values into
            // the existing instance and notify the EditContext of changes.
            if (VendorRegistrationFormId != Guid.Empty)
            {
                try
                {
                    var loaded = await VendorRegistrationFormRepository.GetVendorRegistrationFormByIdAsync(VendorRegistrationFormId);
                    if (loaded != null)
                    {
                        CopyModelValues(loaded, _vendorReg);

                        // Notify EditContext that fields changed so components re-evaluate binding/validation
                        foreach (var prop in typeof(VendorRegistrationFormDto).GetProperties().Where(p => p.CanRead))
                        {
                            _editContext?.NotifyFieldChanged(new FieldIdentifier(_vendorReg, prop.Name));
                        }

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

        // Helper: copy public property values from source to target without changing the object reference.
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


        // Section validation support
        private readonly List<string> _organizationSectionFields = new()
            {
                nameof(VendorRegistrationFormDto.OrganizationName),
                nameof(VendorRegistrationFormDto.CompanyOwnershipType),
                nameof(VendorRegistrationFormDto.VendorServices),
                nameof(VendorRegistrationFormDto.CompanyPhoneNo),
                nameof(VendorRegistrationFormDto.CompanyEmailID)
            };
        private bool _isOrganizationSectionValid = true;




        // Simplified organization section validity: require that each field in `_organizationSectionFields`
        // is present and non-empty. If all required fields are filled the section is valid.
        //
        // This replaces the previous, more complex validation logic with a straightforward check.
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

        // Simplified address-section validity: require that each field in `_addresSectionFields`
        // is present and non-empty. This replaces the previous complex validation logic.
        private readonly List<string> _addresSectionFields = new()
        {
            nameof(VendorRegistrationFormDto.RegAddress1),
            nameof(VendorRegistrationFormDto.RegCity),
        };
        private bool _isAddressSectionValid = true;
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

        private readonly List<string> _contactSectionFields = new()
        {
            nameof(VendorRegistrationFormDto.ResponderFName),
            nameof(VendorRegistrationFormDto.ResponderLName),
            nameof(VendorRegistrationFormDto.ResponderDesignation),
            nameof(VendorRegistrationFormDto.ResponderMobileNo),
            nameof(VendorRegistrationFormDto.ResponderEmailId)
        };
        private bool _isContactSectionValid = true;
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
        private void UpdateRegistrationSection1Validity()
        {
            if (_editContext == null || _vendorReg == null)
            {
                _isRegistrationSection1Valid = true;
                return;
            }

            var anyInvalid = false;

            foreach (var propName in _registrationSection1Fields)
            {
                var fieldId = new FieldIdentifier(_vendorReg, propName);
                if (_editContext.GetValidationMessages(fieldId).Any())
                {
                    anyInvalid = true;
                    break;
                }

                var prop = typeof(VendorRegistrationFormDto).GetProperty(propName);
                if (prop != null)
                {
                    var value = prop.GetValue(_vendorReg);
                    var results = new List<ValidationResult>();
                    var context = new ValidationContext(_vendorReg) { MemberName = propName };
                    Validator.TryValidateProperty(value, context, results);
                    if (results.Any())
                    {
                        anyInvalid = true;
                        break;
                    }

                    var isRequired = prop.GetCustomAttributes(typeof(RequiredAttribute), inherit: true).Any()
                                     || propName == nameof(VendorRegistrationFormDto.PANNo)
                                     || propName == nameof(VendorRegistrationFormDto.GSTNO);

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
                    }
                }

                if (string.Equals(propName, nameof(VendorRegistrationFormDto.PANNo), StringComparison.Ordinal) && _isPANRequired)
                {
                    if (string.IsNullOrWhiteSpace(_vendorReg.PANNo))
                    {
                        anyInvalid = true;
                        break;
                    }
                }
                if (string.Equals(propName, nameof(VendorRegistrationFormDto.TANNo), StringComparison.Ordinal) && _isTANRequired)
                {
                    if (string.IsNullOrWhiteSpace(_vendorReg.TANNo))
                    {
                        anyInvalid = true;
                        break;
                    }
                }
                if (string.Equals(propName, nameof(VendorRegistrationFormDto.GSTNO), StringComparison.Ordinal) && _isGSTRequired)
                {
                    if (string.IsNullOrWhiteSpace(_vendorReg.GSTNO))
                    {
                        anyInvalid = true;
                        break;
                    }
                }
                if (string.Equals(propName, nameof(VendorRegistrationFormDto.CIN), StringComparison.Ordinal) && _isCINRequired)
                {
                    if (string.IsNullOrWhiteSpace(_vendorReg.CIN))
                    {
                        anyInvalid = true;
                        break;
                    }
                }
            }

            var newState = !anyInvalid;
            if (newState != _isRegistrationSection1Valid)
            {
                _isRegistrationSection1Valid = newState;
                _ = InvokeAsync(StateHasChanged);
            }
        }
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
                if (string.Equals(propName, nameof(VendorRegistrationFormDto.UDYAMRegNo), StringComparison.Ordinal) && _isUDYAMRequired)
                    isRequired = true;
                if (string.Equals(propName, nameof(VendorRegistrationFormDto.PFNo), StringComparison.Ordinal) && _isPFRequired)
                    isRequired = true;
                if (string.Equals(propName, nameof(VendorRegistrationFormDto.ESIRegNo), StringComparison.Ordinal) && _isESIRequired)
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

                    //if (results.Any())
                    //{
                    //    anyInvalid = true;
                    //    // add current validation messages so EditContext/validation UI shows them
                    //    foreach (var res in results)
                    //    {
                    //        //_messageStore?.Add(fieldId, res.ErrorMessage ?? string.Empty);
                    //    }
                    //    continue;
                    //}

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
                            //_messageStore?.Add(fieldId, "This field is required.");
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
        private async Task OnCompanyOwnershipChanged()
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
            await Task.CompletedTask;
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

        // Add this helper near other validation helpers in the partial class
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

                // For registration section dynamic flags
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
            if(CalledFrom == "Contact")
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
        private async Task HandleSubmit(EditContext editContext)
        {
            var validationResults = new List<ValidationResult>();
            var validationContext = new ValidationContext(_vendorReg, serviceProvider: null, items: null);
            Validator.TryValidateObject(_vendorReg, validationContext, validationResults, validateAllProperties: true);

            if (_isAadharRequired && string.IsNullOrWhiteSpace(_vendorReg.AadharNo))
                validationResults.Add(new ValidationResult("Aadhar is required for the selected ownership.", new[] { nameof(_vendorReg.AadharNo) }));

            if (_isPANRequired && string.IsNullOrWhiteSpace(_vendorReg.PANNo))
                validationResults.Add(new ValidationResult("PAN is required for the selected ownership.", new[] { nameof(_vendorReg.PANNo) }));

            if (_isTANRequired && string.IsNullOrWhiteSpace(_vendorReg.TANNo))
                validationResults.Add(new ValidationResult("TAN No is required for the selected ownership.", new[] { nameof(_vendorReg.TANNo) }));

            if (_isGSTRequired && string.IsNullOrWhiteSpace(_vendorReg.GSTNO))
                validationResults.Add(new ValidationResult("GST No is required for the selected ownership.", new[] { nameof(_vendorReg.GSTNO) }));

            if (_isUDYAMRequired && string.IsNullOrWhiteSpace(_vendorReg.UDYAMRegNo))
                validationResults.Add(new ValidationResult("UDYAM No is required for the selected ownership.", new[] { nameof(_vendorReg.UDYAMRegNo) }));

            if (_isCINRequired && string.IsNullOrWhiteSpace(_vendorReg.CIN))
                validationResults.Add(new ValidationResult("CIN is required for the selected ownership.", new[] { nameof(_vendorReg.CIN) }));

            if (_isPFRequired && string.IsNullOrWhiteSpace(_vendorReg.PFNo))
                validationResults.Add(new ValidationResult("PF No is required for the selected ownership.", new[] { nameof(_vendorReg.PFNo) }));

            if (_isESIRequired && string.IsNullOrWhiteSpace(_vendorReg.ESIRegNo))
                validationResults.Add(new ValidationResult("ESI No is required for the selected ownership.", new[] { nameof(_vendorReg.ESIRegNo) }));

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


        private void OnCancel() => NavigationManager.NavigateTo($"/vendor-registrations/{_vendorReg.Id}");

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