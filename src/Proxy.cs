using System.Net.Http;
using Microsoft.Extensions.Options;

public class Proxy
{
	private readonly IOptions<AppConfig> config;
	private SemaphoreSlim semaphore;
	private string mainXmlFile;
	
	public Proxy(RequestDelegate next, IOptions<AppConfig> config) {
		this.config = config;
		this.mainXmlFile = Path.GetFileName(config.Value.URL_MAIN_XML);
		if (config.Value.MAX_CONCURRENT_DOWNLOADS > 0)
			this.semaphore = new SemaphoreSlim(config.Value.MAX_CONCURRENT_DOWNLOADS);
		else
			this.semaphore = null;
	}
	
	public async Task Invoke(HttpContext context) {
		if (context.Request.Path.ToString().EndsWith("/" + mainXmlFile)) {
			context.Response.StatusCode = 200;
			await context.Response.SendFileAsync(Path.GetFullPath(mainXmlFile));
			return;
		}
		
		bool ApiCall;
		PathString targetPath;
		if (context.Request.Path.StartsWithSegments("/apiproxy", out targetPath)) {
			ApiCall = true;
		} else if (context.Request.Path.StartsWithSegments("/sproxy.com", out targetPath)) {
			ApiCall = false;
		} else {
			context.Response.StatusCode = 418;
			return;
		}
		
		if (!ApiCall && config.Value.USE_CACHE_ON_PROXY) {
			if (File.Exists("cache" + targetPath)) {
				context.Response.StatusCode = 200;
				await context.Response.SendFileAsync(Path.GetFullPath("cache" + targetPath));
				return;
			}
		}
		
		HttpClient client = new HttpClient();
		
		try {
			if (semaphore != null)
				semaphore.Wait();
			
			HttpResponseMessage response;
			if (!ApiCall) {
				if (config.Value.VERBOSE)
					Console.WriteLine(string.Format("Start download (get): {0}", targetPath));
				response = await client.GetAsync(config.Value.URL_MEDIA + targetPath);
			} else {
				if (config.Value.VERBOSE)
					Console.WriteLine(string.Format("Start download (post): {0}", targetPath));
				var content = new StreamContent(context.Request.Body);
				foreach (var header in context.Request.Headers) {
					content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToString());
				}
				response = await client.PostAsync(config.Value.URL_API + targetPath, content);
			}
			
			context.Response.StatusCode = (int)response.StatusCode;
			Byte[] data = await response.Content.ReadAsByteArrayAsync();
			// NOTE: loop with 8192 buffer size write + flush operations (instead of simple `context.Response.Body.WriteAsync(data)`)
			//       to avoid `System.Net.Sockets.SocketException (10040): Unknown error (0x2738)` on some systems
			for (int i=0, len=8192; i<data.Length; i+=len) {
				if (i+len > data.Length)
					len = data.Length - i;
				await context.Response.Body.WriteAsync(data, i, len);
				await context.Response.Body.FlushAsync();
			}
			if (!ApiCall && context.Response.StatusCode == 200 && config.Value.USE_CACHE_ON_PROXY) {
				if (!Directory.Exists(Path.GetDirectoryName("cache" + targetPath))) {
					Directory.CreateDirectory(Path.GetDirectoryName("cache" + targetPath));
				}
				using (var stream = File.Open(Path.GetFullPath("cache" + targetPath), FileMode.Create)) {
					using (var writer = new BinaryWriter(stream)) {
						writer.Write(data);
					}
				}
			}
			
			if (config.Value.VERBOSE)
				Console.WriteLine(string.Format("End download: {0}", targetPath));
		} finally {
			if (semaphore != null)
				semaphore.Release();
		}
	}
}
