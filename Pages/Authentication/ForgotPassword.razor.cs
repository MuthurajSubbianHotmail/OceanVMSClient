using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;

namespace OceanVMSClient.Pages.Authentication
{
    public partial class ForgotPassword
    {
        private ForgotModel _model = new();
        private bool isSubmitting;
        private string? StatusMessage;
        private bool IsSuccess;

        [Inject] private HttpClient Http { get; set; } = default!;
        [Inject] private NavigationManager NavigationManager { get; set; } = default!;

        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        private async Task HandleSubmit()
        {
            isSubmitting = true;
            StatusMessage = null;
            IsSuccess = false;

            try
            {
                var payload = JsonSerializer.Serialize(new { email = _model.Email }, _jsonOptions);
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");

                // Adjust the endpoint if your API uses a different route
                var response = await Http.PostAsync("authentication/forgot-password", content);

                var responseText = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    IsSuccess = true;
                    StatusMessage = "If that email is registered, password reset instructions have been sent.";
                    // optional: redirect to login after short delay
                    await Task.Delay(1800);
                    NavigationManager.NavigateTo("/login");
                }
                else
                {
                    // try to surface API message, otherwise generic
                    StatusMessage = !string.IsNullOrWhiteSpace(responseText) ? responseText : "Unable to request password reset. Please try again.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "An error occurred. Please try again later.";
            }
            finally
            {
                isSubmitting = false;
            }
        }

        private class ForgotModel
        {
            [Required(ErrorMessage = "Email is required")]
            [EmailAddress(ErrorMessage = "Invalid email address")]
            public string? Email { get; set; }
        }
    }
}