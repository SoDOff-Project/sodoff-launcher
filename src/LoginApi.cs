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

    public static async Task<RegistrationResult> RegisterParent(HttpClient client, string Email, string UserName, string Password) {
        ParentRegistrationData registrationData = new ParentRegistrationData {
            Email = Email,
            ChildList = new ChildRegistrationData[]{
                new ChildRegistrationData {
                    ChildName = UserName
                }
            },
            Password = Password
        };

        var registrationDataString = XmlUtil.SerializeXml(registrationData);
        var registrationDataStringEncrypted = TripleDES.EncryptUnicode(registrationDataString, Config.KEY);

        var formContent = new FormUrlEncodedContent(new[] {
            new KeyValuePair<string, string>("apiKey", Config.APIKEY),
            new KeyValuePair<string, string>("parentRegistrationData", registrationDataStringEncrypted)
        });

        var bodyRaw = await client.PostAndGetReplayOrThrow(Config.URL_USER_API + "/v3/RegistrationWebService.asmx/RegisterParent", formContent);
        var bodyEncrypted = XmlUtil.DeserializeXml<string>(bodyRaw);
        var bodyDecrypted = TripleDES.DecryptUnicode(bodyEncrypted, Config.KEY);
        return XmlUtil.DeserializeXml<RegistrationResult>(bodyDecrypted);
    }

    public static async Task<string> GetApiToken(bool childLogin) {
        const string apiTokenFile = "apiToken.txt";
        HttpClient client = new HttpClient();
        string? apiToken = null;

        try {
            apiToken = System.IO.File.ReadAllText(apiTokenFile).Trim();
            if (!await IsValidApiToken(client, apiToken)) {
                Console.WriteLine("Invalid saved token. Please login.");
                apiToken = null;
            }
        } catch {
            apiToken = null;
        }

        if (apiToken != null) {
            System.Console.WriteLine("");
            while (Console.KeyAvailable)
                Console.ReadKey();
            for (int i=0; i<=50; ++i) {
                if (i%10 == 0) {
                    WriteOnPreviousLine($"Press Enter to continue or X to logout ... {(50-i)/10} s");
                }
                if (Console.KeyAvailable) {
                    ConsoleKey key = Console.ReadKey().Key;
                    if (key == ConsoleKey.Enter) {
                        return apiToken;
                    } else if (key == ConsoleKey.X) {
                        File.Delete(apiTokenFile);
                        apiToken = null;
                        System.Console.WriteLine("Logout successful.");
                        break;
                    }
                }
                Thread.Sleep(100);
            }
        }

        while (true) {
            System.Console.WriteLine($"Press L to login or C to create account");
            ConsoleKey key = Console.ReadKey().Key;
            System.Console.WriteLine("");
            if (key == ConsoleKey.C) {

                // Create account

                while(true) {
                    string username = Input("Enter username: ");
                    string email = Input("Enter e-mail: ");
                    string passwordA, passwordB;
                    while (true) {
                        passwordA = Input("Enter password: ", true);
                        passwordB = Input("Re-Enter password: ", true);

                        if (passwordA == passwordB)
                            break;
                        Console.WriteLine($"Password do not match!");
                    }
                    System.Console.WriteLine($"Do you want create account with username=`{username}` and e-mail=`{email}`?");
                    System.Console.WriteLine($"Press Y or Enter to continue, or any other key to cancel and change data.");
                    key = Console.ReadKey().Key;
                    if (key == ConsoleKey.Y || key == ConsoleKey.Enter) {

                        // Call register API

                        RegistrationResult registerResults = await RegisterParent(client, email, username, passwordA);
                        if (registerResults.Status == MembershipUserStatus.DuplicateEmail) {
                            System.Console.WriteLine($"Duplicated e-mail");
                            continue;
                        }
                        if (registerResults.Status == MembershipUserStatus.DuplicateUserName) {
                            System.Console.WriteLine($"Duplicated username");
                            continue;
                        }

                        System.Console.WriteLine("Registration successful");

                        ParentLoginInfo loginInfo;
                        loginInfo = XmlUtil.DeserializeXml<ParentLoginInfo>( await LoginParent(client, username, passwordA) );
                        if (loginInfo.Status != MembershipUserStatus.Success)
                            loginInfo = XmlUtil.DeserializeXml<ParentLoginInfo>( await LoginParent(client, email, passwordA) );

                        if (loginInfo.Status != MembershipUserStatus.Success) {
                            System.Console.WriteLine($"Login error ({loginInfo.Status}) after successful account created ... this should never happen!");
                            break;
                        }

                        apiToken = loginInfo.ApiToken;

                        if (childLogin) {
                            if (loginInfo.ChildList is null || loginInfo.ChildList.Length < 1) {
                                System.Console.WriteLine($"NEED_CHILD_LOGIN is set to true, but no child account after successful parent account created ... do you use correct apiKey?");
                                break;
                            }
                            apiToken = await LoginChild(client, apiToken, loginInfo.ChildList[0].UserID);
                            if (apiToken is null) {
                                System.Console.WriteLine($"Child login error after successful account created ... do you use correct apiKey?");
                                break;
                            }
                        }

                        System.Console.WriteLine("Login successful");

                        using (StreamWriter writer = new StreamWriter(apiTokenFile)) {
                            writer.WriteLine(apiToken);
                        }
                        return apiToken;
                    } else {
                        break;
                    }
                }

            } else if (key == ConsoleKey.L) {

                // Login

                string username = Input("Enter username: ");
                string password = Input("Enter password: ", true);

                var loginInfo = XmlUtil.DeserializeXml<ParentLoginInfo>( await LoginParent(client, username, password) );

                if (loginInfo.Status != MembershipUserStatus.Success) {
                    Console.WriteLine("\nLogin error. Please check username and password.\n");
                    continue;
                }

                apiToken = loginInfo.ApiToken;

                if (childLogin) {
                    Console.WriteLine("Vikings: ");
                    foreach (var viking in loginInfo.ChildList){
                        Console.WriteLine($" - {viking.UserName}");
                    }
                    Console.WriteLine();
                    string childName = Input("Enter viking name: ");

                    string? childUserId = loginInfo.ChildList.FirstOrDefault(v => v.UserName == childName)?.UserID;
                    if (childUserId is null)
                        continue;
                    apiToken = await LoginChild(client, apiToken, childUserId);
                    if (apiToken is null)
                        continue;
                }

                using (StreamWriter writer = new StreamWriter(apiTokenFile)) {
                    writer.WriteLine(apiToken);
                }
                if (apiToken != null) {
                    System.Console.WriteLine("Login successful");
                    return apiToken;
                }
            }
        }
    }

    private static void WriteOnPreviousLine(string text) {
        int cursory = Console.GetCursorPosition().Item2;
        Console.SetCursorPosition(0, cursory-1);
        Console.WriteLine(text);
    }

    private static string Input(string text, bool clearInput = false) {
        Console.WriteLine(text);
        string input = Console.In.ReadLine();
        if (clearInput)
            WriteOnPreviousLine(new String(' ', input.Length));
        return input;
    }
}
