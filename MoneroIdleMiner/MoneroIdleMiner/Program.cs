using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.IO;
using System.Configuration;

namespace MoneroIdleMiner
{
    internal struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    class Program
    {
        [DllImport("User32.dll")]
        public static extern bool LockWorkStation();
        [DllImport("User32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO Dummy);
        [DllImport("Kernel32.dll")]
        private static extern uint GetLastError();

        private static readonly char[] AppSettingsSplitter = { '|' };

        static int minersRunning = 0;

        private class Config
        {
            public int IdleTimeThreshold;
            public int IdleScanFrequency;
            public string[] Programs;
        }

        private static uint GetIdleTime()
        {
            LASTINPUTINFO LastUserAction = new LASTINPUTINFO();
            LastUserAction.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(LastUserAction);
            GetLastInputInfo(ref LastUserAction);
            return ((uint)Environment.TickCount - LastUserAction.dwTime);
        }

        private static long GetTickCount()
        {
            return Environment.TickCount;
        }

        private static long GetLastInputTime()
        {
            LASTINPUTINFO LastUserAction = new LASTINPUTINFO();
            LastUserAction.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(LastUserAction);
            if (!GetLastInputInfo(ref LastUserAction))
            {
                throw new Exception(GetLastError().ToString());
            }

            return LastUserAction.dwTime;
        }

        private static void p_Exited(object sender, EventArgs e)
        {
            minersRunning -= 1;
        }

        private static void ShowErrorAndExit(string error)
        {
            Console.WriteLine(error);
            Console.ReadLine();
            Environment.Exit(0);
        }

        static Config ReadSettingsFile()
        {
            var manager = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            var obj = new Config();

            if(manager.AppSettings.Settings.Count == 0) {
                obj.IdleTimeThreshold = 60000;
                obj.IdleScanFrequency = 1000;
                obj.Programs = new string[1];

                manager.AppSettings.Settings.Add("IdleTimeThreshold", obj.IdleTimeThreshold.ToString());
                manager.AppSettings.Settings.Add("IdleScanFrequency", obj.IdleScanFrequency.ToString());
                manager.AppSettings.Settings.Add("Programs", "");

                manager.Save(ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection("appSettings");

                Console.WriteLine("Created default configuration file at \"" + AppDomain.CurrentDomain.SetupInformation.ConfigurationFile + "\"");

                return obj;
            }

            Console.WriteLine("Reading configuration file: \"" + AppDomain.CurrentDomain.SetupInformation.ConfigurationFile + "\"");

            if (string.IsNullOrEmpty(manager.AppSettings.Settings["IdleTimeThreshold"].Value) ||
                !int.TryParse(manager.AppSettings.Settings["IdleTimeThreshold"].Value, out obj.IdleTimeThreshold)) {
                obj.IdleTimeThreshold = 60000;
                manager.AppSettings.Settings["IdleTimeThreshold"].Value = obj.IdleTimeThreshold.ToString();
            }

            if (string.IsNullOrEmpty(manager.AppSettings.Settings["IdleScanFrequency"].Value) ||
                !int.TryParse(manager.AppSettings.Settings["IdleScanFrequency"].Value, out obj.IdleScanFrequency)) {
                obj.IdleScanFrequency = 1000;
                manager.AppSettings.Settings["IdleScanFrequency"].Value = obj.IdleScanFrequency.ToString();
            }

            if (string.IsNullOrEmpty(manager.AppSettings.Settings["Programs"].Value))
                throw new Exception("Missing \"Programs\" field in configuration file. " +
                    "Please indicate which programs to start when the system is idle and seperate them using the \"" + AppSettingsSplitter + "\" key if there's more than one.");
            else
                obj.Programs = manager.AppSettings.Settings["Programs"].Value.Split(AppSettingsSplitter);

            manager.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("appSettings");

            return obj;
        }

        static void Main(string[] args)
        {
            var settings = ReadSettingsFile();

            var processes = new Process[settings.Programs.Length];
            ProcessStartInfo processStartInfo;

            processStartInfo = new ProcessStartInfo();
            processStartInfo.CreateNoWindow = false;
            processStartInfo.UseShellExecute = false;

            for(var i = 0; i < settings.Programs.Length; i++) {
                processStartInfo.FileName = settings.Programs[i];
                processes[i] = new Process();
                processes[i].StartInfo = processStartInfo;
                processes[i].EnableRaisingEvents = true;

                processes[i].Exited += p_Exited;
                processes[i].OutputDataReceived += new DataReceivedEventHandler
                ( (object sender, DataReceivedEventArgs e) => { Console.WriteLine(e.Data); } );
            }

            Console.WriteLine("Waiting for system idle...");

            while (true)
            {
                Thread.Sleep(settings.IdleScanFrequency);

                if (GetIdleTime() >= settings.IdleTimeThreshold && minersRunning == 0)
                {
                    Console.WriteLine("System is idle. Starting miner" + (settings.Programs.Length > 1 ? "s" : "") + "...");

                    foreach (var p in processes)
                        try { p.Start(); } catch (Exception e)
                            { Console.WriteLine("Error starting process: " + e.ToString()); }
                }

                if (GetIdleTime() < settings.IdleTimeThreshold && minersRunning > 0)
                {
                    Console.WriteLine("System no longer idle. MINER STOPPED.");

                    foreach(var p in processes) {
                        if (p.HasExited)
                            continue;

                        p.Kill();
                    }

                    while (minersRunning != 0)
                        System.Threading.Thread.Sleep(100);
                }

            }

        }
    }
}
