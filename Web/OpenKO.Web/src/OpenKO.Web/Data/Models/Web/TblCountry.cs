using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OpenKO.Web.Data.Models.Web;

[Table("tblCountry")]
public class TblCountry
{
    [Key]
    public int Id { get; set; }
    public string CountryCode { get; set; } = null!;
    public string Country { get; set; } = null!;
}
