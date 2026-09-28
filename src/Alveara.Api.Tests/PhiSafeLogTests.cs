using Alveara.Api.Architecture.Logging;
using Xunit;

namespace Alveara.Api.Tests;

public class PhiSafeLogTests
{
    [Fact]
    public void Correlate_produces_only_an_entity_type_and_id_never_a_free_text_field()
    {
        var id = Guid.NewGuid();
        var token = PhiSafeLog.Correlate("Patient", id);

        Assert.Equal($"Patient:{id:N}", token);
    }

    [Fact]
    public void Known_PHI_field_names_list_is_non_empty_and_documents_the_convention()
    {
        Assert.NotEmpty(PhiSafeLog.KnownPhiFieldNames);
        Assert.Contains("patientname", PhiSafeLog.KnownPhiFieldNames);
        Assert.Contains("diagnosis", PhiSafeLog.KnownPhiFieldNames);
    }
}
