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
}
