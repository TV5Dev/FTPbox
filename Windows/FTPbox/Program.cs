﻿/* License
 * This file is part of FTPbox - Copyright (C) 2012 ftpbox.org
 * FTPbox is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published 
 * by the Free Software Foundation, either version 3 of the License, or (at your option) any later version. This program is distributed 
 * in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. 
 * See the GNU General Public License for more details. You should have received a copy of the GNU General Public License along with this program. 
 * If not, see <http://www.gnu.org/licenses/>.
 */
/* Program.cs
 * The main form of the application (options form)
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using FTPbox.Forms;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.IO.Pipes;
using System.Threading;
using Microsoft.Win32;
using FTPboxLib;

namespace FTPbox
{
    static class Program
    {
        public static AccountController Account;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            Settings.Load();
            Account = new AccountController();
            Account = Settings.DefaultProfile;

            // Allocate console
            if (args.Length > 0 && args.Contains("-console"))
                aConsole.Allocate();

            Settings.IsDebugMode = args.Contains("-debug");
            Settings.IsNoMenusMode = args.Contains("-nomenus");

            Log.Init(Common.DebugLogPath, l.Debug | l.Info | l.Warning | l.Error | l.Client, true, Settings.IsDebugMode);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (!DLLsExist)
            {
                MessageBox.Show("The required DLL files to run this program are missing. Please make sure all the needed files are in the installation folder and then run the application. If you cannot find these files, just reinstall FTPbox.", "FTPbox - Missing Resources", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Process.GetCurrentProcess().Kill();
            }
            else
            {
                if (CheckArgs(args))
                {
                    KillUnecessaryDLLs();
                    CheckForPreviousInstances();
                    Application.Run(new fMain());
                }
            }
        }

        #region Check file dependencies

        /// <summary>
        /// returns true if all the required .dll files exist in the startup folder
        /// </summary>
        private static bool DLLsExist
        {
            get
            {
                string[] dlls = { "FTPboxLib.dll", "FluentFTP.dll", "Renci.SshNet.dll", 
                                    "Ionic.Zip.Reduced.dll", "Newtonsoft.Json.dll" };

                return dlls.All(s => File.Exists(Path.Combine(Application.StartupPath, s)));
            }
        }

        /// <summary>
        /// Remove any leftover DLLs and files from previous versions of FTPbox
        /// </summary>
        private static void KillUnecessaryDLLs()
        {
            string[] all = { "Starksoft.Net.Ftp.dll", "Starksoft.Net.Proxy.dll", "DiffieHellman.dll", "Org.Mentalis.Security.dll", "Tamir.SharpSSH.dll", "appinfo.ini", "updater.exe" };

            foreach (var s in all)
            {
                if (File.Exists(Path.Combine(Application.StartupPath, s)))
                    try
                    {
                        File.Delete(Path.Combine(Application.StartupPath, s));
                    }
                    catch (Exception ex)
                    {
                        Log.Write(l.Error, ex.Message);
                    }
            }
        }

        #endregion

        /// <summary>
        /// Any file paths in the arguement list?
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        private static bool CheckArgs(IEnumerable<string> args)
        {
            string param = null;
            var files = new List<string>();
            
            foreach (var s in args)
            {
                if (File.Exists(s) || Directory.Exists(s))
                    files.Add(s);
                else if (s.Equals("move") || s.Equals("copy") || s.Equals("open") || s.Equals("sync"))
                    param = s;
            }

            if (files.Count > 0 && param != null)
            {
                RunClient(files.ToArray(), param);
                return false;
            }
            
            return true;
        }

        #region Named-Pipe Client

        /// <summary>
        /// Connect to our named-pipe server, send arguements and close current process
        /// </summary>
        /// <param name="args"></param>
        /// <param name="param"></param>
        private static void RunClient(string[] args, string param)
        {
            if (!IsServerRunning)
            {
                MessageBox.Show("FTPbox must be running to use the context menus!", "FTPbox", MessageBoxButtons.OK, MessageBoxIcon.Information);
                RemoveFTPboxMenu();
                Process.GetCurrentProcess().Kill();
            }
            
            var pipeClient = new NamedPipeClientStream(".", "FTPbox Server", PipeDirection.InOut, PipeOptions.None, System.Security.Principal.TokenImpersonationLevel.Impersonation);

            Log.Write(l.Client, "Connecting client...");
            pipeClient.Connect();

            var ss = new StreamString(pipeClient);
            if (ss.ReadString() == "ftpbox")
            {
                var p = CombineParameters(args, param);
                ss.WriteString(p);
                Log.Write(l.Client, ss.ReadString());
            }
            else
            {
                Log.Write(l.Client, "Server couldnt be verified.");
            }
            pipeClient.Close();
            Thread.Sleep(4000);

            Process.GetCurrentProcess().Kill();
        }

        private static string CombineParameters(IEnumerable<string> args, string param)
        {
            var r = param + "\"";

            foreach (var s in args)
                r += string.Format("{0}\"", s);

            r = r.Substring(0, r.Length - 1);

            return r;
        }

        private static bool IsServerRunning
        {
            get
            {
                var processes = Process.GetProcesses();
                return processes.Any(p => p.ProcessName == "FTPbox" && p.Id != Process.GetCurrentProcess().Id);
            }
        }

        #endregion

        /// <summary>
        /// Remove the FTPbox context menu (delete the registry files). 
        /// </summary>
        private static void RemoveFTPboxMenu()
        {
            var key = Registry.CurrentUser.OpenSubKey("Software\\Classes\\*\\Shell\\", true);
            if (key != null)
            {
                key.DeleteSubKeyTree("FTPbox", false);
                key.Close();
            }

            key = Registry.CurrentUser.OpenSubKey("Software\\Classes\\Directory\\Shell\\", true);
            if (key != null)
            {
                key.DeleteSubKeyTree("FTPbox", false);
                key.Close();
            }
        }

        /// <summary>
        /// Kill if instances of FTPbox are already running
        /// </summary>
        private static void CheckForPreviousInstances()
        {
            try
            {
                var procname = Process.GetCurrentProcess().ProcessName;
                var allprocesses = Process.GetProcessesByName(procname);

                if (allprocesses.Length > 0)
                    foreach (var p in allprocesses)
                        if (p.Id != Process.GetCurrentProcess().Id)
                        {
                            p.WaitForExit(3000);
                            if (!p.HasExited)
                            {
                                MessageBox.Show("Another instance of FTPbox is already running.", "FTPbox",
                                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                                Process.GetCurrentProcess().Kill();
                            }
                        }
            }
            catch { }
        }
    }

    public class StreamString
    {
        private readonly Stream _ioStream;
        private readonly UnicodeEncoding _sEncoding;

        public StreamString(Stream ioStream)
        {
            _ioStream = ioStream;
            _sEncoding = new UnicodeEncoding();
        }

        public string ReadString()
        {
            int len = 0;

            len = _ioStream.ReadByte() * 256;
            len += _ioStream.ReadByte();
            var inBuffer = new byte[len];
            _ioStream.Read(inBuffer, 0, len);

            return _sEncoding.GetString(inBuffer);
        }

        public int WriteString(string outString)
        {
            var outBuffer = _sEncoding.GetBytes(outString);
            var len = outBuffer.Length;

            if (len > ushort.MaxValue)
                len = ushort.MaxValue;            

            _ioStream.WriteByte((byte)(len / 256));
            _ioStream.WriteByte((byte)(len & 255));
            _ioStream.Write(outBuffer, 0, len);
            _ioStream.Flush();

            return outBuffer.Length + 2;
        }
    }

    public class ReadMessageSent
    {
        private readonly string _data;
        private readonly StreamString _ss;

        public ReadMessageSent(StreamString str, string data)
        {
            _data = data;
            _ss = str;
        }

        public void Start()
        {
            _ss.WriteString(_data);
        }
    }
}
