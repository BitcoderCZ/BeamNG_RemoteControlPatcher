using ConsoleUI;
using ConsoleUI.Elements;
using ConsoleUI.Text;
using MathUtils.Vectors;
using Microsoft.Win32;
using Serilog;
using System.Runtime.InteropServices;

namespace BeamNG.RemoteControlPatcher
{
    internal class Program
    {
        const string backupFolder = "Backup";
        static readonly Vector2I menuPosition = new Vector2I(3, 1);

        static void Main(string[] args)
        {
            var log = new LoggerConfiguration()
                .WriteTo.Console()
                .WriteTo.File("logs/debug.txt", rollingInterval: RollingInterval.Day, rollOnFileSizeLimit: true, fileSizeLimitBytes: 8338607, outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .MinimumLevel.Debug()
                .CreateLogger();
            Log.Logger = log;

            UIList list = new UIList(
                new UIText(new ColoredString("Patcher", ColoredString.DefaultColor))
                {
                    TextOptions = new TruncatedTextFormatter(true, true)
                },
                new UIButton(new ColoredString("Patch", ColoredString.DefaultColor), patchUI),
                new UIButton(new ColoredString("Restore", ColoredString.DefaultColor), restoreUI),
                new UIButton(new ColoredString("Close", ColoredString.DefaultColor))
                {
                    OnClickFunc = () => UIManager.ContinueOptions.CloseUI,
                }
            );

            list.SetColor(ConsoleColor.White, ConsoleColor.Black);

            UIManager ui = new UIManager(list, menuPosition)
            {
                CircularNavigation = true,
            };

            ui.Open();
        }

        private static void patchUI()
        {
            Console.Clear();

            string? gameFolder = locateGameFolder();
            if (string.IsNullOrEmpty(gameFolder)) return;

            Console.Clear();

            bool supportUltra = true;

            UIList list = new UIList(
                new UIText(new ColoredString("Patcher/PatchOptions", ColoredString.DefaultColor))
                {
                    TextOptions = new TruncatedTextFormatter(true, true)
                },
                new UIBool(new ColoredString("Support non-default controls", ColoredString.DefaultColor))
                {
                    Value = true,
                    OnInvoke = currentVal =>
                    {
                        supportUltra = !currentVal;
                        return !currentVal;
                    },
                },
                new UIButton(new ColoredString("Patch", ColoredString.DefaultColor))
                {
                    OnClickFunc = () => UIManager.ContinueOptions.CloseUI,
                }
            );

            list.SetColor(ConsoleColor.White, ConsoleColor.Black);

            UIManager ui = new UIManager(list, menuPosition)
            {
                CircularNavigation = true,
            };

            ui.Open();

            Console.Clear();

            try
            {
                patch(gameFolder, supportUltra);
                Console.WriteLine("Restart the game to apply changes");
            }
            catch (Exception ex)
            {
                Log.Error($"Error white patching: {ex}");
            }

            Console.WriteLine("Press any key to continue...");
            Console.ReadKey(true);
        }
        private static void patch(string gameFolder, bool supportUltra)
        {
            Patcher patcher = new Patcher(Path.GetFullPath("Patches"), gameFolder);

            patcher.BeforeEditFile += (from, to) =>
            {
                string dest = Path.Combine(backupFolder, from);
                if (File.Exists(dest)) return;

                Log.Information($"Creating backup of '{Path.GetFileName(from)}'");
                new FileInfo(dest).Directory!.Create();

                File.Copy(Path.Combine(gameFolder, from), dest);
                Log.Debug("Done");
            };

            List<string> patches = new List<string>()
            {
                "Required.patch"
            };
            if (supportUltra) patches.Add("RemoteControlUltra.patch");
            patcher.PatchAll(patches);

            if (supportUltra)
            {
                Log.Information($"Copying struct.lua");
                Directory.CreateDirectory(Path.Combine(gameFolder, "lua/common/libs/luastruct"));
                File.Copy("struct.lua", Path.Combine(gameFolder, "lua/common/libs/luastruct/struct.lua"), true);
                Log.Debug("Done");
            }
        }

        private static void restoreUI()
        {
            Console.Clear();

            if (!Directory.Exists(backupFolder))
            {
                Console.WriteLine("No backup to restore from");
                Console.WriteLine("Press any key to continue...");
                Console.ReadKey(true);
                return;
            }

            string? gameFolder = locateGameFolder();
            if (string.IsNullOrEmpty(gameFolder)) return;

            Console.Clear();

            try
            {
                restore(gameFolder);
                Console.WriteLine("Restart the game to apply changes");
            }
            catch (Exception ex)
            {
                Log.Error($"Error white patching: {ex}");
            }

            Console.WriteLine("Press any key to continue...");
            Console.ReadKey(true);
        }
        private static void restore(string gameFolder)
        {
            string backupPath = Path.GetFullPath(backupFolder);
            foreach (string file in Directory.EnumerateFiles(backupPath, "*", SearchOption.AllDirectories))
            {
                Log.Information($"Restoring '{Path.GetFileName(file)}'");
                string dest = Path.Combine(gameFolder, Path.GetRelativePath(backupPath, file));
                File.Copy(file, dest, true);
                Log.Debug("Done");
            }

            if (Directory.Exists(Path.Combine(gameFolder, "lua/common/libs/luastruct")))
            {
                Log.Information($"Deleting struct.lua");
                Directory.Delete(Path.Combine(gameFolder, "lua/common/libs/luastruct"), true);
                Log.Debug("Done");
            }
        }

        private static string? locateGameFolder()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                string? gameDir = tryLocateUsingSteam();
                if (!string.IsNullOrEmpty(gameDir))
                {
                    Console.Write($"Detected that the game is installed in: {gameDir}, is that correct (Y/n): ");
                    Console.CursorVisible = true;
                    string? resp = Console.ReadLine()?.Trim();
                    Console.CursorVisible = false;
                    if (resp != "N" && resp != "n")
                        return gameDir;
                }
            }

