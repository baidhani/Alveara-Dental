namespace Alveara.Api.Controllers;

// ALV-N030 slice 2: the product no longer has sign-up or the first-admin bootstrap route, so these request shapes left the API. The existing tests still post
// them (and now get 404) until slice 5 moves every test to the TestHost's provisioning route; this file is deleted there.
public record RegisterRequest(string Username, string Password);
public record BootstrapAdminRequest(string Username, string Password, string Secret);
