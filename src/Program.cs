using dragonrescue.Api;
using dragonrescue.Util;
using dragonrescue.Schema;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<AppConfig>(builder.Configuration.GetSection("AppConfig"));

Config.URL_USER_API = builder.Configuration.GetSection("AppConfig").GetValue<string>("URL_API");

Task? proxyRun = null;
if (builder.Configuration.GetSection("AppConfig").GetValue<bool>("USE_PROXY")) {
    var proxyApp = builder.Build();
    proxyApp.UseMiddleware<Proxy>();
    proxyRun = proxyApp.RunAsync();
}

Thread.Sleep(1000);
System.Console.WriteLine("");
    
string apiToken = "";
if (builder.Configuration.GetSection("AppConfig").GetValue<bool>("USE_LOGIN")) {
    apiToken = await LoginApi.GetApiToken();
}

try {
    var clientApp = System.Diagnostics.Process.Start("DOMain.exe", builder.Configuration.GetSection("AppConfig").GetValue<string>("URL_MAIN_XML") + " " + apiToken);
    clientApp.WaitForExit();
} catch (System.ComponentModel.Win32Exception) {
    System.Console.WriteLine("Can't run DOMain.exe ...");
    if (proxyRun != null)
        proxyRun.Wait();
}
