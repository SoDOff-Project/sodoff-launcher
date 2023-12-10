using System.Net;
using dragonrescue.Util;
using dragonrescue.Schema;

namespace dragonrescue.Api;

public static class LoginApi {
    public class Data {
        public string username = "";
        public string password = "";
        public string viking = "";
    }
    
    public static async Task<string> LoginParent(HttpClient client, string UserName, string Password) {
        ParentLoginData loginData = new ParentLoginData {
            UserName = UserName,
            Password = Password,
            Locale = "en-US"
        };

        var loginDataString = XmlUtil.SerializeXml(loginData);
        var loginDataStringEncrypted = TripleDES.EncryptUnicode(loginDataString, Config.KEY);

        var formContent = new FormUrlEncodedContent(new[] {
            new KeyValuePair<string, string>("apiKey", Config.APIKEY),
            new KeyValuePair<string, string>("parentLoginData", loginDataStringEncrypted)
        });

        var bodyRaw = await client.PostAndGetReplayOrThrow(Config.URL_USER_API + "/v3/AuthenticationWebService.asmx/LoginParent", formContent);
        var bodyEncrypted = XmlUtil.DeserializeXml<string>(bodyRaw);
        var bodyDecrypted = TripleDES.DecryptUnicode(bodyEncrypted, Config.KEY);
        return bodyDecrypted;
        //return XmlUtil.DeserializeXml<ParentLoginInfo>(bodyDecrypted);
    }

    public static async Task<bool> IsValidApiToken(HttpClient client, string ApiToken) {
        var formContent = new FormUrlEncodedContent(new[] {
            new KeyValuePair<string, string>("apiKey", Config.APIKEY),
            new KeyValuePair<string, string>("apiToken", ApiToken)
        });
        var bodyRaw = await client.PostAndGetReplayOrThrow(Config.URL_USER_API + "/AuthenticationWebService.asmx/IsValidApiToken_V2", formContent);
        var tokenStatus = XmlUtil.DeserializeXml<ApiTokenStatus>(bodyRaw);
        return tokenStatus == ApiTokenStatus.TokenValid;
    }
    
    public static async Task<string> LoginChild(HttpClient client, string apiToken, string childUserId) {
        var childUserIdEncrypted = TripleDES.EncryptUnicode(childUserId, Config.KEY);

        var ticks = DateTime.UtcNow.Ticks.ToString();
        var locale = "en-US";
        var signature = Md5.GetMd5Hash(string.Concat(new string[]
            {
                ticks,
                Config.KEY,
                apiToken,
                childUserIdEncrypted,
                locale
            }));

        var formContent = new FormUrlEncodedContent(new[] {
            new KeyValuePair<string, string>("apiKey", Config.APIKEY),
            new KeyValuePair<string, string>("parentApiToken", apiToken),
            new KeyValuePair<string, string>("ticks", ticks),
            new KeyValuePair<string, string>("signature", signature),
            new KeyValuePair<string, string>("childUserID", childUserIdEncrypted),
            new KeyValuePair<string, string>("locale", locale),
        });

        var bodyRaw = await client.PostAndGetReplayOrThrow(Config.URL_USER_API + "/AuthenticationWebService.asmx/LoginChild", formContent);
        var bodyEncrypted = XmlUtil.DeserializeXml<string>(bodyRaw);
        return TripleDES.DecryptUnicode(bodyEncrypted, Config.KEY);
    }

    public static async Task<string> GetApiToken() {
        HttpClient client = new HttpClient();
        string? apiToken = null;

        try {
            apiToken = System.IO.File.ReadAllText("apiToken.txt").Trim();
            if (!await IsValidApiToken(client, apiToken)) {
                Console.WriteLine("Invalid saved token. Please login.");
                apiToken = null;
            }
        } catch {
            apiToken = null;
        }

        while(apiToken is null) {
            Console.WriteLine("Enter username: ");
            string username = Console.In.ReadLine();
            Console.WriteLine("Enter password: ");
            string password = Console.In.ReadLine();
            
            var loginInfo = XmlUtil.DeserializeXml<ParentLoginInfo>( await LoginParent(client, username, password) );
            
            if (loginInfo.Status != MembershipUserStatus.Success) {
                Console.WriteLine("\nLogin error. Please check username and password.\n");
                continue;
            }
            
            apiToken = loginInfo.ApiToken;
            using (StreamWriter writer = new StreamWriter("apiToken.txt")) {
                writer.WriteLine(apiToken);
            }
        }
        return apiToken;
    }
}
