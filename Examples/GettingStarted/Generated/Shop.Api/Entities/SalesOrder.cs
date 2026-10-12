using System.Diagnostics.CodeAnalysis;
using System.ComponentModel.DataAnnotations.Schema;

namespace Shop.Api.Entities;

[Table("sales_order")]
public class SalesOrder : BaseEntity
{
    [SetsRequiredMembers]
    public SalesOrder() { }

    #region Omitted
    [Key]
    [Display(Order = -1, AutoGenerateField = true)]
    [Column("sales_order_id")]
    public required int SalesOrderId { get; set; }

    [ForeignKey(nameof(Customer))]
    [Display(Order = -1, AutoGenerateField = false)]
    [Column("customer_id")]
    public required int CustomerId { get; set; }

    [ForeignKey(nameof(Product))]
    [Display(Order = -1, AutoGenerateField = false)]
    [Column("product_id")]
    public required int ProductId { get; set; }
    #endregion Omitted

    // entities
    public Customer? Customer { get; set; }
    public Product? Product { get; set; }

    [Display(Name = "Order Date", Description = "Order Date")]
    [DisplayFormat(DataFormatString = "{0:MM/dd/yyyy}")]
    [DataType(DataType.Date)]
    [Column("order_date")]
    public required DateTime OrderDate { get; set; }

    [Display(Name = "Quantity", Description = "Quantity")]
    [Column("quantity")]
    public required int Quantity { get; set; }
}
