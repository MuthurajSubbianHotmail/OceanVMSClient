using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using MudBlazor;
using OceanVMSClient.HttpRepoInterface.VendorRegistration;

namespace OceanVMSClient.Components
{
    public partial class OrgRegisterDocsUploader
    {
        [Parameter]
        public bool IsReadOnly { get; set; } = true!;

        // unique id per component instance to avoid collisions
        private readonly string _inputId = $"orgreg_{Guid.NewGuid():N}";

        public string ImgUrl { get; set; } = string.Empty;

        // Parent receives uploaded URL here
        [Parameter]
        public EventCallback<string?> OnChange { get; set; }

        // Parent passes document type (e.g. "GSTIN", "PAN", ...)
        [Parameter]
        public string DocType { get; set; } = string.Empty;

        [Inject]
        public IVendorRegistrationRepository Repository { get; set; } = default!;

        private string UploadError { get; set; } = string.Empty;
        private string SelectedFileName { get; set; } = string.Empty;
        private const long MaxFileBytes = 5 * 1024 * 1024; // 5 MB
        [Inject]
        public ISnackbar Snackbar { get; set; } = default!;

        // single-file handler - uses this.DocType provided by parent
        private async Task UploadOrgRegDocImage(InputFileChangeEventArgs e)
        {
            UploadError = string.Empty;
            SelectedFileName = string.Empty;

            var file = e.File; // single-file only
            if (file == null)
            {
                UploadError = "No file selected.";
                return;
            }

            if (file.Size > MaxFileBytes)
            {
                UploadError = "File size must not exceed 5 MB.";
                return;
            }

            SelectedFileName = file.Name;

            try
            {
                using var stream = file.OpenReadStream(MaxFileBytes);
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                ms.Position = 0;

                using var content = new MultipartFormDataContent();
                var streamContent = new StreamContent(ms);
                streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);

                // form field name "file" expected by server
                content.Add(streamContent, "file", file.Name);

                // Use DocType provided by parent when calling repository
                var url = await Repository.UploadOrgRegDocImage(DocType, content);

                ImgUrl = url;
                await OnChange.InvokeAsync(ImgUrl);
                Snackbar.Add($"{DocType} document uploaded successfully.", Severity.Success);
            }
            catch (Exception ex)
            {
                UploadError = $"Upload error: {ex.Message}";
            }
        }
    }
}
