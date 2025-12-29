using Entities.Models.Setup;
using Entities.Models.VendorReg;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using MudBlazor;
using OceanVMSClient.HttpRepoInterface.VendorRegistration;
using Shared.DTO.POModule;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace OceanVMSClient.Pages.VendorRegistration
{
    public partial class NewVendorRegistration 
    {
        [CascadingParameter]
        private Task<AuthenticationState>? AuthenticationStateTask { get; set; }
        private bool _isEmployee;
        private bool _isReviewer;
        private bool _isApprover;
        private bool _isReadOnly;

        [Inject]
        public required IVendorRegistrationRepository VendorRegistrationFormRepository { get; set; }

        private MudForm? _form;
        bool success;
        string[] errors = { };
        private int _activeStep = 0;
        private bool _completed;

        //Registration Validations
        private bool _IsAadharRequired = false;
        private bool _isPANrequired = false;
        private bool _isTANRrequired = false;
        private bool _isGSTRequired = false;
        private bool _isUDYAMRequired = false;
        private bool _isCINRequired = false;
        private bool _isPFRequired = false;
        private bool _isESIRequired = false;

        [Parameter]
        public Guid VendorRegistrationFormId { get; set; }

        private VendorRegistrationForm _vendorReg = new()
        {
            Id = Guid.NewGuid(),
            ResponderFName = string.Empty,
            ResponderLName = string.Empty,
            ResponderDesignation = string.Empty,
            ResponderMobileNo = string.Empty,
            ResponderEmailId = string.Empty,
            OrganizationName = string.Empty,
            CompanyOwnershipId = new Guid("D1E4C053-49B6-410C-BC78-2D54A9991870"),
            CompanyOwnership = new CompanyOwnership
            {
                Id = new Guid("D1E4C053-49B6-410C-BC78-2D54A9991870"),
                CompanyOwnershipType = "Private Limited"
            },
        };

        [Inject]
        public ICompanyOwnershipRepository CompanyOwnershipRepository { get; set; }

        private List<CompanyOwnership> _companyOwnerships { get; set; } = new List<CompanyOwnership>();

        [Inject]
        public IVendorServiceRepository VendorServiceRepository { get; set; }
        private List<VendorService> _vendorServices { get; set; } = new List<VendorService>();

        private IEnumerable<string> _selectedServices = new HashSet<string> { "" };

        protected override async Task OnInitializedAsync()
        {
            // load lookups first so we can map the model's id to an item
            _companyOwnerships = await CompanyOwnershipRepository.GetAllCompanyOwnershipsAsync();
            _vendorServices = await VendorServiceRepository.GetAllVendorServices();

            if (VendorRegistrationFormId != Guid.Empty)
            {
                _vendorReg = await VendorRegistrationFormRepository.GetVendorRegistrationFormByIdAsync(VendorRegistrationFormId);
            }

            //registration form ownerships
            _companyOwnerships = await CompanyOwnershipRepository.GetAllCompanyOwnershipsAsync();
            if (_companyOwnerships?.Any() == true)
            {
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
            }


            if (!string.IsNullOrWhiteSpace(_vendorReg.VendorType))
            {
                var selected = _vendorReg.VendorType
                    .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Distinct()
                    .ToList();

                // assign only the vendortypes present on the model
                _selectedServices = selected;
            }

            _vendorServices = await VendorServiceRepository.GetAllVendorServices();
        }

        private Task OnCompanyOwnershipChanged(Guid id)
        {
            _vendorReg.CompanyOwnershipId = id;
            // keep the child object in sync (best-effort)
            _vendorReg.CompanyOwnership = _companyOwnerships.FirstOrDefault(c => c.Id == id)
                                             ?? new CompanyOwnership { Id = id, CompanyOwnershipType = string.Empty };
            ApplyCompanyOwnershipRequirements(id);
            StateHasChanged();
            return Task.CompletedTask;
        }

        private void ApplyCompanyOwnershipRequirements(Guid id)
        {
            var sel = _companyOwnerships.FirstOrDefault(c => c.Id == id);
            _isPANrequired = sel?.PAN_Required ?? false;
            _isTANRrequired = sel?.TAN_Required ?? false;
            _isGSTRequired = sel?.GST_Required ?? false;
            _isCINRequired = sel?.CIN_Required ?? false;
            _isUDYAMRequired = sel?.UDYAM_Required ?? false;
            _isPFRequired = sel?.PF_Required ?? false;
            _isESIRequired = sel?.ESI_Required ?? false;
            _IsAadharRequired = sel?.AADHAR_Required ?? false;
        }

        private Task ChangeRequiredFields()
        {
            _IsAadharRequired = _vendorReg.CompanyOwnership?.AADHAR_Required ?? false;
            _isPANrequired = _vendorReg.CompanyOwnership?.PAN_Required ?? false;
            _isTANRrequired = _vendorReg.CompanyOwnership?.TAN_Required ?? false;
            _isGSTRequired = _vendorReg?.CompanyOwnership?.GST_Required ?? false;
            _isUDYAMRequired = _vendorReg?.CompanyOwnership?.UDYAM_Required ?? false;
            _isCINRequired = _vendorReg?.CompanyOwnership?.CIN_Required ?? false;
            _isPFRequired = _vendorReg?.CompanyOwnership?.PF_Required ?? false;
            _isESIRequired = _vendorReg?.CompanyOwnership?.ESI_Required ?? false;
            StateHasChanged();
            return Task.CompletedTask;
        }
        // add inside the partial class GridLayout
        private void OnOperAddSameAsRegChanged()
        {

            if (_vendorReg.OperAddSameAsReg == true)
            {
                // copy registered -> operational
                _vendorReg.OperAddress1 = _vendorReg.RegAddress1;
                _vendorReg.OperAddress2 = _vendorReg.RegAddress2;
                _vendorReg.OperAddress3 = _vendorReg.RegAddress3;
                _vendorReg.OperCity = _vendorReg.RegCity;
                _vendorReg.OperState = _vendorReg.RegState;
                _vendorReg.OperCountry = _vendorReg.RegCountry;
                _vendorReg.OperPIN = _vendorReg.RegPIN;
            }
            else
            {
                // clear operational fields when unchecked
                _vendorReg.OperAddress1 = string.Empty;
                _vendorReg.OperAddress2 = string.Empty;
                _vendorReg.OperAddress3 = string.Empty;
                _vendorReg.OperCity = string.Empty;
                _vendorReg.OperState = string.Empty;
                _vendorReg.OperCountry = string.Empty;
                _vendorReg.OperPIN = string.Empty;
            }

            // notify UI
            StateHasChanged();
            //return Task.CompletedTask;
        }
        private Task OnPANUploaded(string? url)
        {
            _vendorReg.PANCardURL = url ?? string.Empty;
            return Task.CompletedTask;
        }

        private Task OnTANUploaded(string? url)
        {
            _vendorReg.TANCertURL = url ?? string.Empty;
            return Task.CompletedTask;
        }

        private Task OnGSTUploaded(string? url)
        {
            _vendorReg.GSTRegistrationCertURL = url ?? string.Empty;
            return Task.CompletedTask;
        }
        private Task OnAADHARUploaded(string? url)
        {
            _vendorReg.AadharDocURL = url ?? string.Empty;
            return Task.CompletedTask;
        }

        private Task onCINUploaded(string? url)
        {
            _vendorReg.CINCertURL = url ?? string.Empty;
            return Task.CompletedTask;
        }

        private Task OnCancelCheqUploaded(string? url)
        {
            _vendorReg.CancelledChequeURL = url ?? string.Empty;
            return Task.CompletedTask;
        }
        //OnUDYAMUploaded
        private Task OnUDYAMUploaded(string? url)
        {
            _vendorReg.UDYAMRegCertURL = url ?? string.Empty;
            return Task.CompletedTask;
        }

        private Task OnESIUploaded(string? url)
        {
            _vendorReg.ESIRegCertURL = url ?? string.Empty;
            return Task.CompletedTask;
        }

        private Task OnPFUploaded(string? url)
        {
            _vendorReg.PFRegCertURL = url ?? string.Empty;
            return Task.CompletedTask;
        }
    }
}
