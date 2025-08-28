using Microsoft.EntityFrameworkCore;
using OpenKO.Web.Data.Models.Web;

namespace OpenKO.Web.Data;

public class WebDbContext : DbContext
{
    public DbSet<TblCountry> TblCountries { get; set; }
    public DbSet<TblNews> TblNews { get; set; }
    public DbSet<TblSecretQuestion> TblSecretQuestions { get; set; }
    public DbSet<TblTicket> TblTickets { get; set; }

    public WebDbContext(DbContextOptions options)
        : base(options) { }
}
