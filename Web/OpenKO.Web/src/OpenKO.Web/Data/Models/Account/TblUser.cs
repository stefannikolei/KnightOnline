using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OpenKO.Web.Data.Models.Account;

[Table("TB_USER")]
public class TblUser
{
    [Key]
    public int Id { get; set; }

    [Column("strAccountID")]
    public string AccountId { get; set; } = null!;

    [Column("strPasswd")]
    public string Password { get; set; } = null!;

    [Column("strSocNo")]
    public string SocNo { get; set; } = null!;

    [Column("strEmail")]
    public string Email { get; set; } = null!;

    [Column("strAuthority")]
    public byte Authority { get; set; }

    [Column("PremiumExpire")]
    public DateTime PremiumExpire { get; set; }

    [Column("sQuestionId")]
    public int QuestionId { get; set; }

    [Column("sQuestionAnswer")]
    public string QuestionAnswer { get; set; } = null!;

    [Column("CountryId")]
    public int CountryId { get; set; }
}
