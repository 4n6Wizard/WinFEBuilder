namespace WinFEBuilder.Core.Models;

/// <summary>
/// Operational / forensic validation states. These are intentionally SEPARATE from
/// build success (<see cref="CheckStatus"/>). The application must never automatically
/// claim a device is forensically validated: the higher states are set only by explicit
/// human testing recorded through the Validation page (Milestone 5).
/// </summary>
public enum ValidationStatus
{
    /// <summary>The build batch process reported success and output was validated.</summary>
    BuildSuccessful = 0,

    /// <summary>Expected boot files/structure were found and verified on disk.</summary>
    BootStructureValidated = 1,

    /// <summary>A human confirmed the media booted. Never set automatically.</summary>
    BootTestPassed = 2,

    /// <summary>A human confirmed write-protection behaviour. Never set automatically.</summary>
    WriteProtectionTestPassed = 3,

    /// <summary>An authorized approver signed off. Never set automatically.</summary>
    OrganizationApproved = 4,

    /// <summary>Step has not been performed / recorded.</summary>
    NotTested = 100
}
