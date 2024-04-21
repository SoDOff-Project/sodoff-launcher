using dragonrescue.Api;
using dragonrescue.Util;
using dragonrescue.Schema;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<AppConfig>(builder.Configuration.GetSection("AppConfig"));

Config.URL_USER_API = builder.Configuration.GetSection("AppConfig").GetValue<string>("URL_API");

string apikey = builder.Configuration.GetSection("AppConfig").GetValue<string>("APIKEY");
if (!String.IsNullOrEmpty(apikey))
    Config.APIKEY = apikey;

Task? proxyRun = null;
if (builder.Configuration.GetSection("AppConfig").GetValue<bool>("USE_PROXY")) {
    /* client doesn't like this ...
    int MAX_CONCURRENT_DOWNLOADS = builder.Configuration.GetSection("AppConfig").GetValue<int>("MAX_CONCURRENT_DOWNLOADS");
    if (MAX_CONCURRENT_DOWNLOADS > 0) {
        builder.WebHost.ConfigureKestrel(serverOptions => {
            serverOptions.Limits.MaxConcurrentConnections = MAX_CONCURRENT_DOWNLOADS;
        });
    }
    */
    var proxyApp = builder.Build();
    proxyApp.UseMiddleware<Proxy>();
    proxyRun = proxyApp.RunAsync();
}

Thread.Sleep(1000);
System.Console.WriteLine("");
    
string apiToken = "";
if (builder.Configuration.GetSection("AppConfig").GetValue<bool>("USE_LOGIN")) {
    apiToken = await LoginApi.GetApiToken(builder.Configuration.GetSection("AppConfig").GetValue<bool>("NEED_CHILD_LOGIN"));
}

string program = builder.Configuration.GetSection("AppConfig").GetValue<string>("PROGRAM_TO_START");
if (program != string.Empty) {
    string arguments = apiToken;
    if (builder.Configuration.GetSection("AppConfig").GetValue<bool>("USE_LEGACY_ARGS")) {
        arguments = apiToken;
    } else {
        arguments = builder.Configuration.GetSection("AppConfig").GetValue<string>("URL_MAIN_XML") + " " + apiToken;
    }
    try {
        var clientApp = System.Diagnostics.Process.Start(program, arguments);
        clientApp.WaitForExit();
        Environment.Exit(clientApp.ExitCode);
    } catch (System.ComponentModel.Win32Exception) {
        System.Console.WriteLine($"Can't run {program} ...");
    }
}
if (proxyRun != null)
        proxyRun.Wait();
