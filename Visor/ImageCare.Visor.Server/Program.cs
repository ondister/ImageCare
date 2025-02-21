using BlazorPanzoom;

using ImageCare.Visor.Server;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

if (IpHelper.TryGetLocalIp(out var addresses))
{
	foreach (var ipAddress in addresses)
	{
		builder.WebHost.ConfigureKestrel(serverOptions =>
		{
			serverOptions.Listen(ipAddress, 55150);
		});
	}
}


builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddBlazorPanzoomServices();
builder.WebHost.UseStaticWebAssets();

var app = builder.Build();

app.UseStaticFiles();

app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();