using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Verhaeg.IoT.HomeConnect.Client.Managers
{
    public class EventManager : Processor.TaskManager
    {

        // SingleTon
        private static EventManager _instance = null;
        private static readonly object padlock = new object();

        // Timers to refresh token
        private static System.Timers.Timer tKeepAlive;

        // Configuration
        private string haId;
        private string device_name;
        private string uri;
        private HttpClient hc;

        // Event
        public event EventHandler<Dictionary<string, string>> applianceEvent;

        private EventManager(string uri, string device_name, string haId) : base("EventManager_" + device_name)
        {
            this.haId = haId;
            this.device_name = device_name;
            this.uri = uri;

            cts = new CancellationTokenSource();
            tKeepAlive = new System.Timers.Timer();
        }

        public static EventManager Instance()
        {
            lock (padlock)
            {
                if (_instance == null)
                {
                    return null;
                }
                else
                {
                    return (EventManager)_instance;
                }
            }
        }

        public static void Start(string uri, string device_name, string ha_id)
        {
          
            lock (padlock)
            {
                if (_instance == null)
                {
                    _instance = new EventManager(uri, device_name, ha_id);
                }
            }
        }

        protected async override void Process()
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    Log.Debug("Preparing timer...");
                    // Message, reset keep alive timer
                    ResetTimer();

                    // Start reading events
                    Log.Debug("Start reading events.");
                    await GetEvents();
                }
                catch (Exception ex)
                {
                    Log.Error("Event retrieval stopped.");
                    Log.Debug(ex.ToString());
                }

                Log.Debug("Restarting event retrieval after 60 seconds pause...");
                await Task.Delay(60000, cts.Token);
            }
        }

        private async Task GetEvents()
        {
            string url = uri + "homeappliances//" + haId + "//events";
            Log.Debug("Trying to retrieve events from " + @url);
            try
            {
                // Blocking method to get the HttpClient from the AuthorizationManager.
                hc = await AuthorizationManager.Instance().GetHttpClient();

                // Create event
                EventHandler<Dictionary<string,string>> evApplianceEvent = applianceEvent;

                Dictionary<string, string> dMessage = null;
                using (var streamReader = new StreamReader(await hc.GetStreamAsync(url)))
                {
                    while (!streamReader.EndOfStream)
                    {
                        Log.Debug("Waiting for message...");
                        var message = await streamReader.ReadLineAsync();
                        Log.Debug($"Received message: {message}");

                        if (message.StartsWith("data:"))
                        {
                            dMessage = new Dictionary<string, string>();
                            dMessage.Add("data", message);
                        }
                        else if (message.StartsWith("event:") && dMessage != null)
                        {
                            dMessage.Add("event", message);
                        }
                        else if (message.StartsWith("id:") && dMessage != null)
                        {
                            dMessage.Add("id", message);
                        }
                        else
                        {
                            if (dMessage != null)
                            {
                                if (dMessage["event"] != "event:KEEP-ALIVE")
                                {
                                    // Last line of message
                                    Log.Debug("Last line of message detected, generating event.");

                                    // TRIGGER EXTERNAL EVENT!!
                                    applianceEvent(this, dMessage);
                                }
                                else
                                {
                                    // Keep alive message, restart timer.
                                    Log.Debug("KEEP-ALIVE received, restarting timer...");
                                    try
                                    {
                                        ResetTimer();
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.Error("Could not restart timer.");
                                        Log.Debug(ex.ToString());
                                        break;
                                    }
                                }

                                Log.Debug("Clearing dictionary...");
                                dMessage = null;
                            }
                            else
                            {
                                Log.Error("Strange state detected: dMessage == NULL");
                            }
                        }
                    }

                    Log.Debug("EndOfStream passed.");
                    Log.Debug("Stopping timer.");
                    tKeepAlive.Stop();
                    Log.Debug("Updating HttpClient object.");
                    hc = await AuthorizationManager.Instance().GetHttpClient();
                }

            }
            catch (Exception ex)
            {
                Log.Error("Exception in Worker...");
                Log.Debug(ex.ToString());
            }
        }

        private void ResetTimer()
        {
            tKeepAlive.Stop();
            Log.Debug("Timer: 240 seconds.");
            tKeepAlive = new System.Timers.Timer(240000);
            tKeepAlive.Elapsed += TKeepAlive_Elapsed;
            tKeepAlive.AutoReset = false;
            tKeepAlive.Start();
            Log.Debug("Timer (re)started.");
        }

        private async void TKeepAlive_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            // Restart connection thread
            Log.Debug("Timer expired, stopping event retrieval task.");
            cts.Cancel();
            cts = null;
            cts = new CancellationTokenSource();
            Log.Debug("Waiting 120 seconds before attempting to restart process.");
            await Task.Delay(120000);

            Log.Debug("Restarting ExecuteAsync.");
            Process();
        }
    }
}
