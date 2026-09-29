namespace Alveara.Api.Architecture.Identity;

/// <summary>
/// STORY-001/REQ-002's defined role set. A practice role, not a permission bitmask — future
/// stories that need finer-grained authorization can layer claims on top of this, but every
/// account has exactly one of these today because the story's roles are job titles, not
/// composable capabilities.
/// </summary>
public enum Role
{
    Dentist,
    Hygienist,
    Assistant,
    FrontDesk,
    Billing,
    OfficeManager,
    Admin,

    // ALV-001-C01: self-service registration must never grant a working role (a public caller
    // could otherwise select Admin). A newly self-registered account holds this placeholder,
    // disabled, until an administrator both enables it and assigns one of the roles above.
    // Appended at the end so it never disturbs the ordinal values already persisted for the
    // roles above (STORY-001's migration backfilled existing rows with ordinal 0 = Dentist).
    Unassigned,
}
