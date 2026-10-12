using System.Diagnostics.CodeAnalysis;
using System.ComponentModel.DataAnnotations.Schema;

namespace Shop.Api.Entities;

[Table("product")]
public class Product : BaseEntity
{
    [SetsRequiredMembers]
    public Product() { }

    #region Omitted
    [Key]
    [Display(Order = -1, AutoGenerateField = true)]
    [Column("product_id")]
    public required int ProductId { get; set; }
    #endregion Omitted

    [Display(Name = "Name", Description = "Name")]
    [StringLength(80)]
    [Column("name")]
    public required string Name { get; set; } = "";

    [Display(Name = "Price", Description = "Price")]
    [DisplayFormat(ApplyFormatInEditMode = true, DataFormatString = "#0.00")]
    [Precision(10, 2)]
    [Column("price")]
    public required decimal Price { get; set; }

    [Display(Name = "Rating", Description = "Rating")]
    [Column("rating")]
    public int? Rating { get; set; }

    public override string? ToString() => Name;
}