            while (true)
            {
                Console.Write("Enter the folder BeamNG.drive is installed in (has the game exe in it): ");
                Console.CursorVisible = true;
                string? resp = Console.ReadLine()?.Trim();
                Console.CursorVisible = false;

                try
                {
                    if (Directory.Exists(resp))
                    {
                        if (Directory.Exists(Path.Combine(resp, "lua")))
                            return resp;
                        else
                            Console.WriteLine("Folder isn't a valid BeamNG.drive install");
                    }
                    else
                        Console.WriteLine("Folder doesn't exist");
                }
                catch
                {
                }
            }
        }
        // Already kinda hard on windows, not even gonna try to implement this for linux
        private static string? tryLocateUsingSteam()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return null;

            using (var key = Registry.LocalMachine.OpenSubKey(Environment.Is64BitOperatingSystem ? @"SOFTWARE\WOW6432Node\Valve\Steam" : @"SOFTWARE\Valve\Steam"))
            {
                if (key?.GetValue("InstallPath") is string steamPath)
                { // found steam install dir
                    string infoFile = Path.Combine(steamPath, "steamapps", "appmanifest_284160.acf");
                    if (!File.Exists(infoFile)) return null;

                    try
                    {
                        var kv = new SteamKit2.KeyValue();
                        using (FileStream fs = File.OpenRead(infoFile))
                            kv.ReadAsText(fs);

                        var installDirName = kv["installdir"].Value;
                        if (string.IsNullOrEmpty(installDirName)) return null;

                        string installDir = Path.Combine(steamPath, "steamapps", "common", installDirName);
                        if (Directory.Exists(installDir)) return installDir;
                        else return null;
                    }
                    catch
                    {
                    }
                }
            }

            return null;
        }
    }
}
