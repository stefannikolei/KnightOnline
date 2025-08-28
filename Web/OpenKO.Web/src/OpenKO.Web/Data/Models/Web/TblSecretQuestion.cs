using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OpenKO.Web.Data.Models.Web;

[Table("tblSecretQuestion")]
public class TblSecretQuestion
{
    [Key]
    public int Id { get; set; }
    public string Question { get; set; } = null!;
}
