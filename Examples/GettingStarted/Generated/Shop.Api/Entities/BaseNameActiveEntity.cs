namespace Shop.Api.Entities;

/// <summary> The base of a table with a Name and an IsActive column: BaseEntity plus the two columns the repository's duplicate-name rule reads. </summary>
public class BaseNameActiveEntity : BaseEntity
{
    public required string Name { get; set; }

    public required bool IsActive { get; set; } = true;

    public override string? ToString() => Name;
}
