using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CGTOOL.Web.Data.Governance;

public class Transaction
{
    public int Id { get; set; }

    [Required, MaxLength(20)]
    public string ReferenceCode { get; set; } = string.Empty;

    public int MemberId { get; set; }
    public Member? Member { get; set; }

    [Required, MaxLength(40)]
    public string Instrument { get; set; } = string.Empty;

    public TransactionSide Side { get; set; }

    public int Quantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    public DateOnly TradeDate { get; set; }

    public TransactionCategory Category { get; set; }

    public TransactionStatus Status { get; set; }

    [MaxLength(1000)]
    public string? ComplianceNote { get; set; }

    [MaxLength(120)]
    public string? PolicyReference { get; set; }

    [MaxLength(120)]
    public string? Reviewer { get; set; }

    public DateOnly? FiledDate { get; set; }
}
