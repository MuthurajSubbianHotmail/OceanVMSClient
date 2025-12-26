using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using OceanVMSClient.HttpRepo.Authentication;
using OceanVMSClient.HttpRepo.POModule;
using OceanVMSClient.HttpRepoInterface.Authentication;
using OceanVMSClient.HttpRepoInterface.PoModule;
using OceanVMSClient.HttpRepoInterface.POModule;
using System;
using System.Net.Http;
using Toolbelt.Blazor.Extensions.DependencyInjection;

namespace OceanVMSClient.Extensions
{
    public static class ServiceExtensions
    {
        public static IServiceCollection ConfigureApiEndPointAddress(this IServiceCollection services)
        {
            services.AddHttpClient("OceanAzureAPI", (sp, cl) =>
            {
                cl.BaseAddress = new Uri("https://myoceanapp.azurewebsites.net/api/");
            });

            services.AddScoped(
                sp => sp.GetService<IHttpClientFactory>().CreateClient("OceanAzureAPI"));
            return services;
        }
        public static IServiceCollection HttpClientInterceptor(this IServiceCollection services)
        {
            services.AddScoped(sp => new HttpClient
            {
                BaseAddress = new Uri("https://myoceanapp.azurewebsites.net/api/"),
            }.EnableIntercept(sp));
            return services;
        }
        public static IServiceCollection ConfigureHttpRepositories(this IServiceCollection services)
        {

            //Authentication Repositories
            services.AddScoped<IAuthenticationService, AuthenticationService>();
            //Data Repositories
            services.AddScoped<IPurchaseOrderRepository, PurchaseOrderRepository>();
            services.AddScoped<IInvoiceApproverRepository, InvoiceApproverRepository>();
            return services;
        }

        public static IServiceCollection AddMudBlazorServices(this IServiceCollection services)
        {
            services.AddMudServices(config =>
            {
                config.ResizeOptions = new ResizeOptions {
                
                };
                config.SnackbarConfiguration = new MudBlazor.SnackbarConfiguration
                {
                    PositionClass = Defaults.Classes.Position.BottomRight,
                    PreventDuplicates = false,
                    NewestOnTop = false,
                    ShowCloseIcon = true,
                    VisibleStateDuration = 5000,
                    HideTransitionDuration = 500,
                    ShowTransitionDuration = 500,
                    SnackbarVariant = MudBlazor.Variant.Filled
                };
            });
            return services;
        }

    }
}
