namespace Alveara.Api.Architecture.Clinical;

/// <summary>The values a clinician types for one set of vital signs (metric units; everything optional, but at least one value). See <see cref="EncounterNoteRules.ValidateVitals"/>.</summary>
public sealed record VitalsInput(
    DateTimeOffset? MeasuredAtUtc, int? SystolicMmHg, int? DiastolicMmHg, int? PulseBpm, int? RespirationsPerMinute, decimal? TemperatureC,
    int? OxygenSaturationPercent, decimal? WeightKg, decimal? HeightCm, string? Note);

/// <summary>One section of a template as the administrator edits it.</summary>
public sealed record TemplateSectionInput(string? Section, bool Required, string? StarterText);

/// <summary>Limits and validation for notes, vitals and templates; one place so the services, the API and the tests agree.</summary>
public static class EncounterNoteRules
{
    public const int NoteMax = 8000;
    public const int StarterMax = 2000;
    public const int TemplateNameMax = 120;
    public const int TemplateDescriptionMax = 500;
    public const int VitalsNoteMax = 200;

    public static string RequireNoteSection(string? section)
    {
        if (section is null || !NoteSections.All.Contains(section))
            throw new ClinicalException("validation_failed", "That note section does not exist.", 400,
                new Dictionary<string, string> { ["section"] = "Choose " + string.Join(", ", NoteSections.All) + "." });
        return section;
    }

    /// <summary>What an addendum can amend: one of the four documentation sections, a note section, the vital signs, or nothing in particular (null).</summary>
    public static string? CleanAmendedSection(string? section)
    {
        section = EncounterRules.Clean(section);
        if (section is null) return null;
        if (EncounterEntryKinds.All.Contains(section) || NoteSections.All.Contains(section) || section == AmendableVitals) return section;
        throw new ClinicalException("validation_failed", "Some fields need attention.", 400, new Dictionary<string, string> { ["section"] = "Choose what the addendum amends, or leave it general." });
    }

    public const string AmendableVitals = "Vitals";

    public static string CleanNoteBody(string? body)
    {
        var b = (body ?? "").TrimEnd();
        if (b.Length > NoteMax) throw new ClinicalException("validation_failed", "Some fields need attention.", 400, new Dictionary<string, string> { ["body"] = $"Keep the note to {NoteMax} characters or fewer." });
        return b;
    }

    /// <summary>Validates one set of vitals: at least one value, blood pressure as a pair with diastolic below systolic, and every value within a plausible range (a typo guard, not a diagnosis). Throws a 400 naming each field.</summary>
    public static VitalsInput ValidateVitals(VitalsInput v, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string>();
        void Range(string field, decimal? value, decimal min, decimal max, string unit)
        {
            if (value is { } x && (x < min || x > max)) errors[field] = $"Enter a value between {min} and {max} {unit}.";
        }
        void OneDecimal(string field, decimal? value)
        {
            if (value is { } x && decimal.Round(x, 1) != x) errors[field] = "Use at most one decimal place.";
        }

        var any = v.SystolicMmHg is not null || v.DiastolicMmHg is not null || v.PulseBpm is not null || v.RespirationsPerMinute is not null || v.TemperatureC is not null
                  || v.OxygenSaturationPercent is not null || v.WeightKg is not null || v.HeightCm is not null;
        if (!any) errors["vitals"] = "Enter at least one measurement.";
        if ((v.SystolicMmHg is null) != (v.DiastolicMmHg is null)) errors[v.SystolicMmHg is null ? "systolicMmHg" : "diastolicMmHg"] = "Blood pressure needs both the systolic and the diastolic value.";
        Range("systolicMmHg", v.SystolicMmHg, 40, 300, "mmHg");
        Range("diastolicMmHg", v.DiastolicMmHg, 20, 200, "mmHg");
        if (v.SystolicMmHg is { } s && v.DiastolicMmHg is { } d && d >= s && !errors.ContainsKey("diastolicMmHg")) errors["diastolicMmHg"] = "The diastolic value must be lower than the systolic value.";
        Range("pulseBpm", v.PulseBpm, 20, 300, "beats per minute");
        Range("respirationsPerMinute", v.RespirationsPerMinute, 4, 80, "breaths per minute");
        Range("temperatureC", v.TemperatureC, 30, 45, "degrees C");
        OneDecimal("temperatureC", v.TemperatureC);
        Range("oxygenSaturationPercent", v.OxygenSaturationPercent, 50, 100, "percent");
        Range("weightKg", v.WeightKg, 0.5m, 500, "kg");
        OneDecimal("weightKg", v.WeightKg);
        Range("heightCm", v.HeightCm, 20, 260, "cm");
        OneDecimal("heightCm", v.HeightCm);
        var note = EncounterRules.Clean(v.Note);
        if (note is { Length: > VitalsNoteMax }) errors["note"] = $"Keep the note to {VitalsNoteMax} characters or fewer.";
        if (v.MeasuredAtUtc is { } at && at > now.AddMinutes(5)) errors["measuredAtUtc"] = "Vital signs cannot be dated in the future.";
        if (errors.Count > 0) throw new ClinicalException("validation_failed", "Some fields need attention.", 400, errors);
        return v with { Note = note };
    }

    public static (string Name, string? Description, IReadOnlyList<TemplateSectionInput> Sections) ValidateTemplate(string? name, string? description, IReadOnlyList<TemplateSectionInput>? sections)
    {
        var errors = new Dictionary<string, string>();
        var n = EncounterRules.Clean(name);
        var d = EncounterRules.Clean(description);
        if (n is null) errors["name"] = "A name is required.";
        else if (n.Length > TemplateNameMax) errors["name"] = $"Keep the name to {TemplateNameMax} characters or fewer.";
        if (d is { Length: > TemplateDescriptionMax }) errors["description"] = $"Keep the description to {TemplateDescriptionMax} characters or fewer.";
        var clean = new List<TemplateSectionInput>();
        if (sections is null || sections.Count == 0) errors["sections"] = "Choose at least one note section.";
        else
        {
            foreach (var s in sections)
            {
                var starter = EncounterRules.Clean(s.StarterText);
                if (s.Section is null || !NoteSections.All.Contains(s.Section)) errors["sections"] = "A section in the template does not exist.";
                else if (clean.Any(c => c.Section == s.Section)) errors["sections"] = "A section can appear in a template only once.";
                else if (starter is { Length: > StarterMax }) errors["sections"] = $"Keep the starter text to {StarterMax} characters or fewer.";
                else clean.Add(new TemplateSectionInput(s.Section, s.Required, starter));
            }
        }
        if (errors.Count > 0) throw new ClinicalException("validation_failed", "Some fields need attention.", 400, errors);
        return (n!, d, clean.OrderBy(c => NoteSections.All.ToList().IndexOf(c.Section!)).ToList());
    }
}
