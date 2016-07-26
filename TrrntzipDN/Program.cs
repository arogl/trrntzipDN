/******************************************************
 *     Torrent Zip File C# Version                    *
 *     Contact: gordon@romvault.com                   *
 *                                                    *
 *     This code is published to expand the           *
 *     availability and usage of torrent zip files.   *
 *     Please feel free to use this code or just      *
 *     learn what you need from it to complete your   *
 *     own torrent zip compatible applications.       *
 *                                                    *
 *     Just 2 things I ask, Just give me a little     *
 *     Credit in your code somewhere,                 *
 *     (Or just leave these comments attached)        *
 *     And Also please let me know if you are using   *
 *     and finding this code usefull in your project  *
 *     Chances are I will help you along the way.     *
 *                                                    *
 ******************************************************/

using System;
using System.IO;
using System.Reflection;

namespace TrrntzipDN
{
    class Program
    {
        public static bool NoRecursion=false;
        public static bool ForceReZip=false;
        public static bool CheckOnly=false;
        public static bool VerboseLogging=false;
        private static bool _guiLaunch;

        private static TorrentZip tz;

        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("");
                Console.WriteLine("trrntzip: missing path");
                Console.WriteLine("Usage: trrntzip [OPTIONS] [PATH/ZIP FILES]");
                return;
            }

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg.Length < 2) continue;
                if (arg.Substring(0, 1) != "-") continue;

                switch (arg.Substring(1, 1))
                {
                    case "?":
                        Console.WriteLine("TorrentZip.Net v{0}\n", Assembly.GetExecutingAssembly().GetName().Version);
                        Console.WriteLine("Copyright (C) 2016 GordonJ");
                        Console.WriteLine("Homepage : http://www.romvault.com/trrntzip\n");
                        Console.WriteLine("Usage: trrntzip [OPTIONS] [PATH/ZIP FILE]\n");
                        Console.WriteLine("Options:\n");
                        Console.WriteLine("-? : show this help");
                        Console.WriteLine("-s : prevent sub-directory recursion");
                        Console.WriteLine("-f : force re-zip");
                        Console.WriteLine("-c : Check files only do not repair");
                        Console.WriteLine("-l : verbose logging");
                        Console.WriteLine("-v : show version");
                        Console.WriteLine("-g : pause when finished");
                        return;
                    case "s":
                        NoRecursion = true;
                        break;
                    case "f":
                        ForceReZip = true;
                        break;
                    case "c":
                        CheckOnly = true;
                        break;
                    case "l":
                        VerboseLogging = true;
                        break;
                    case "v":
                        Console.WriteLine("TorrentZip v{0}", Assembly.GetExecutingAssembly().GetName().Version);
                        return;
                    case "g":
                        _guiLaunch = true;
                        break;

                }
            }

            tz = new TorrentZip();
            tz.StatusCallBack = StatusCallBack;
            tz.StatusLogCallBack = StatusLogCallBack;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg.Length < 2) continue;
                if (arg.Substring(0, 1) == "-") continue;

                // first check if arg is a directory
                if (Directory.Exists(arg))
                {
                    ProcessDir(arg);
                    continue;
                }

                // now check if arg is a directory/filename with possible wild cards.
                string dir = Path.GetDirectoryName(arg);
                if (string.IsNullOrEmpty(dir)) dir = Environment.CurrentDirectory;

                string filename = Path.GetFileName(arg);
                
                DirectoryInfo dirInfo = new DirectoryInfo(dir);
                FileInfo[] fileInfo = dirInfo.GetFiles(filename);
                foreach (FileInfo file in fileInfo)
                {
                    string ext = Path.GetExtension(file.FullName);
                    if (!string.IsNullOrEmpty(ext) && (ext.ToLower() ==".zip" || ext.ToLower()==".7z"))
                    {
                        tz.Process(new IO.FileInfo(file.FullName));
                    }
                }
            }

            if (_guiLaunch)
            {
                Console.WriteLine("Complete.");
                Console.ReadLine();
            }
        }

        private static void StatusCallBack(int processID, int percent)
        {
            Console.Write($"{percent,3}%");
        }

        private static void StatusLogCallBack(int processId, string log)
        {
            Console.WriteLine(log);
        }


        private static void ProcessDir(string dirName)
        {
            Console.WriteLine("Checking Dir : " + dirName);

            DirectoryInfo di = new DirectoryInfo(dirName);
            FileInfo[] fi = di.GetFiles();
            for (int i = 0; i < fi.Length; i++)
            {
                string filename = fi[i].FullName;
                string ext = Path.GetExtension(filename);
                if (!string.IsNullOrEmpty(ext) && (ext.ToLower() == ".zip" || ext.ToLower() == ".7z"))
                {
                    tz.Process(new IO.FileInfo(filename));
                }
            }

            if (Program.NoRecursion)
                return;

            string[] directories = Directory.GetDirectories(dirName);
            foreach (string dir in directories)
                ProcessDir(dir);
        }

    }
}
