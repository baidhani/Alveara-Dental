using Alveara.Api.Architecture.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Alveara.Api.TestSupport;

/// <summary>The fixture request (brief section 8, "The route"; unchanged by R07). <c>Kind</c> is <c>Staff</c> (default) or <c>DeploymentAdministrator</c>.</summary>
public record CreateTestAccountRequest(string? Username, string? Password, string? Role, string? Kind = null, bool WithStaffProfile = false, bool MustChangePassword = false);

/// <summary>
/// ALV-N030 (brief 17): <c>POST /api/test-support/accounts</c> creates an enabled account directly, so a browser or chain fixture that needs an Administrator
/// (or any role) does not need the removed sign-up or bootstrap routes. Anonymous by design: it exists only in ChainHost under the <c>Test</c> environment, with
/// <c>TestSupport:Enabled=true</c> and a loopback-only listener (<see cref="TestSupportRegistration"/>). <c>internal</c> so MVC never discovers it by itself.
/// </summary>
[ApiController]
[Route("api/test-support/accounts")]
internal sealed class TestAccountsController(TestAccountService accounts) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTestAccountRequest? request, CancellationToken ct)
    {
        try
        {
            var created = await accounts.CreateAsync(request, ct);
            return StatusCode(StatusCodes.Status201Created, created);
        }
        catch (TestAccountRejectedException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code });
        }
    }
}
