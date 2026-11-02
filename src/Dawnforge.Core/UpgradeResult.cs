namespace Dawnforge.Core;

public enum UpgradeStatus
{
    Changed,
    Unchanged,
    AlreadyDawntrail,
    Failed,
}

public sealed record UpgradeResult(UpgradeStatus Status, string? Message = null)
{
    public bool Succeeded => Status is UpgradeStatus.Changed or UpgradeStatus.Unchanged;
}
