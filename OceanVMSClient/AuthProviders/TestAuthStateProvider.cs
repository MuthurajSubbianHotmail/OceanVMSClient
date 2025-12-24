using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Runtime.CompilerServices;
using System.Security.Claims;

namespace OceanVMSClient.AuthProviders
{
    public class TestAuthStateProvider : AuthenticationStateProvider
    {

        public async override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            await Task.Delay(1500); // Simulate a delay for async operation
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, "TestUser"),
                new Claim(ClaimTypes.Role, "Administrator2")
            };


            var anonymous = new ClaimsIdentity(claims, "testAuthType");
            return await Task.FromResult(new AuthenticationState(new ClaimsPrincipal(anonymous)));
        }
    }
}
