using Xunit;
using Alveara.Api.Architecture.Patients.Portability.Package;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-018-C01, the names of folders and files inside a package, without a database: a patient folder is the id alone or the name and the id (the id always stays, so two patients with one name never share a folder);
/// a name is cut down to letters, digits and single hyphens, so nothing a user typed can leave its folder or break an unzip; a document's file name loses any folder part, reserved character, device name and trailing dot
/// but keeps its extension; the same input always gives the same name; and a misspelt naming mode fails instead of falling back.
/// </summary>
public class PackagePathsTests
{
    private static readonly Guid Id = Guid.Parse("3f2a9c10-1111-4222-8333-444455556666");

    [Fact]
    public void The_id_only_folder_is_the_id_and_nothing_about_the_patient()
    {
        Assert.Equal("3f2a9c10-1111-4222-8333-444455556666", PackagePaths.PatientFolder(Id, "Eli", "Moss", FolderNaming.IdOnly));
    }

    [Fact]
    public void The_name_and_id_folder_is_last_name_first_name_then_the_id()
    {
        Assert.Equal("Moss-Eli_3f2a9c10-1111-4222-8333-444455556666", PackagePaths.PatientFolder(Id, "Eli", "Moss", FolderNaming.NameAndId));
    }

    [Theory]
    [InlineData("Eli", "O'Brien", "OBrien-Eli")]                                  // the quote is dropped, not turned into a separator
    [InlineData("Mary Ann", "van der Berg", "van-der-Berg-Mary-Ann")]            // spaces become single hyphens
    [InlineData("José", "Müller", "Müller-José")]                                  // accents are kept
    [InlineData("  Eli  ", "..\\..\\Moss/", "Moss-Eli")]                         // separators and dots at the edges never survive
    [InlineData("E:li*", "Mo?ss<>|\"", "Moss-Eli")]                              // characters Windows rejects are dropped
    public void A_name_is_reduced_to_letters_digits_and_single_hyphens(string first, string last, string expectedName)
    {
        Assert.Equal($"{expectedName}_{Id:D}", PackagePaths.PatientFolder(Id, first, last, FolderNaming.NameAndId));
    }

    [Fact]
    public void A_patient_without_a_usable_name_gets_the_id_alone()
    {
        Assert.Equal(Id.ToString("D"), PackagePaths.PatientFolder(Id, "", "", FolderNaming.NameAndId));
        Assert.Equal(Id.ToString("D"), PackagePaths.PatientFolder(Id, "'", "\"/", FolderNaming.NameAndId));
        Assert.Equal(Id.ToString("D"), PackagePaths.PatientFolder(Id, null, null, FolderNaming.NameAndId));
    }

    [Fact]
    public void A_long_name_is_capped_and_two_patients_with_the_same_name_still_get_different_folders()
    {
        var other = Guid.Parse("aaaaaaaa-1111-4222-8333-444455556666");

        var a = PackagePaths.PatientFolder(Id, new string('x', 200), new string('y', 200), FolderNaming.NameAndId);
        var b = PackagePaths.PatientFolder(other, new string('x', 200), new string('y', 200), FolderNaming.NameAndId);

        Assert.True(a.Length <= 40 + 1 + 36);
        Assert.NotEqual(a, b);
        Assert.EndsWith(Id.ToString("D"), a);
    }

    [Fact]
    public void The_same_input_always_gives_the_same_folder_and_whatever_is_typed_the_folder_is_one_safe_path_segment()
    {
        foreach (var hostile in new[] { "..", "../..", "a/b", "a\\b", "C:\\x", "CON", "nul.txt", "x\u0000y", "x\ny", ". .", "\u202Eevil" })
        {
            var folder = PackagePaths.PatientFolder(Id, hostile, hostile, FolderNaming.NameAndId);

            Assert.Equal(folder, PackagePaths.PatientFolder(Id, hostile, hostile, FolderNaming.NameAndId));
            Assert.DoesNotContain("/", folder);
            Assert.DoesNotContain("\\", folder);
            Assert.DoesNotContain(":", folder);
            Assert.DoesNotContain("..", folder);
            Assert.All(folder, c => Assert.False(char.IsControl(c)));
            Assert.EndsWith(Id.ToString("D"), folder);
        }
    }

    [Fact]
    public void An_unknown_naming_mode_is_refused_not_replaced_by_a_default()
    {
        Assert.Throws<ArgumentException>(() => PackagePaths.PatientFolder(Id, "Eli", "Moss", "Names"));
        Assert.Throws<ArgumentException>(() => FolderNaming.Require(null));
        Assert.True(FolderNaming.IsValid(FolderNaming.IdOnly) && FolderNaming.IsValid(FolderNaming.NameAndId) && !FolderNaming.IsValid("idonly"));
    }

    [Theory]
    [InlineData("bitewing.png", "bitewing.png")]
    [InlineData("..\\..\\evil.exe", "evil.exe")]                                   // a folder part is dropped
    [InlineData("C:\\Users\\x\\scan.pdf", "scan.pdf")]
    [InlineData("a/b/c.txt", "c.txt")]
    [InlineData("scan?.pdf", "scan.pdf")]                                          // reserved characters are dropped
    [InlineData("report.pdf.", "report.pdf")]                                      // Windows would drop the trailing dot
    [InlineData("  spaced name .png  ", "spaced name .png")]
    [InlineData(".hidden", "hidden")]
    [InlineData("CON.png", "_CON.png")]                                            // a device name cannot be a file name on Windows
    [InlineData("lpt1", "_lpt1")]
    [InlineData("", "file")]
    [InlineData("...", "file")]
    [InlineData("müller röntgen.png", "müller röntgen.png")]
    public void A_document_file_name_is_cleaned_but_stays_recognisable(string original, string expected)
    {
        Assert.Equal(expected, PackagePaths.DocumentFileName(original));
    }

    [Fact]
    public void A_null_file_name_becomes_file_and_a_long_one_is_cut_but_keeps_its_extension()
    {
        var cut = PackagePaths.DocumentFileName(new string('n', 300) + ".pdf");

        Assert.Equal("file", PackagePaths.DocumentFileName(null));
        Assert.True(cut.Length <= 100);
        Assert.EndsWith(".pdf", cut);
    }

    [Fact]
    public void A_document_path_groups_by_patient_and_is_unique_by_document()
    {
        var doc = Guid.Parse("11111111-2222-4333-8444-555555555555");

        Assert.Equal("documents/F1/11111111-2222-4333-8444-555555555555/pano.png", DataPackageFormat.DocumentPath("F1", doc, "pano.png"));
    }
}
