using System.Diagnostics.CodeAnalysis;
using System.ComponentModel.DataAnnotations.Schema;

namespace Shop.Api.Entities;

[Table("customer")]
public class Customer : BaseEntity
{
    [SetsRequiredMembers]
    public Customer() { }

    #region Omitted
    [Key]
    [Display(Order = -1, AutoGenerateField = true)]
    [Column("customer_id")]
    public required int CustomerId { get; set; }
    #endregion Omitted

    [Display(Name = "Name", Description = "Name")]
    [StringLength(50)]
    [Column("name")]
    public required string Name { get; set; } = "";

    [Display(Order = -1, Name = "Email", Description = "Email")]
    [StringLength(100)]
    [DataType(DataType.MultilineText)]
    [Column("email")]
    public string? Email { get; set; }

    [Display(Name = "Credit Limit", Description = "Credit Limit")]
    [DisplayFormat(ApplyFormatInEditMode = true, DataFormatString = "#0.00")]
    [Precision(10, 2)]
    [Column("credit_limit")]
    public decimal? CreditLimit { get; set; }

    [Display(Name = "Status", Description = "Status")]
    [StringLength(10)]
    [Column("status")]
    public required string Status { get; set; } = "";

    [Display(Name = "Taxable", Description = "Taxable")]
    [Column("is_taxable")]
    public required bool IsTaxable { get; set; } = false;

    public override string? ToString() => Name;
}
