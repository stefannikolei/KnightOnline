using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OpenKO.Web.Data.Models.Web;

[Table("tblTicket")]
public class TblTicket
{
    [Key]
    public int Id { get; set; }
    public string Title { get; set; } = null!;

    [Column("tContent")]
    public string Content { get; set; } = null!;
    public int? CategoryId { get; set; }
    public string Sender { get; set; } = null!;
    public int StatusId { get; set; }
    public System.DateTime Date { get; set; }
    public string Replied { get; set; } = null!;
    public DateTime? ReplyDate { get; set; }
    public int? AnswerId { get; set; }
}
