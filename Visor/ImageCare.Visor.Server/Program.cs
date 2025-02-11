using System.Net;
using System.Runtime.CompilerServices;

using ImageCare.Visor.Server;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

var builder = WebApplication.CreateBuilder(args);

if (IpHelper.TryGetLocalIp(out var ipAddress))
{
    builder.WebHost.ConfigureKestrel(serverOptions =>
    {
        serverOptions.Listen(ipAddress, 55150);
    });
}


builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();