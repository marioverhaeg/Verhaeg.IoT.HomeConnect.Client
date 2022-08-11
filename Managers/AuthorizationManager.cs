using System;
using System.Collections.Generic;
using System.Net.Http;
using Newtonsoft.Json;
using System.Threading.Tasks;

using System.Runtime.CompilerServices;
using System.IO;

namespace Verhaeg.IoT.HomeConnect.Client.Managers
{
    public class AuthorizationManager : Processor.TaskManager
    {
        // SingleTon
        private static AuthorizationManager _instance = null;
        private static readonly object padlock = new object();

        // Authentication data
        public bool token_available;

        // Configuration
        private Configuration.Connection hc_configuration;
        private static readonly HttpClient hcRequest = new HttpClient();
        private static readonly HttpClient hcToken = new HttpClient();
        private static readonly long token_refresh_time = 14400000;

        // Timers to refresh token
        private System.Timers.Timer tToken;

        // API connection
        private HomeConnectClient hcc;

        public static AuthorizationManager Instance()
        {
            lock (padlock)
            {
                if (_instance == null)
                {
                    return null;
                }
                else
                {
                    return (AuthorizationManager)_instance;
                }
            }            
        }


        private AuthorizationManager(string uri, string authentication_uri, string token_uri, string client_id,
            string client_secret, string device_name, string ha_id) : base("AuthorizationManager_" + device_name)
        {
            hc_configuration = new Configuration.Connection(uri, authentication_uri, token_uri, client_id, client_secret,
                device_name, ha_id);
            hcRequest.Timeout = TimeSpan.FromSeconds(10);
        }

        public static void Start(string uri, string authentication_uri, string token_uri, string client_id, 
            string client_secret, string device_name, string ha_id)
        {
            lock (padlock)
            {
                if (_instance == null)
                {
                    _instance = new AuthorizationManager(uri, authentication_uri, token_uri, client_id,
                        client_secret, device_name, ha_id);
                }
            }
        }

        protected async override void Process()
        {
            token_available = false;
            Device_Token dt = GetTokenFromFile();
            if (dt == null || await TokensAreValid(dt) == false)
            {
                Log.Debug("Could not read tokens from file, trying to retrieve tokens from HomeConnect...");
                Device_Authentication da = await GetDeviceAuthentication();
                dt = await GetTokenFromHomeConnect(da);
                Log.Debug("Tokens retrieved from HomeConnect.");
            }

            if (dt != null)
            {          
                if (await TokensAreValid(dt))
                {
                    WriteTokensToFile(dt.access_token, dt.refresh_token);
                    token_available = true;
                    StartTokenRefreshTimer();
                }
                else
                {
                    Log.Error("Something went wrong...");
                }
            }
        }

        private void StartTokenRefreshTimer()
        {
            Log.Debug("Used token refresh time: " + (token_refresh_time / 1000 / 60).ToString() + " minutes.");
            tToken = new System.Timers.Timer(token_refresh_time);
            tToken.Elapsed += T_Elapsed;
            tToken.AutoReset = true;
            tToken.Enabled = true;
            tToken.Start();
        }

        private async Task<bool> TokensAreValid(Device_Token dt)
        {
            CreateHTTPClient(dt.access_token);
            Log.Debug("Trying to validate tokens by retrieving HaId...");
            if (await RetrieveHaId() == "")
            {
                Log.Debug("Could not validate tokens...");
                return false;
            }
            else
            {
                Log.Debug("Tokens validated."); 
                return true;
            }
        }

        private async Task<Device_Authentication> GetDeviceAuthentication()
        {
            // Prepare HTTP POST request
            Log.Debug("Starting device authorization flow, preparing HTTP POST message...");
            var auth_values = new Dictionary<string, string>
            {
                { "client_id", hc_configuration.client_id }
            };
            FormUrlEncodedContent content = new FormUrlEncodedContent(auth_values);

            try
            {
                HttpResponseMessage response = await hcRequest.PostAsync(hc_configuration.authentication_uri, content);
                string str = await response.Content.ReadAsStringAsync();
                Log.Information("Received response: " + str);
                Log.Debug("Trying to deserialize response into object...");
                Device_Authentication da = JsonConvert.DeserializeObject<Device_Authentication>(str);
                Log.Debug("Response deserialized.");
                Task t = new Task(() => GenerateAuthenticationError(da));
                t.Start();
                return da;
            }
            catch (Exception ex)
            {
                Log.Error("Could not connect to configured URI.");
                Log.Debug(ex.ToString());
                return null;
            }
        }

