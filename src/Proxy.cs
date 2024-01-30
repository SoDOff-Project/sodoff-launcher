using System.Net.Http;
using System.Net;
using System.IO;
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
		
		var handler = new HttpClientHandler();
		handler.AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip;
		HttpClient client = new HttpClient(handler);
		client.Timeout = TimeSpan.FromMinutes(config.Value.HTTP_CLIENT_TIMEOUT);
		
		try {
			if (semaphore != null)
				semaphore.Wait();
			
			if (config.Value.VERBOSE)
				Console.WriteLine($"Start download (post={ApiCall}): {targetPath}");
			
			if (ApiCall) {
				var content = new StreamContent(context.Request.Body);
				foreach (var header in context.Request.Headers) {
					content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToString());
				}
				using (var response = await client.PostAsync(config.Value.URL_API + targetPath, content)) {
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
				}
			} else {
				string filePath = Path.GetFullPath("cache" + targetPath);
				string filePathTmp = filePath + Path.GetRandomFileName().Substring(0, 8);
				using (var response = await client.GetAsync(
					config.Value.URL_MEDIA + targetPath,
					HttpCompletionOption.ResponseHeadersRead
				)) {
					if (response.IsSuccessStatusCode) {
						if (response.Content.Headers.ContentType?.MediaType != null)
							context.Response.Headers["Content-Type"] =  response.Content.Headers.ContentType?.MediaType;
						
						if (response.Content.Headers.ContentLength != null)
							context.Response.Headers["Content-Length"] = response.Content.Headers.ContentLength.ToString();
						
						using (var inputStream = await response.Content.ReadAsStreamAsync()) {
							if (config.Value.USE_CACHE_ON_PROXY) {
								string dirPath = Path.GetDirectoryName(filePath);
								if (!Directory.Exists(dirPath)) {
									Directory.CreateDirectory(dirPath);
								}
								
								// copy data retrieved from upstream server to file and to response for game client
								using (var fileStream = File.Open(filePathTmp, FileMode.Create)) {
									// read response from upstream server
									byte[] buffer = new byte[4096];
									int bytesRead;
									while ((bytesRead = await inputStream.ReadAsync(buffer, 0, buffer.Length)) > 0) {
										// write to temporary file
										var task1 = fileStream.WriteAsync(buffer, 0, bytesRead);
										// send to client
										var task2 = context.Response.Body.WriteAsync(buffer, 0, bytesRead);
										// wait for finish both writes
										await Task.WhenAll(task1, task2);
									}
								}
								
								// after successfully write data to temporary file, rename it to proper asset filename
								File.Move(filePathTmp, filePath);
							} else {
								await inputStream.CopyToAsync(context.Response.Body);
							}
						}
					} else {
						context.Response.StatusCode = 404;
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
