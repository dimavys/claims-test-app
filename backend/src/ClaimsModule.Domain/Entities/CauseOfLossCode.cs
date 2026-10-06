using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Enums;

namespace ClaimsModule.Domain.Entities;

public class CauseOfLossCode : BaseEntity
{
    private CauseOfLossCode() { }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public PerilCategory PerilCategory { get; private set; }
    public bool IsActive { get; private set; }
    public int SortOrder { get; private set; }
}