        private void GenerateAuthenticationError(Device_Authentication da)
        {
            Fields.Error er = new Fields.Error("Verhaeg.IoT.Slack.Error:HomeConnectAuthentication", DateTime.UtcNow, "Error",
                        "Waiting for authentication on " + da.verification_uri_complete, "", "Verhaeg.IoT.HomeConnect.Client");
            er.UpdateDigitalTwin();
        }

        private async Task<Device_Token> GetTokenFromHomeConnect(Device_Authentication da)
        {
            // Prepare HTTP POST request
            Log.Debug("Checking if access token is ready, preparing HTTP POST message...");

            var token_values = new Dictionary<string, string>
                {
                    { "grant_type", "device_code" },
                    { "device_code", da.device_code },
                    { "client_id", hc_configuration.client_id },
                    { "client_secret", hc_configuration.client_secret }
                };

            FormUrlEncodedContent content = new FormUrlEncodedContent(token_values);

            // Get token from HTTP
            Device_Token dt = null;
            while (dt == null)
            {
                Log.Information("Trying to retrieve device token from HomeConnect...");
                dt = await GetDeviceToken(content);
                if (dt == null)
                {
                    Log.Error("Could not retrieve access token. Retry in 60 seconds.");
                    await Task.Delay(60000);
                }
            }
            return dt;
        }

        

        private async void T_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            Log.Information("Token about to expire, refresh token.");
            token_available = false;
            var token_values = new Dictionary<string, string>
                {
                    { "grant_type", "refresh_token" },
                    { "refresh_token", RetrieveTextFromFile("Refresh.txt")},
                    { "client_secret", hc_configuration.client_secret }
                };
            FormUrlEncodedContent content = new FormUrlEncodedContent(token_values);
            int i = 0;
            Device_Token dt = null;
            while (dt == null && i < 10)
            {
                dt = await GetDeviceToken(content);
                if (dt == null)
                {
                    Log.Error("Could not retrieve access token. Retry in 60 seconds.");
                    i++;
                    await Task.Delay(60000);
                }
            }

            WriteTokensToFile(dt.access_token, dt.refresh_token);
            CreateHTTPClient(dt.access_token);
            token_available = true;

            if (i == 10)
            {
                Log.Error("Could not refresh token, restarting authentication flow.");
            }
        }

        public Device_Token GetTokenFromFile()
        {
            Log.Information("Trying to retrieve token from file...");
            string access_token = RetrieveTextFromFile("Access.txt");
            string refresh_token = RetrieveTextFromFile("Refresh.txt");

            if (access_token != "" && refresh_token != "")
            {
                // Tokens found
                Device_Token dt = new Device_Token();
                dt.access_token = access_token;
                dt.refresh_token = refresh_token;
                dt.expires_in = 86400;
                dt.id_token = "";
                dt.scope = "CookProcessor-Monitor Dryer-Settings Washer-Control Dryer-Monitor Settings IdentifyAppliance CleaningRobot Washer-Settings CoffeeMaker Washer CookProcessor-Settings Hob-Settings Oven-Monitor Hood-Control WasherDryer-Monitor Oven-Settings CoffeeMaker-Monitor Monitor Hob-Monitor WasherDryer-Control Dishwasher-Control Refrigerator-Control Dishwasher Dryer-Control CleaningRobot-Control WineCooler Freezer-Monitor WasherDryer Refrigerator-Monitor CookProcessor Freezer Freezer-Settings WineCooler-Control WineCooler-Settings Dishwasher-Settings Hood Dryer FridgeFreezer-Monitor CleaningRobot-Settings Refrigerator Refrigerator-Settings Dishwasher-Monitor CoffeeMaker-Settings FridgeFreezer-Settings CleaningRobot-Monitor WineCooler-Monitor Freezer-Control CoffeeMaker-Control Washer-Monitor Hood-Monitor Hood-Settings FridgeFreezer-Control CookProcessor-Control WasherDryer-Settings";
                dt.token_type = "Bearer";
                return dt;
            }
            else
            {
                return null;
            }
        }

        private void CreateHTTPClient(string access_token)
        {
            Log.Debug("Creating HttpClient client...");
            hcRequest.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", access_token);            
            hcc = new HomeConnectClient(hcRequest);
            hcc.BaseUrl = hc_configuration.uri.ToString();
            Log.Information("HttpClient created.");
        }

