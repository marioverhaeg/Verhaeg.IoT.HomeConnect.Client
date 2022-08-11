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
        private static System.Timers.Timer tRestart;
        private bool _running;

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
            //tRestart = new System.Timers.Timer();
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
                    // Start reading events
                    _running = true;
                    //RestartTimer();
                    Log.Debug("Start reading events.");
                    await GetEvents();
                    Log.Debug("GetEvents stopped, waiting 5 seconds to restart.");
                    //tRestart.Stop();
                    _running = false;
                    await Task.Delay(5000);
                }
                catch (Exception ex)
                {
                    Log.Error("Exception in GetEvents, retrieval stopped.");
                    Log.Debug(ex.ToString());
                    Log.Debug("Restarting event retrieval after 60 seconds pause...");
                    _running = false;
                    await Task.Delay(60000);
                }
                tRestart.Stop();
            }
        }

        private async Task GetEvents()
        {
            string url = uri + "homeappliances/" + haId + "/events";
            Log.Debug("Trying to retrieve events from " + @url);
            try
            {
                // Wait for authentication to complete
                Log.Information("Waiting for authentication to complete...");
                hc = await AuthorizationManager.Instance().GetHttpClient();
                Log.Information("Authentication complete.");

                // Create event
                EventHandler<Dictionary<string,string>> evApplianceEvent = applianceEvent;

                Dictionary<string, string> dMessage = null;
                Log.Debug("Connecting to " + url + " using StreamReader...");
                using (var streamReader = new StreamReader(await hc.GetStreamAsync(url)))
                {
                    Log.Debug("Waiting for end of stream...");
                    while (!streamReader.EndOfStream && !cts.IsCancellationRequested && _running)
                    {
                        Log.Debug("Waiting for message...");
                        var message = await streamReader.ReadLineAsync();
                        Log.Debug($"Received message: {message}");

                        if (dMessage == null)
                        {
                            dMessage = new Dictionary<string, string>();
                        }

                        // Consolidate message
                        if (message.StartsWith("data:"))
                        {
                            dMessage.Add("data", message);
                        }
                        else if (message.StartsWith("event:"))
                        {

                            dMessage.Add("event", message);
                        }
                        else if (message.StartsWith("id:"))
                        {
                            dMessage.Add("id", message);
                        }

                        // Process message when message is complete
                        if (dMessage.ContainsKey("id") && dMessage.ContainsKey("event") && dMessage.ContainsKey("data"))
                        {
                            // Last line of message
                            Log.Debug("Last line of message detected.");

                            if (dMessage["event"] != "event:KEEP-ALIVE")
                            {
                                // TRIGGER EXTERNAL EVENT!!
                                Log.Debug("Generating external event.");
                                applianceEvent(this, dMessage);
                            }
                            else
                            {
                                // Keep alive message, restart timer.
                                Log.Debug("KEEP-ALIVE received, restarting timer...");
                                try
                                {
                                    ResetKeepAliveTimer();
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
                    }

                    Log.Debug("EndOfStream passed.");
                    Log.Debug("Stopping timer.");
                    tKeepAlive.Stop();
                }
            }
            catch (HttpRequestException ex)
            {
                if (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    Log.Error("HttpStatusCode.TooManyRequests.");
                    Log.Debug(ex.Message);
                    Log.Debug("Waiting for 3600 seconds.");
                    Thread.Sleep(60 * 1000 * 60);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Exception in EventManager...");
                Log.Debug(ex.ToString());
            }
        }

        private void ResetKeepAliveTimer()
        {
            tKeepAlive.Stop();
            tKeepAlive = new System.Timers.Timer(240000);
            tKeepAlive.Elapsed += TKeepAlive_Elapsed;
            tKeepAlive.AutoReset = false;
            tKeepAlive.Start();
            Log.Debug("KEEP-ALIVE Timer (re)started.");
        }

        //private void RestartTimer()
        //{
        //    tRestart = new System.Timers.Timer(60000 * 90);
        //    tRestart.Elapsed += TRestart_Elapsed;
        //    tRestart.AutoReset = true;
        //    tRestart.Start();
        //    Log.Debug("Restart Timer started with an interval of 90 minutes.");
        //}

        private void TKeepAlive_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            // Restart connection thread
            Log.Debug("KEEP-ALIVE timer expired, stopping event retrieval task.");
            RestartProcess();
            Process();
        }

        private void TRestart_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            // Restart connection thread
            Log.Debug("Restart timer expired, stopping event retrieval task.");
            RestartProcess();
        }

        private void RestartProcess()
        {
            _running = false;
            cts.Cancel();
            hc.CancelPendingRequests();
            cts = null;
            cts = new CancellationTokenSource();

            tKeepAlive.Stop();
            //tRestart.Stop();

            while (_running == true)
            {
                Log.Debug("GetEvents still running, waiting 5 seconds...");
                Thread.Sleep(5000);
            }
        }
    }
}
