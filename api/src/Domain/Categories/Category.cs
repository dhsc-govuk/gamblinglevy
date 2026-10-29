using Domain.Common;
using Domain.Users;

namespace Domain.Categories;

// High-level policy theme (e.g. Education, Treatment). Read-only DHSC-managed reference data; this feature
// seeds it but does not expose management commands (KR/taxonomy admin is a separate epic).
public class Category : IEntity<CategoryId>
{
    public CategoryId Id { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public int SequenceOrder { get; private set; }
    public UserId CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private Category(
        CategoryId id,
        string name,
        string? description,
        bool isActive,
        int sequenceOrder,
        UserId createdBy,
        DateTime createdAt,
        DateTime? updatedAt)
    {
        Id = id;
        Name = name;
        Description = description;
        IsActive = isActive;
        SequenceOrder = sequenceOrder;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public static Category Seed(
        CategoryId id,
        string name,
        int sequenceOrder,
        UserId createdBy,
        DateTime utcNow,
        string? description = null,
        bool isActive = true)
    {
        Guard.NotNull(id, nameof(id));
        Guard.NotNullOrEmptyOrWhiteSpace(name, nameof(name));
        Guard.NotNull(createdBy, nameof(createdBy));

        return new Category(id, name.Trim(), description, isActive, sequenceOrder, createdBy, utcNow, null);
    }

    // The seeder's only edit to an existing category: it puts the catalogue's categories back in catalogue order
    // after it has inserted one among them (BLUM-8749).
    public void SetSequenceOrder(int sequenceOrder, DateTime utcNow)
    {
        Guard.GreaterThanOrEqual(sequenceOrder, 0, nameof(sequenceOrder));

        SequenceOrder = sequenceOrder;
        UpdatedAt = utcNow;
    }
}