using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OpenKO.Web.Data.Models.Web;

[Table("tblNews")]
public class TblNews
{
    [Key]
    public int Id { get; set; }
    public string Title { get; set; } = null!;
    public string Content { get; set; } = null!;
    public bool Status { get; set; }
    public int TypeId { get; set; }
    public int cUserId { get; set; }
    public DateTime cDate { get; set; }
    public int? eUserId { get; set; }
    public DateTime? eDate { get; set; }
}