        public async Task<Device_Token> GetDeviceToken(FormUrlEncodedContent content)
        {
            try
            {
                HttpResponseMessage response = await hcToken.PostAsync(hc_configuration.token_uri, content);
                string str = await response.Content.ReadAsStringAsync();                
                Log.Debug("Trying to deserialize response into object...");
                if (str.Contains("access_token"))
                {
                    Device_Token dt = JsonConvert.DeserializeObject<Device_Token>(str);
                    
                    Log.Debug("Access token: " + dt.access_token);
                    Log.Debug("Refresh token: " + dt.refresh_token); 
                    return dt;
                }
                else if (str.Contains("invalid_grant"))
                {
                    Log.Error("Invalid refresh token.");
                    Log.Debug("Received message: " + str);
                    return null;
                }
                else
                {
                    Log.Error("Could not process HttpResponseMessage.");
                    Log.Debug("Received message: " + str);
                    return null;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Could not connect to configured URI.");
                Log.Debug(ex.ToString());
                return null;
            }
        }

        private void WriteTokensToFile(string access, string refresh)
        {
            Log.Debug("Writing tokens to text file...");
            WriteTextToFile(access, "Access.txt");
            WriteTextToFile(refresh, "Refresh.txt");
        }

        private string RetrieveTextFromFile(string file)
        {
            try
            {
                //Pass the file path and file name to the StreamReader constructor
                StreamReader sr = new StreamReader(file);
                //Read the first line of text
                string line = sr.ReadLine();
                Log.Debug("Read from file: " + line);
                //close the file
                sr.Close();
                return line;
            }
            catch (Exception e)
            {
                Log.Error("Could not read from file " + file);
                Log.Debug(e.Message);
                return "";
            }
        }

        private void WriteTextToFile(string text, string file)
        {
            Log.Debug("Writing " + text + " to file " + file);
            try
            {
                //Pass the filepath and filename to the StreamWriter Constructor
                StreamWriter sw = new StreamWriter(file, false);
                //Write a line of text
                sw.WriteLine(text);
                //Close the file
                sw.Close();
            }
            catch (Exception e)
            {
                Log.Error("Could not write to file " + file);
                Log.Debug(e.Message);
            }
        }

        private async Task<string> RetrieveHaId()
        {
            bool retry_get = true;
            string return_value = "";

            while (retry_get)
            {
                Log.Debug("Trying to retrieve HaId from HomeConnect...");
                try
                {
                    ArrayOfHomeAppliances appliances = await hcc.HomeappliancesGetAsync();
                    foreach (Homeappliances ha in appliances.Data.Homeappliances)
                    {
                        if (ha.Name == hc_configuration.device_name)
                        {
                            Log.Debug("Found device in account: " + ha.Name);
                            return_value = ha.HaId;
                            retry_get = false;
                        }
                        else
                        {
                            Log.Debug("Device in configuration does not match with device connect to HomeConnect " + ha.Name);
                        }
                    }

                    return return_value;
                }
                catch (ApiException ex)
                {
                    if (ex.StatusCode == 429)
                    {
                        Log.Error(ex.Message);
                        string[] h = (string[])ex.Headers["Retry-After"];
                        int retry = int.Parse(h[0]);
                        Log.Debug("Waiting for " + retry.ToString() + " seconds.");
                        System.Threading.Thread.Sleep(retry * 1000);
                    }
                    else
                    {
                        Log.Error("Could not retrieve HaId.");
                        Log.Debug(ex.ToString());
                        if (ex.ToString().ToLower().Contains("unauthorized"))
                        {
                            Fields.Error er = new Fields.Error("Verhaeg.IoT.Error:HomeConnectAuthentication", DateTime.UtcNow, "Exception",
                                "HomeConnect authentication failure", ex.ToString(), "Verhaeg.IoT.HomeConnect.Client");
                            er.UpdateDigitalTwin();
                        }
                        return_value = "";
                        retry_get = false;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Unknown exception.");
                    Log.Debug(ex.ToString());
                }
            }

            return return_value;
        }

        public async Task<HomeConnectClient> GetHomeConnectClient([CallerMemberName] string caller = "")
        {
            while (token_available == false)
            {
                Log.Debug("Token not available, waiting to return HomeConnectClient to " + caller);
                // Wait and block thread
                await Task.Delay(5000);
            }

            Log.Debug("Token available, returning new HomeConnectClient to " + caller);
            return new HomeConnectClient(hcRequest);
        }

        public async Task<HttpClient> GetHttpClient([CallerMemberName] string caller = "")
        {
            while (token_available == false)
            {
                Log.Debug("Token not available, waiting to return HttpClient to " + caller);
                // Wait and block thread
                await Task.Delay(5000);
            }

            Log.Debug("Token available, returning new HttpClient to " + caller);
            return hcRequest;
        }

    }
}
