using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace MinecraftModLoader
{
    public partial class MainWindow : Window
    {
        private readonly Dictionary<string, string> _categoryFolders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Button> _categoryButtons = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);
        private string _customLauncherPath = string.Empty;

        private static readonly string[] Categories = { "Mods", "Resource Packs", "Shaders", "Configs" };

        private static string TargetSubfolder(string category) => category switch
        {
            "Mods" => "mods",
            "Resource Packs" => "resourcepacks",
            "Shaders" => "shaderpacks",
            "Configs" => "config",
            _ => category.ToLowerInvariant().Replace(" ", "")
        };

        public MainWindow()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                McPathBox.Text = Path.Combine(appdata, ".minecraft");
                UpdateBuildEnabled();
            };
        }

        private void LauncherChanged(object sender, RoutedEventArgs e)
        {
            if (CustomLauncherPanel != null)
                CustomLauncherPanel.Visibility = RbCustom.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            UpdateBuildEnabled();
        }

        private void BrowseCustomLauncher_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Choose installer exe",
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true)
            {
                _customLauncherPath = dlg.FileName;
                CustomLauncherPath.Text = _customLauncherPath;
                UpdateBuildEnabled();
            }
        }

        private void BrowseMcPath_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = "Choose Minecraft folder" };
            if (dlg.ShowDialog() == true)
            {
                McPathBox.Text = dlg.FolderName;
                UpdateBuildEnabled();
            }
        }

        private void ContentChecked(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb) return;
            var cat = cb.Content?.ToString() ?? string.Empty;
            if (!_categoryButtons.ContainsKey(cat))
            {
                var btn = new Button
                {
                    Content = "Choose " + cat,
                    Width = 160,
                    Height = 28,
                    Margin = new Thickness(0, 2, 0, 2),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                btn.Click += (_, _) => ChooseFolder(cat);
                _categoryButtons[cat] = btn;
                ButtonsPanel.Children.Add(btn);
            }
            UpdateBuildEnabled();
        }

        private void ContentUnchecked(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb) return;
            var cat = cb.Content?.ToString() ?? string.Empty;
            if (_categoryButtons.TryGetValue(cat, out var btn))
            {
                ButtonsPanel.Children.Remove(btn);
                _categoryButtons.Remove(cat);
            }
            _categoryFolders.Remove(cat);
            UpdateBuildEnabled();
        }

        private void ChooseFolder(string category)
        {
            var dlg = new OpenFolderDialog { Title = "Choose " + category + " folder" };
            if (dlg.ShowDialog() == true)
            {
                _categoryFolders[category] = dlg.FolderName;
                Log("Selected " + category + ": " + dlg.FolderName);
                UpdateBuildEnabled();
            }
        }

        private bool IsChecked(CheckBox cb) => cb != null && cb.IsChecked == true;

        private List<string> SelectedCategories()
        {
            var list = new List<string>();
            if (IsChecked(CbMods)) list.Add("Mods");
            if (IsChecked(CbResourcePacks)) list.Add("Resource Packs");
            if (IsChecked(CbShaders)) list.Add("Shaders");
            if (IsChecked(CbConfigs)) list.Add("Configs");
            return list;
        }

        private bool IsReady()
        {
            if (CbMods == null || CbResourcePacks == null || CbShaders == null || CbConfigs == null) return false;
            if (VersionCombo == null || McPathBox == null || BuildBtn == null) return false;
            var cats = SelectedCategories();
            if (cats.Count == 0) return false;
            foreach (var c in cats)
            {
                if (!_categoryFolders.ContainsKey(c)) return false;
                var p = _categoryFolders[c];
                if (string.IsNullOrWhiteSpace(p) || !Directory.Exists(p)) return false;
            }
            var version = (VersionCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? VersionCombo.Text;
            if (string.IsNullOrWhiteSpace(version?.Trim())) return false;
            if (RbCustom.IsChecked == true)
            {
                if (string.IsNullOrWhiteSpace(_customLauncherPath) || !File.Exists(_customLauncherPath)) return false;
            }
            if (string.IsNullOrWhiteSpace(McPathBox.Text.Trim())) return false;
            return true;
        }

        private void UpdateBuildEnabled()
        {
            if (BuildBtn == null) return;
            BuildBtn.IsEnabled = IsReady();
        }

        private void Log(string msg)
        {
            Dispatcher.Invoke(() =>
            {
                LogBox.AppendText(msg + Environment.NewLine);
                LogBox.ScrollToEnd();
            });
        }

        private void Build_Click(object sender, RoutedEventArgs e)
        {
            if (!IsReady())
            {
                MessageBox.Show("Please complete all steps first.", "Not ready", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            BuildBtn.IsEnabled = false;
            try
            {
                var mcPath = McPathBox.Text.Trim();
                var version = (VersionCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? VersionCombo.Text;
                if (string.IsNullOrWhiteSpace(version)) version = VersionCombo.Text;
                Log("Version: " + version);

                // Stage content
                var stage = Path.Combine(Path.GetTempPath(), "MinecraftModLoader_Stage_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(stage);
                try
                {
                    foreach (var cat in SelectedCategories())
                    {
                        var src = _categoryFolders[cat];
                        var sub = TargetSubfolder(cat);
                        var dest = Path.Combine(stage, sub);
                        Directory.CreateDirectory(dest);
                        CopyDirectory(src, dest);
                        Log("Staged " + cat + " -> " + sub);
                    }

                    // Launcher installer
                    if (RbCustom.IsChecked == true && File.Exists(_customLauncherPath))
                    {
                        var launcherDir = Path.Combine(stage, "launcher");
                        Directory.CreateDirectory(launcherDir);
                        File.Copy(_customLauncherPath, Path.Combine(launcherDir, Path.GetFileName(_customLauncherPath)), true);
                        Log("Staged launcher installer.");
                    }

                    // manifest.txt (read by the installer stub)
                    var isCustom = RbCustom.IsChecked == true;
                    var customInstallerName = isCustom ? Path.GetFileName(_customLauncherPath) : string.Empty;
                    var manifest = "VERSION=" + version + "\\n" +
                                   "LAUNCHER_MODE=" + (isCustom ? "custom" : "legacy") + "\\n" +
                                   "CUSTOM_INSTALLER=" + customInstallerName + "\\n" +
                                   "HINT_PATH=" + mcPath + "\\n";
                    File.WriteAllText(Path.Combine(stage, "manifest.txt"), manifest, Encoding.ASCII);

                    // payload.zip
                    var payloadZip = Path.Combine(Path.GetTempPath(), "payload_" + Guid.NewGuid().ToString("N") + ".zip");
                    if (File.Exists(payloadZip)) File.Delete(payloadZip);
                    ZipFile.CreateFromDirectory(stage, payloadZip, CompressionLevel.Optimal, false);
                    Log("Created payload zip.");

                    // Try to build a self-contained .exe installer via stub compilation
                    string? finalPath = null;
                    bool exeOk = false;
                    try
                    {
                        var stubDir = Path.Combine(Path.GetTempPath(), "MinecraftModLoader_Stub_" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(stubDir);
                        try
                        {
                            var stubSrc = StubSourceCode;
                            File.WriteAllText(Path.Combine(stubDir, "Program.cs"), stubSrc, Encoding.UTF8);
                            var stubCsproj = StubCsproj;
                            File.WriteAllText(Path.Combine(stubDir, "Stub.csproj"), stubCsproj, Encoding.UTF8);

                            var psi = new ProcessStartInfo("dotnet", "publish \"" + Path.Combine(stubDir, "Stub.csproj") + "\" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o \"" + Path.Combine(stubDir, "out") + "\"")
                            {
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                CreateNoWindow = true
                            };
                            using var proc = Process.Start(psi);
                            if (proc == null) throw new Exception("Failed to start dotnet.");
                            var stdout = proc.StandardOutput.ReadToEnd();
                            var stderr = proc.StandardError.ReadToEnd();
                            proc.WaitForExit(180000);
                            if (!proc.HasExited) { proc.Kill(); throw new Exception("dotnet publish timed out."); }
                            if (proc.ExitCode != 0)
                            {
                                Log("dotnet publish failed: " + stderr);
                                throw new Exception("dotnet publish failed.");
                            }
                            var stubExe = Path.Combine(stubDir, "out", "Stub.exe");
                            if (!File.Exists(stubExe)) throw new Exception("Stub exe not produced.");
                            var stubBytes = File.ReadAllBytes(stubExe);
                            var payloadBytes = File.ReadAllBytes(payloadZip);
                            var marker = Encoding.ASCII.GetBytes("MLPAYLOAD_MARKER_V1");
                            using (var fs = new FileStream(Path.Combine(Path.GetTempPath(), "installer_tmp.exe"), FileMode.Create, FileAccess.Write))
                            {
                                fs.Write(stubBytes, 0, stubBytes.Length);
                                fs.Write(marker, 0, marker.Length);
                                var lenBytes = BitConverter.GetBytes((long)payloadBytes.Length);
                                fs.Write(lenBytes, 0, lenBytes.Length);
                                fs.Write(payloadBytes, 0, payloadBytes.Length);
                            }
                            finalPath = Path.Combine(Path.GetTempPath(), "MinecraftModPackInstaller.exe");
                            if (File.Exists(finalPath)) File.Delete(finalPath);
                            File.Move(Path.Combine(Path.GetTempPath(), "installer_tmp.exe"), finalPath);
                            exeOk = true;
                            Log("Built self-extracting .exe installer.");
                        }
                        finally
                        {
                            try { Directory.Delete(stubDir, true); } catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log("Could not build .exe (" + ex.Message + "). Falling back to self-extracting .bat.");
                        exeOk = false;
                    }

                    if (!exeOk)
                    {
                        // Fallback: self-extracting .bat
                        var b64 = Convert.ToBase64String(File.ReadAllBytes(payloadZip));
                        var sb = new StringBuilder();
                        sb.AppendLine("@echo off");
                        sb.AppendLine("setlocal");
                        sb.AppendLine("set \"OUTDIR=%TEMP%\\MinecraftModLoader_%RANDOM%\"");
                        sb.AppendLine("mkdir \"%OUTDIR%\" 2>nul");
                        sb.AppendLine("set \"ZIPFILE=%OUTDIR%\\payload.zip\"");
                        sb.AppendLine("powershell -NoProfile -Command \"$lines=[IO.File]::ReadAllLines('%~f0'); $start=0; for($i=0;$i -lt $lines.Count;$i++){ if($lines[$i] -eq '::PAYLOAD_START'){ $start=$i+1; break } }; $b=[Convert]::FromBase64String(($lines[$start..($lines.Count-1)] -join '')); [IO.File]::WriteAllBytes('%ZIPFILE%',$b)\"");
                        sb.AppendLine("powershell -NoProfile -Command \"Expand-Archive -Path '%ZIPFILE%' -DestinationPath '%OUTDIR%' -Force\"");
                        sb.AppendLine("powershell -NoProfile -ExecutionPolicy Bypass -File \"%OUTDIR%\\install.ps1\"");
                        sb.AppendLine("del \"%ZIPFILE%\" 2>nul");
                        sb.AppendLine("endlocal");
                        sb.AppendLine("exit /b");
                        sb.AppendLine("::PAYLOAD_START");
                        // write base64 in 76-char lines
                        for (int i = 0; i < b64.Length; i += 76)
                        {
                            sb.AppendLine(b64.Substring(i, Math.Min(76, b64.Length - i)));
                        }
                        finalPath = Path.Combine(Path.GetTempPath(), "MinecraftModPackInstaller.bat");
                        File.WriteAllText(finalPath, sb.ToString(), Encoding.ASCII);
                        Log("Built self-extracting .bat installer.");
                    }

                    // Ask where to save
                    var ext = exeOk ? ".exe" : ".bat";
                    var sfd = new SaveFileDialog
                    {
                        Title = "Save installer as",
                        FileName = "MinecraftModPackInstaller" + ext,
                        Filter = exeOk ? "Executable (*.exe)|*.exe" : "Batch script (*.bat)|*.bat",
                        DefaultExt = ext
                    };
                    string destPath;
                    if (sfd.ShowDialog() == true)
                    {
                        destPath = sfd.FileName;
                    }
                    else
                    {
                        destPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "MinecraftModPackInstaller" + ext);
                    }
                    if (File.Exists(destPath)) File.Delete(destPath);
                    File.Copy(finalPath!, destPath, true);
                    Log("Installer saved: " + destPath);
                    MessageBox.Show("Installer built successfully:\n" + destPath, "Build complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                finally
                {
                    try { Directory.Delete(stage, true); } catch { }
                    // cleanup payload zip
                }
            }
            catch (Exception ex)
            {
                Log("Error: " + ex.Message);
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                UpdateBuildEnabled();
            }
        }

        private static void CopyDirectory(string source, string destination)
        {
            foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(source, dir);
                Directory.CreateDirectory(Path.Combine(destination, rel));
            }
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(source, file);
                var destFile = Path.Combine(destination, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
                File.Copy(file, destFile, true);
            }
        }

        private const string StubSourceCode = @"using System;
using System.IO;
using System.IO.Compression;
using System.Diagnostics;

class Program
{
    static void Main(string[] args)
    {
        try
        {
            var selfPath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(selfPath)) selfPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var bytes = File.ReadAllBytes(selfPath);
            var marker = System.Text.Encoding.ASCII.GetBytes(""MLPAYLOAD_MARKER_V1"");
            int idx = -1;
            for (int i = bytes.Length - marker.Length; i >= 0; i--)
            {
                bool ok = true;
                for (int j = 0; j < marker.Length; j++) { if (bytes[i + j] != marker[j]) { ok = false; break; } }
                if (ok) { idx = i; break; }
            }
            if (idx < 0) { Console.WriteLine(""No payload found.""); Console.ReadKey(); return; }
            int pos = idx + marker.Length;
            long len = BitConverter.ToInt64(bytes, pos); pos += 8;
            var payload = new byte[len];
            Buffer.BlockCopy(bytes, pos, payload, 0, (int)len);
            var tempDir = Path.Combine(Path.GetTempPath(), ""MinecraftModLoader_"" + Guid.NewGuid().ToString(""N""));
            Directory.CreateDirectory(tempDir);
            var zipPath = Path.Combine(tempDir, ""payload.zip"");
            File.WriteAllBytes(zipPath, payload);
            ZipFile.ExtractToDirectory(zipPath, tempDir, true);
            File.Delete(zipPath);
            var installScript = Path.Combine(tempDir, ""install.ps1"");
            if (File.Exists(installScript))
            {
                var psi = new ProcessStartInfo(""powershell.exe"", ""-NoProfile -ExecutionPolicy Bypass -File install.ps1"")
                {
                    WorkingDirectory = tempDir,
                    UseShellExecute = false,
                    CreateNoWindow = false
                };
                var proc = Process.Start(psi);
                proc?.WaitForExit();
                Console.WriteLine();
                Console.WriteLine(""Installer exited with code: "" + (proc != null ? proc.ExitCode.ToString() : ""unknown""));
            }
            else
            {
                Console.WriteLine(""install.ps1 not found in payload."");
                Console.ReadKey();
            }
            try { Directory.Delete(tempDir, true); } catch { }
        }
        catch (Exception ex)
        {
            Console.WriteLine(""Error: "" + ex.Message);
            Console.WriteLine(""Press any key to exit."");
            Console.ReadKey();
        }
    }
}
";
        private const string StubCsproj = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
    <DebugType>none</DebugType>
  </PropertyGroup>
</Project>
";
    }
}
