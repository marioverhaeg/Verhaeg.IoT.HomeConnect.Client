using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Verhaeg.IoT.HomeConnect.Client.Managers
{
    public class CommandManager : Processor.QueueManager
    {
        // SingleTon
        private static CommandManager _instance = null;
        private static readonly object padlock = new object();

        public static CommandManager Instance()
        {
            lock (padlock)
            {
                if (_instance == null)
                {
                    _instance = new CommandManager();
                    return _instance;
                }
                else
                {
                    return (CommandManager)_instance;
                }
            }
        }

        private CommandManager() : base("CommandManager")
        {

        }

        protected override void Dispose()
        {
            throw new NotImplementedException();
        }

        protected override void Process(object obj)
        {
            KeyValuePair<string, string> kvp = (KeyValuePair<string, string>)obj;
            if (kvp.Value == "StartSelectedProgram")
            {
                Log.Debug("Starting selected program for haId: " + kvp.Key);
                StartSelectedProgram(kvp.Key);
            }
            else if (kvp.Value == "StopActiveProgram")
            {
                Log.Debug("Stopping active program for haId " + kvp.Key);
                StopActiveProgram(kvp.Key);
            }
        }

        private async void StartSelectedProgram(string haId)
        {
            try
            {
                HomeConnectClient hcc = await AuthorizationManager.Instance().GetHomeConnectClient();
                Program p = await hcc.HomeappliancesProgramsSelectedGetAsync(haId, AcceptLanguage7.EnGB);
                await hcc.HomeappliancesProgramsActivePutAsync(p, haId, AcceptLanguage4.EnGB);
            }
            catch (Exception ex)
            {
                Log.Error("Could not start program on haId " + haId);
                Log.Debug(ex.ToString());
            }
        }

        private async void StopActiveProgram(string haId)
        {
            try
            {
                HomeConnectClient hcc = await AuthorizationManager.Instance().GetHomeConnectClient();
                await hcc.HomeappliancesProgramsActiveDeleteAsync(haId, AcceptLanguage4.EnGB);
            }
            catch (Exception ex)
            {
                Log.Error("Could not stop program on haId " + haId);
                Log.Debug(ex.ToString());
            }
        }
    }
}
