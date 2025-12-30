using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor;
using MudBlazor.Services;
using OceanVMSClient;
using OceanVMSClient.AuthProviders;
using OceanVMSClient.Extensions;
using OceanVMSClient.HttpRepo.Authentication;
using Toolbelt.Blazor.Extensions.DependencyInjection;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

//Extension Methods
builder.Services.ConfigureApiEndPointAddress();
builder.Services.ConfigureHttpRepositories();
builder.Services.AddHttpClientInterceptor();

//Authentication Services
builder.Services.AddBlazoredLocalStorage();
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthenticationStateProvider, AuthStateProvider>();
builder.Services.AddScoped<RefreshTokenService>();
builder.Services.AddScoped<HttpInterceptorService>();

//MudBlazor Services
builder.Services.AddMudServices(config =>
    {
        config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.TopRight;
        config.SnackbarConfiguration.SnackbarVariant = Variant.Filled;
        config.SnackbarConfiguration.ShowCloseIcon = true;
        config.SnackbarConfiguration.MaxDisplayedSnackbars = 1;
        config.SnackbarConfiguration.PreventDuplicates = true;
        config.SnackbarConfiguration.VisibleStateDuration = 1000;
        config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomLeft;
    }
    );
//builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

await builder.Build().RunAsync();
