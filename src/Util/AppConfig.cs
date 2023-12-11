public class AppConfig {
	public string URL_API { get; set; } = string.Empty;
	public string URL_MEDIA { get; set; } = string.Empty;
	public string URL_MAIN_XML { get; set; } = string.Empty;
	public bool USE_PROXY { get; set; } = false;
	public bool USE_CACHE_ON_PROXY { get; set; } = false;
	public bool USE_LOGIN { get; set; } = false;
	public bool VERBOSE { get; set; } = false;
	public int MAX_CONCURRENT_DOWNLOADS { get; set; } = -1;
}
