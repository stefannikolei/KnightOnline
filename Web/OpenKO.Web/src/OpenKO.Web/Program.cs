using Microsoft.EntityFrameworkCore;
using OpenKO.Web.Data;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AccountDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("AccountDb"))
);
builder.Services.AddDbContext<WebDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("WebDb"))
);

WebApplication app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.Run();
