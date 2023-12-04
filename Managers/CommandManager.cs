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
            // TEST
            //PowerState("012030519692005346", "BSH.Common.EnumType.PowerState.On");
            //StartSelectedProgram("BOSCH-WAXH2K75NL-68A40E4E014E");
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
                Log.Debug("Powering On device for haId " + kvp.Key);
                PowerState(kvp.Key, "BSH.Common.EnumType.PowerState.On");
                Log.Debug("Waiting for 10 seconds before sending start command.");
                System.Threading.Thread.Sleep(10000);
                Log.Debug("Starting selected program for haId: " + kvp.Key);
                StartSelectedProgram(kvp.Key);
            }
            else if (kvp.Value == "")
            {

            }
            else if (kvp.Value == "StopActiveProgram")
            {
                Log.Debug("Stopping active program for haId " + kvp.Key);
                StopActiveProgram(kvp.Key);
            }
        }

        private async Task<bool> ProgramAvailable(string program_name, HomeConnectClient hcc, string haId)
        {
            ArrayOfAvailablePrograms aoap = await hcc.HomeappliancesProgramsAvailableGetAsync(haId, AcceptLanguage2.EnGB);
            bool return_value = false;
            foreach (Programs p in aoap.Data.Programs)
            {
                if (p.Key == program_name)
                {
                    return_value = true;
                    break;
                }
            }
            return return_value;
        }

        private async void PowerState(string haId, string powerstate)
        {
            try
            {
                Log.Debug("Waiting for AuthorizationManager to deliver client...");
                HomeConnectClient hcc = await AuthorizationManager.Instance().GetHomeConnectClient();
                Log.Debug("Client delivered by AuthorizationManager.");

                Log.Debug("Trying to change PowerState to " + powerstate);
                PutSetting ps = new PutSetting();
                ps.Data.Key = "BSH.Common.Setting.PowerState";
                ps.Data.Value = powerstate;
                //ArrayOfSettings aos =  await hcc.HomeappliancesSettingsGetAsync(haId, AcceptLanguage11.EnGB);
                await hcc.HomeappliancesSettingsPutAsync(ps, haId, AcceptLanguage12.EnGB, "BSH.Common.Setting.PowerState");
                Log.Debug("PowerState command send.");

            }
            catch (ApiException ex)
            {
                Log.Error("Could not change PowerState on haId " + haId);
                Log.Error(ex.ToString());
            }
            catch (Exception ex)
            {
                Log.Error("General exception.");
                Log.Error(ex.ToString());
            }
        }
        

        private async void StartSelectedProgram(string haId)
        {
            try
            {
                Log.Debug("Waiting for AuthorizationManager to deliver client...");
                HomeConnectClient hcc = await AuthorizationManager.Instance().GetHomeConnectClient();
                Log.Debug("Client delivered by AuthorizationManager, trying to retrieve selected program...");
                Program p = await hcc.HomeappliancesProgramsSelectedGetAsync(haId, AcceptLanguage7.EnGB);
                Log.Debug("Checking if program can be started through API...");

                if (await ProgramAvailable(p.Data.Key, hcc, haId))
                {
                    Log.Debug("Program can be started through API.");
                    ProgramDefinition pd = await hcc.HomeappliancesProgramsAvailableGetAsync(haId, AcceptLanguage3.EnGB, p.Data.Key);
                    p.Data.Options = SetOptions(pd, p);
                    Log.Debug("Selected program: " + p.Data.Key + ", sending start command...");
                    await hcc.HomeappliancesProgramsActivePutAsync(p, haId, AcceptLanguage4.EnGB);
                    Log.Debug("Start command send.");
                }
                else
                {
                    Log.Information("Cannot start " + p.Data.Name + " on washer.");                    
                }
            }
            catch (ApiException ex)
            {
                Log.Error("Could not start program on haId " + haId);
                Log.Error(ex.ToString());
            }
            catch(Exception ex)
            {
                Log.Error("General exception.");
                Log.Error(ex.ToString());
            }
        }

        private List<Options> SetOptions(ProgramDefinition pd, Program p)
        {
            DebugSelectedOptions(p);
            DebugAvailableOptions(pd);
            List<Options> lOpt = new List<Options>();
            foreach (Options2 o2 in pd.Data.Options)
            {
                Options opt = p.Data.Options.Where(o => o.Key == o2.Key).ToList().FirstOrDefault();
                if (opt != null)
                {
                    lOpt.Add(opt);
                    Log.Debug("Added option " + opt.Key + " with value " + opt.Value + " to program options.");
                }
                else
                {
                    Log.Error("Option not set: " + o2.Key);
                }
            }
            return lOpt;
        }

        private void DebugSelectedOptions(Program p)
        {
            foreach (Options o in p.Data.Options)
            {
                Log.Debug("Selected option: " + o.Key + " value: " + o.Value);
            }
        }

        private void DebugAvailableOptions(ProgramDefinition pd)
        {
            foreach (Options2 o2 in pd.Data.Options)
            {
                Constraints5 c5 = o2.Constraints;
                Log.Debug("Available option to set: " + o2.Key);
            }
        }

        private async void DebugAvailableProgram(HomeConnectClient hcc, string haId)
        {
            ArrayOfAvailablePrograms ps = await hcc.HomeappliancesProgramsAvailableGetAsync(haId, AcceptLanguage2.EnGB);
            foreach (Programs p in ps.Data.Programs)
            {
                Log.Debug("Available program: " + p.Key);
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
                Log.Error(ex.ToString());
            }
        }
    }
}
