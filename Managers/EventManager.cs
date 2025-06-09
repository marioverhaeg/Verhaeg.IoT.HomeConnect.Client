using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
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
        // Timers to refresh token
        private static System.Timers.Timer tKeepAlive;
        private bool _running;

        // Configuration
        private string haId;
        private string device_name;
        private string uri;
        private HttpClient hc;

        // Event
        public event EventHandler<Dictionary<string, string>> applianceEvent;

        // Task
        private Task get_events;

        public EventManager(string uri, string device_name, string haId) : base("EventManager_" + device_name)
        {
            this.haId = haId;
            this.device_name = device_name;
            this.uri = uri;
            this._running = false;

            tKeepAlive = new System.Timers.Timer();
        }

        protected override void Process()
        {
            Log.Debug("=============== Thread opened ===============");
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // Start reading events
                    _running = true;
                    Log.Debug("Start reading events.");
                    GetEvents();
                    Log.Debug("GetEvents stopped, waiting 5 seconds to restart.");
                    _running = false;
                    System.Threading.Thread.Sleep(5000);
                }
                catch (Exception ex)
                {
                    Log.Error("Exception in GetEvents, retrieval stopped.");
                    Log.Error(ex.ToString());
                    Log.Debug("Restarting event retrieval after 60 seconds pause...");
                    _running = false;
                    System.Threading.Thread.Sleep(60000);
                }
            }
            Log.Debug("Cancellation requested.");
            _running = false;
        }

        private void GetEvents()
        {
            string url = uri + "homeappliances/" + haId + "/events";
            Log.Debug("Trying to retrieve events from " + @url);
            try
            {
                // Wait for authentication to complete
                Log.Information("Waiting for authentication to complete...");
                hc = AuthorizationManager.Instance().GetHttpClient().Result;
                Log.Information("Authentication complete.");

                // Create event
                EventHandler<Dictionary<string,string>> evApplianceEvent = applianceEvent;

                Dictionary<string, string> dMessage = null;
                Log.Debug("Connecting to " + url + " using StreamReader...");
                using (StreamReader streamReader = new StreamReader(hc.GetStreamAsync(url).Result))
                { 
                    Log.Debug("Waiting for end of stream...");
                    while (!streamReader.EndOfStream && !cts.IsCancellationRequested && _running)
                    {
                        Log.Debug("Waiting for message...");
                        string message = streamReader.ReadLineAsync().Result;
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
                        else if (message.Trim() == "")
                        {
                            Log.Debug("Received empty message.");
                        }
                        else
                        {
                            Log.Error("Received non-conformant message: " + message);
                        }

                        // Process message when message is complete
                        if (dMessage.ContainsKey("id") && dMessage.ContainsKey("event") && dMessage.ContainsKey("data"))
                        {
                            // Last line of message
                            Log.Debug("Last line of message detected.");

                            if (dMessage["event"] != "event:KEEP-ALIVE")
                            {
                                // TRIGGER EXTERNAL EVENT!!
                                Log.Information("Received data from device, generating event.");
                                applianceEvent(this, dMessage);
                            }
                            Log.Debug("Clearing dictionary...");
                            dMessage = null;
                        }

                        // Keep alive message, restart timer.
                        Log.Debug("KEEP-ALIVE received, restarting timer...");
                        try
                        {
                            ResetKeepAliveTimer();
                        }
                        catch (Exception ex)
                        {
                            Log.Error("Could not restart timer.");
                            Log.Error(ex.ToString());
                            break;
                        }
                    }
                }

                Log.Debug("EndOfStream passed.");
                Log.Debug("Stopping timer.");
                tKeepAlive.Stop();
            }
            catch (HttpRequestException ex)
            {
                if (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    Log.Error("HttpStatusCode.TooManyRequests.");
                    Log.Debug(ex.Message);
                    foreach (KeyValuePair kvp in ex.Data)
                    {
                        Log.Debug(kvp.ToString());
                    }
                    Log.Debug("Waiting for 3600 seconds.");
                    Thread.Sleep(60 * 1000 * 60);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Exception in EventManager...");
                Log.Error(ex.ToString());
            }
        }

        private void ResetKeepAliveTimer()
        {
            tKeepAlive.Stop();
            tKeepAlive = new System.Timers.Timer(60000);
            tKeepAlive.Elapsed += TKeepAlive_Elapsed;
            tKeepAlive.AutoReset = false;
            tKeepAlive.Start();
            Log.Debug("KEEP-ALIVE Timer (re)started.");
        }

     

        private void TKeepAlive_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            // Restart connection thread
            Log.Error("KEEP-ALIVE timer expired, stopping event retrieval task.");
            Log.Debug("Trying to restart processes...");
            RestartProcess();
        }

        private void RestartProcess()
        {
            cts.Cancel();
            Log.Debug("Cancellation requested = " + cts.IsCancellationRequested.ToString());
            hc.CancelPendingRequests();
            tKeepAlive.Stop();
            Thread.Sleep(5000);
            
            Log.Debug("GetEvents stopped running.");
            Log.Debug("Checking if task is canceled, completed, or faulted.");
            Log.Debug("Task status: " + t.Status.ToString());

            while (t.Status.ToString() != "RanToCompletion" && t.Status.ToString() != "Cancelled" && t.Status.ToString() != "Faulted")
            {
                cts.Cancel();
                Log.Debug("Cancellation requested = " + cts.IsCancellationRequested.ToString());
                if (cts.IsCancellationRequested)
                {
                    ct.ThrowIfCancellationRequested();
                }
                hc.CancelPendingRequests();
                Log.Debug("Waiting 5 seconds for task to be canceled, completed, or faulted.");
                Log.Debug("Task status: " + t.Status.ToString());
               
                Thread.Sleep(5000);
            }

            Log.Information("Restarting Process with new Task.");
            cts = new CancellationTokenSource();
            ct = cts.Token;
            t = Task.Factory.StartNew(() => Process(), ct);
            Log.Debug("=============== Thread closed ===============");
        }
    }
}
