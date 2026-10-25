using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.Documents;
using Alveara.Api.Architecture.ExternalCases;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Permissions;
using Alveara.Api.Controllers;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-009-C02: the second permission a case's document actions need. Reading a case's linked documents, linking one and removing a link also need <see cref="Permission.ViewDocuments"/> beside the case permission,
/// because they are the documents themselves. Every role that holds the case permissions also holds the document permission by default, so only a stored policy that takes it away exposes the check: this test gives the
/// controller a permission seam that does exactly that and shows the three actions refuse (403 naming the missing permission) and store nothing, and that they work when it is held.
/// </summary>
public class ExternalCaseControllerPermissionTests : TreatmentPlanTestBase
{
    private sealed class Seam(params Permission[] withheld) : IEffectivePermissions
    {
        public Task<PermissionOutcome> CheckAsync(Role role, Permission permission, CancellationToken ct = default) => Task.FromResult(withheld.Contains(permission) ? PermissionOutcome.Denied : PermissionOutcome.Allowed);
        public Task<PermissionOutcome> CheckAnyAsync(Role role, IReadOnlyCollection<Permission> permissions, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasAsync(Role role, Permission permission, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlySet<Permission>> PermissionsForAsync(Role role, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Role, IReadOnlySet<Permission>>> MatrixAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PolicyRead> SnapshotAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private async Task<(ExternalCaseView Case, PatientDocument Doc)> ArrangeAsync()
    {
        var party = await WithDb(db => new ExternalPartyService(db, PatientTestSupport.Clock).CreateAsync(S.Actor, new CreatePartyRequest(ExternalPartyKinds.ReferralProvider, $"Party {Guid.NewGuid():N}", null), default));
        var c = (await WithDb(db => new ExternalCaseService(db, PatientTestSupport.Clock).CreateReferralAsync(S.Actor, Ann, new CreateReferralRequest(party.Id, "Second opinion", null, null, null), Guid.NewGuid().ToString("N"), default))).Case;
        var doc = new PatientDocument
        {
            Id = Guid.NewGuid(), PatientId = Ann, CategoryId = DocumentCategorySeeds.Radiograph, DocumentDate = new DateOnly(2026, 10, 1), Description = "Panoramic film.", OriginalFileName = "pano.png", DetectedType = "png", ContentType = "image/png",
            SizeBytes = 2048, Sha256 = new string('a', 64), BlobId = Guid.NewGuid(), ImportedByUserId = S.Actor, ImportedAtUtc = DateTimeOffset.UtcNow, IdempotencyKey = $"doc-{Guid.NewGuid():N}", RequestHash = new string('a', 64),
        };
        await using var db = Fixture.CreateContext();
        db.Add(doc);
        await db.SaveChangesAsync();
        return (c, doc);
    }

    private async Task<T> WithController<T>(IEffectivePermissions seam, Func<ExternalCasesController, Task<T>> action) => await WithDb(db =>
    {
        var controller = new ExternalCasesController(new ExternalCaseService(db, PatientTestSupport.Clock), new ExternalPartyService(db, PatientTestSupport.Clock), seam)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, S.Actor.ToString()), new Claim(ClaimTypes.Role, "Dentist")], "test")) } },
        };
        return action(controller);
    });

    private async Task<int> LinksAsync() { await using var db = Fixture.CreateContext(); return await db.ExternalCaseDocuments.CountAsync(); }

    private static void AssertRefused(IActionResult result)
    {
        var refused = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);
        Assert.Contains("ViewDocuments", System.Text.Json.JsonSerializer.Serialize(refused.Value));
    }

    [Fact]
    public async Task Without_the_document_permission_the_three_document_actions_refuse_and_store_nothing()
    {
        var (c, doc) = await ArrangeAsync();
        var withheld = new Seam(Permission.ViewDocuments);
        AssertRefused(await WithController(withheld, ctl => ctl.Documents("referrals", c.Id, default)));
        AssertRefused(await WithController(withheld, ctl => ctl.LinkDocument("referrals", c.Id, doc.Id, default)));
        AssertRefused(await WithController(withheld, ctl => ctl.UnlinkDocument("referrals", c.Id, doc.Id, default)));
        Assert.Equal(0, await LinksAsync());
    }

    [Fact]
    public async Task With_the_document_permission_the_same_actions_link_list_and_unlink()
    {
        var (c, doc) = await ArrangeAsync();
        var held = new Seam();
        Assert.IsType<OkObjectResult>(await WithController(held, ctl => ctl.LinkDocument("referrals", c.Id, doc.Id, default)));
        Assert.Equal(1, await LinksAsync());
        var listed = Assert.IsType<OkObjectResult>(await WithController(held, ctl => ctl.Documents("referrals", c.Id, default)));
        Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<CaseDocumentView>>(listed.Value));
        Assert.IsType<OkObjectResult>(await WithController(held, ctl => ctl.UnlinkDocument("referrals", c.Id, doc.Id, default)));
        Assert.Equal(0, await LinksAsync());
    }
}
