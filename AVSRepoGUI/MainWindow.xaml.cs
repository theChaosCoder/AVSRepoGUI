using PropertyChanged;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static AVSRepoGUI.SettingsWindow;

namespace AVSRepoGUI
{
    /// <summary>
    /// MainWindow.xaml
    /// </summary>
    ///
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public string vspackages_file; // = @"..\avspackages.json";
        public AvsPlugins Plugins { get; set; }
        public Dictionary<string, string> plugins_dll_parents = new Dictionary<string, string>();
        public AvsApi avsrepo;

        public string CurrentPluginPath  { get; set; }
        public string CurrentScriptPath  { get; set; }
        public bool IsNotWorking { get; set; } = true;
        public bool PortableMode { get; set; } = false;
        public event PropertyChangedEventHandler PropertyChanged;
        public bool HideInstalled { get; set; }
        public string consolestd { get; set; }
        public List<string> consolestdL = new List<string>();

        public string version = "v0.9.1";
        public string AppTitle { get; set; }
        public bool Win64 { get; set; }
        public PluginPaths Avs64Paths { get; set; }
        public PluginPaths Avs32Paths { get; set; }
        public bool showedFirstTimeSettingsAvs = false;
        

        public class AvsPlugins : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler PropertyChanged;

            public Package[] UpdateAvailable { get; set; }
            public Package[] Installed { get; set; }
            public Package[] NotInstalled { get; set; }
            public Package[] Unknown { get; set; }

            [AlsoNotifyFor("All")]
            public Package[] Full { get; set; }

            // Quick and dirty props for Tab Header (stringFormat doesn't work for Header)
            public string TabInstalled { get; set; }
            public string TabNotInstalled { get; set; }
            public string TabUpdateAvailable { get; set; }
            public string TabInstalledUnknown { get; set; }
            public string TabAll { get; set; }

            private Package[] _all;
            public Package[] All
            {
                get { return _all; }
                set
                {
                    UpdateAvailable = Array.FindAll(value, c => c.Status == AvsApi.PluginStatus.UpdateAvailable);
                    Installed =       Array.FindAll(value, c => c.Status == AvsApi.PluginStatus.Installed);
                    NotInstalled =    Array.FindAll(value, c => c.Status == AvsApi.PluginStatus.NotInstalled);
                    Unknown =         Array.FindAll(value, c => c.Status == AvsApi.PluginStatus.InstalledUnknown);
                    //Full =            value;
                    _all = value;

                    TabUpdateAvailable =  string.Format("Updates ({0})", UpdateAvailable.Count());
                    TabInstalled =        string.Format("Installed ({0})", Installed.Count());
                    TabNotInstalled =     string.Format("Not Installed ({0})", NotInstalled.Count());
                    TabInstalledUnknown = string.Format("Unknown Version ({0})", Unknown.Count());
                    TabAll =              string.Format("Full List ({0})", _all.Count());
                }
            }
        }

        public MainWindow()
        {
            avsrepo = new AvsApi();
            Plugins = new AvsPlugins();

            //High dpi 288 fix so it won't cut off the title bar on start
            if (Height > SystemParameters.WorkArea.Height)
            {
                Height = SystemParameters.WorkArea.Height;
                Top = 2;
            }

            
            InitializeComponent();
            
            // init Jot Settings Tracker
            //SettingsService.Tracker.Configure<MainWindow>().Property(w => w.showedFirstTimeSettingsAvs);
            //SettingsService.Tracker.Configure<MainWindow>().Property(w => w.Avs64Paths);
            //SettingsService.Tracker.Configure<MainWindow>().Property(w => w.Avs32Paths);
            SettingsService.Tracker.Track(this);
            //showedFirstTimeSettingsAvs = false;


            AddChatter(avsrepo);

            AppTitle = "AVSRepoGUI - A simple plugin manager for AviSynth | " + version;
            InitAvisynth();

            Win64 = Environment.Is64BitOperatingSystem; // triggers checkbox changed event

          /*  // Show AviSynth plugin settings window on first start
            if(!IsVsrepo && !showedFirstTimeSettingsAvs)
            {
                SettingsWindow wizardDialog = new SettingsWindow();
                wizardDialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                _ = wizardDialog.ShowDialog();

                if (wizardDialog != null)
                {
                    Avs32Paths = wizardDialog.Path32;
                    Avs64Paths = wizardDialog.Path64;
                    showedFirstTimeSettingsAvs = true;
                }
            }*/
        }

        private void InitAvisynth()
        {
            avsrepo.SetPortableMode(true); // avsrepo should always be called with -p since it doesn't know anything about avisynth plugin folders
            var settings = new PortableSettings().LoadLocalFile("avsrepogui.json");

            var avsrepo_files = new string[] { "avsrepo.exe", "avsrepo-32.exe", "avsrepo-64.exe" };
            var found_avrespo_bin = false;

            foreach (var avs_file in avsrepo_files)
            {
                if (File.Exists(avs_file))
                {
                    avsrepo.python_bin = avs_file;
                    found_avrespo_bin = true;
                }
            }

            if (!found_avrespo_bin)
            {
                MessageBox.Show("Can't find avsrepo.exe, avsrepo-32.exe or avsrepo-64.exe");
                System.Environment.Exit(1);
            }


            if (settings is null)
            {
                AppIsWorking(true);
                avsrepo.SetArch(Environment.Is64BitOperatingSystem);
                vspackages_file = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName) + "\\avspackages.json"; // That means avsrepo.exe needs to be near avsrepogui
                if (Avs32Paths != null)
                {
                    avsrepo.SetPaths(false, new Paths() { Binaries = Avs32Paths.Plugin, Scripts = Avs32Paths.Script, Definitions = vspackages_file });
                }
                if(Avs64Paths != null)
                {
                    avsrepo.SetPaths(true, new Paths() { Binaries = Avs64Paths.Plugin, Scripts = Avs64Paths.Script, Definitions = vspackages_file });
                }
                //Trigger GetPaths for 32/64 bit, they are cached in VsApi class anyway
                _ = avsrepo.GetPaths(true).Definitions; _ = avsrepo.GetPaths(false).Definitions;
            }
            else // Portable mode, valid avsrepogui.json found
            {
                //TabablzControl doesn't support hiding or collapsing Tabitems. Hide "Settings" (last) tab if we are in avisynth portable mode	
                TabablzControl.Items.RemoveAt(TabablzControl.Items.Count - 1); 

                LabelPortable.Visibility = Visibility.Visible;
                avsrepo.SetPortableMode(true);
                avsrepo.python_bin = settings.Bin;
                vspackages_file = Path.GetDirectoryName(settings.Bin) + "\\avspackages.json";

                // Set paths manually and DONT trigger Win64 onPropertyChanged yet
                avsrepo.SetPaths(true, new Paths() { Binaries = settings.Win64.Binaries, Scripts = settings.Win64.Scripts, Definitions = vspackages_file });
                avsrepo.SetPaths(false, new Paths() { Binaries = settings.Win32.Binaries, Scripts = settings.Win32.Scripts, Definitions = vspackages_file });

                // Triggering  Win64 is now safe
                //Win64 = Environment.Is64BitOperatingSystem;
            }
            
            try
            {
                Plugins.All = LoadLocalVspackage();
                avsrepo.Update();
            }
            catch
            {
                MessageBox.Show("Could not read (or download) avspackages.json.");
                System.Environment.Exit(1);
            }
        }


         private string GetPythonLocation(string python_bin)
         {
             string cmd = "import sys; print(sys.executable)";
             ProcessStartInfo start = new ProcessStartInfo();
             start.FileName = python_bin;
             start.Arguments = string.Format("-c \"{0}\"", cmd);
             start.UseShellExecute = false;// Do not use OS shell
             start.CreateNoWindow = true; // We don't need new window
             start.RedirectStandardOutput = true;// Any output, generated by application will be redirected back
             start.RedirectStandardError = false; // Any error in standard output will be redirected back (for example exceptions)
             using (Process process = Process.Start(start))
             {
                 using (StreamReader reader = process.StandardOutput)
                 {
                     string result = reader.ReadToEnd();
                     return result.Trim();
                 }
             }
         }

        private bool IsPythonCallable()
        {
            try
            {
                Process p = new Process();
                p.StartInfo.FileName = "python.exe";
                p.StartInfo.Arguments = "-V";
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.Start();
                p.WaitForExit();
                return true;
            }
            catch
            {
                return false;
            }
        }


        public void AddChatter(AvsApi chatter)
        {
            //avsrepo.Add(chatter);
            chatter.PropertyChanged += chatter_PropertyChanged;
        }

        private void chatter_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            Console.WriteLine("A property has changed: " + e.PropertyName);
            var s = sender as AvsApi;
            Console.WriteLine("||| " + s.consolestd);
            consolestd += s.consolestd + "\n";
            //consolestd.Add(s.consolestd);
            //ConsoleBox.Text = String.Join(Environment.NewLine, consolestd);
            //ConsoleBox.Text =  s.consolestd + "\n";
        }

        private Package[] LoadLocalVspackage()
        {
            if (!File.Exists(vspackages_file))
            {
                avsrepo.Update();
            }

            //Load vspackages.json
            var jsonString = File.ReadAllText(vspackages_file);
            var packages = AvsPackage.FromJson(jsonString);
            //var packages = JsonConvert.DeserializeObject<Vspackage>(jsonString);
            //return (packages.FileFormat, packages.Packages);
            return packages.Packages;
        }

        public void AppIsWorking(bool status)
        {
            Progressbar.IsIndeterminate = status;
            IsNotWorking = !status;
        }


        public void FilterPlugins(Package[] plugins)
        {
            if(plugins != null)
            {
                string search = searchBox.Text;
                if (search.Length > 0 && search != "Search")
                {
                    plugins = Array.FindAll(plugins, c => c.Name.ToLower().Contains(search) || (c.Namespace?.ToLower().Contains(search) ?? c.Modulename.ToLower().Contains(search)));
                }
                if (HideInstalled)
                {
                    plugins = Array.FindAll(plugins, c => c.Status != AvsApi.PluginStatus.Installed);
                }

                if(search.Length == 0)
                {
                    Plugins.All = Plugins.Full;
                } else
                {
                    Plugins.All = plugins;
                }                
                dataGrid.Columns[0].SortDirection = ListSortDirection.Ascending;
                dataGridAvailable.Columns[0].SortDirection = ListSortDirection.Ascending;
                dataGridUnknown.Columns[0].SortDirection = ListSortDirection.Ascending;
                dataGridNotInstalled.Columns[0].SortDirection = ListSortDirection.Ascending;
                dataGridAll.Columns[0].SortDirection = ListSortDirection.Ascending;
            }
        }

        private async Task ReloadPluginsAsync()
        {
            var _plugins = LoadLocalVspackage();
            if(Win64)
                _plugins = Array.FindAll(_plugins, c => c.Releases[0].Win64 != null || c.Releases[0].Script != null);
            else
                _plugins = Array.FindAll(_plugins, c => c.Releases[0].Win32 != null || c.Releases[0].Script != null);

            var plugins_installed = await avsrepo.GetInstalledAsync();

            // Set Plugin status (installed, not installed, update available etc.)
            foreach (var plugin in plugins_installed)
            {
                var index = Array.FindIndex(_plugins, row => row.Identifier == plugin.Key);
                if (index >= 0) //-1 if not found
                {
                    _plugins[index].Status = plugin.Value.Value;
                    _plugins[index].Releases[0].VersionLocal = plugin.Value.Key;
                }
            }
            Plugins.Full = _plugins;
            FilterPlugins(Plugins.Full);
        }

        private async void Button_upgrade_all(object sender, RoutedEventArgs e)
        {
            AppIsWorking(true);
            await avsrepo.UpgradeAllAsync();
            await ReloadPluginsAsync();
            AppIsWorking(false);
        }


        private async void Button_Install(object sender, RoutedEventArgs e)
        {
            AppIsWorking(true);
            var button = sender as Button;

            var plugin_status = ((Package)button.DataContext).Status;
            string plugin = ((Package)button.DataContext).Namespace ?? ((Package)button.DataContext).Modulename;
            consolestd = "";
            consolestdL.Clear();
            if(HasWriteAccessToFolder(CurrentPluginPath))
            {
                switch (plugin_status)
                {
                    case AvsApi.PluginStatus.Installed:
                        if (MessageBox.Show("Uninstall " + ((Package)button.DataContext).Name + "?", "Uninstall?", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                        {
                            await avsrepo.UninstallAsync(plugin);
                        }
                        break;
                    case AvsApi.PluginStatus.InstalledUnknown:
                        if (MessageBox.Show("Your local file (with unknown version) has the same name as " + ((Package)button.DataContext).Name + " and will be overwritten, proceed?", "Force Upgrade?", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                        {
                            await avsrepo.UpgradeAsync(plugin, force: true);
                        }
                        break;
                    case AvsApi.PluginStatus.NotInstalled:
                        await avsrepo.InstallAsync(plugin);
                        break;
                    case AvsApi.PluginStatus.UpdateAvailable:
                        await avsrepo.UpgradeAsync(plugin);
                        break;
                }
            
            
          
                ConsoleBox.Focus();
                ConsoleBox.CaretIndex = ConsoleBox.Text.Length;
                ConsoleBox.ScrollToEnd();

                await ReloadPluginsAsync();
            } else
            {
                MessageBox.Show("Can't write to plugins folder. Restart program as admin.");
            }
            AppIsWorking(false);
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterPlugins(Plugins.Full);
        }

        private void HideInstalled_Checked(object sender, RoutedEventArgs e)
        {
            FilterPlugins(Plugins.Full);
        }

        private void HideInstalled_Unchecked(object sender, RoutedEventArgs e)
        {
            FilterPlugins(Plugins.Full);
        }

        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            var textbox = sender as TextBox;
            textbox.Clear();
        }

        private void CheckBox_Win64_Checked(object sender, RoutedEventArgs e)
        {
            CheckBoxToggleHelper(sender as CheckBox);
        }

        private void CheckBox_Win64_Unchecked(object sender, RoutedEventArgs e)
        {
            CheckBoxToggleHelper(sender as CheckBox);
        }

        private async void CheckBoxToggleHelper(CheckBox c)
        {
            AppIsWorking(true);
            Win64 = c.IsChecked.Value;
            avsrepo.SetArch(Win64);
            CurrentPluginPath = avsrepo.paths[Win64].Binaries;
            CurrentScriptPath = avsrepo.paths[Win64].Scripts;
            await ReloadPluginsAsync();
            AppIsWorking(false);
        }

        public static bool HasWriteAccessToFolder(string FilePath)
        {
            try
            {
                FileSystemSecurity security;
                if (File.Exists(FilePath))
                {
                    security = File.GetAccessControl(FilePath);
                }
                else
                {
                    security = Directory.GetAccessControl(Path.GetDirectoryName(FilePath));
                }
                var rules = security.GetAccessRules(true, true, typeof(NTAccount));

                var currentuser = new WindowsPrincipal(WindowsIdentity.GetCurrent());
                bool result = false;
                foreach (FileSystemAccessRule rule in rules)
                {
                    if (0 == (rule.FileSystemRights &
                        (FileSystemRights.WriteData | FileSystemRights.Write)))
                    {
                        continue;
                    }

                    if (rule.IdentityReference.Value.StartsWith("S-1-"))
                    {
                        var sid = new SecurityIdentifier(rule.IdentityReference.Value);
                        if (!currentuser.IsInRole(sid))
                        {
                            continue;
                        }
                    }
                    else
                    {
                        if (!currentuser.IsInRole(rule.IdentityReference.Value))
                        {
                            continue;
                        }
                    }

                    if (rule.AccessControlType == AccessControlType.Deny)
                        return false;
                    if (rule.AccessControlType == AccessControlType.Allow)
                        result = true;
                }
                return result;
            }
            catch
            {
                return false;
            }
        }


        private void Hyperlink_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Hello friend");
        }
        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            Process.Start(e.Uri.AbsoluteUri);
        }

        private void Hyperlink_Click_1(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Definitions: " + avsrepo.GetPaths(Win64).Definitions + "\nScripts: " + avsrepo.GetPaths(Win64).Scripts + "\nBinaries: " + avsrepo.GetPaths(Win64).Binaries);
        }

        private void Hyperlink_open(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            Process.Start(e.Uri.AbsoluteUri);
        }

        private void Hyperlink_namespace(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            var package = (Package)(sender as Hyperlink).DataContext;
            Process.Start("https://github.com/theChaosCoder/avsrepo/tree/master/packages/" + (package.Namespace ?? package.Modulename) + ".json");
        }

        private void Hyperlink_Click_Plugins(object sender, RoutedEventArgs e)
        {
            Process.Start("explorer.exe", avsrepo.GetPaths(Win64).Binaries);
        }

        private void Hyperlink_Click_Scripts(object sender, RoutedEventArgs e)
        {
            Process.Start("explorer.exe", avsrepo.GetPaths(Win64).Scripts);
        }

        private void Hyperlink_Explorer(object sender, RoutedEventArgs e)
        {
            var hyperlink = sender as Hyperlink;
            Process.Start("explorer.exe", hyperlink.NavigateUri.ToString());
        }

        private void Hyperlink_Click_about(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Version " + version);
        }


        /// <summary>
        /// Print a "block" of plugins, helper function
        /// </summary>
        /// <param name="plugins"></param>
        /// <param name="id"></param>
        /// <param name="errmsg"></param>
        /// <param name="tb"></param>
        /// <param name="fullpath"></param>
        private void DiagPrintHelper(Dictionary<string, List<string>> plugins, string id, string errmsg, Paragraph tb, bool fullpath = false)
        {
            if (plugins[id].Count() > 0)
            {
                //tb.Text += errmsg;
                tb.Inlines.Add(new Run(errmsg) { FontSize = 14 });
                tb.Inlines.Add(new Run("------------------------------------------------------------\n") { Foreground = Brushes.SlateBlue });
                foreach (var p in plugins[id])
                {
                    if(fullpath)
                    {
                        tb.Inlines.Add("   ");
                        if (Path.IsPathRooted(p))
                            tb.Inlines.Add(new Run(Path.GetDirectoryName(p) + @"\") { Foreground = Brushes.Silver });
                        tb.Inlines.Add(Path.GetFileName(p) + "\n");
                    }
                    else
                    {
                        tb.Inlines.Add("   " + Path.GetFileName(p) + "\n");
                    }
                }
            }
        }


        private async void TabablzControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // ##### AviSynth diagnose #####
            if (SettingsTab.IsSelected)
            {
                SettingsWindow wizardDialog = new SettingsWindow();
                wizardDialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                _ = wizardDialog.ShowDialog();

                if (wizardDialog != null)
                {
                    Avs32Paths = wizardDialog.Path32;
                    Avs64Paths = wizardDialog.Path64;
                    if (Avs32Paths != null)
                    {
                        avsrepo.SetPaths(false, new Paths() { Binaries = Avs32Paths.Plugin, Scripts = Avs32Paths.Script, Definitions = vspackages_file });
                    }
                    if (Avs64Paths != null)
                    {
                        avsrepo.SetPaths(true, new Paths() { Binaries = Avs64Paths.Plugin, Scripts = Avs64Paths.Script, Definitions = vspackages_file });
                    }
                    CurrentPluginPath = avsrepo.paths[Win64].Binaries;
                    CurrentScriptPath = avsrepo.paths[Win64].Scripts;
                }
                await ReloadPluginsAsync();
            } 
            else
            {
                AppIsWorking(true);

                // Textoutput controls init
                RichTextBox richtextbox = new RichTextBox();
                FlowDocument flowdoc = new FlowDocument();
                Paragraph tb = new Paragraph();

                richtextbox.IsDocumentEnabled = true;
                richtextbox.IsReadOnly = true;
                tb.FontFamily = new FontFamily("Lucida Console");
                tb.Padding = new Thickness(8);

                flowdoc.Blocks.Add(tb);
                richtextbox.Document = flowdoc;

                var diag = new Diagnose("");
                var script_dups = diag.CheckDuplicateAvsScripts(avsrepo.GetPaths(Win64).Scripts);


                tb.Inlines.Add(new Run("Use ") { FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Brushes.Red });
                Hyperlink avsmeter = new Hyperlink()
                {
                    IsEnabled = true,
                    NavigateUri = new Uri("https://forum.doom9.org/showthread.php?t=173259")
                };
                avsmeter.RequestNavigate += new System.Windows.Navigation.RequestNavigateEventHandler(Hyperlink_open);
                avsmeter.Inlines.Add(new Run("Avisynth Info Tool or AVSMeter") { FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Brushes.Blue });
                tb.Inlines.Add(avsmeter);
                tb.Inlines.Add(new Run(" to detect other Avisynth problems. \n\n") { FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Brushes.Red });


                tb.Inlines.Add(new Run("\n\nDuplicate Function Name Detection \n") { FontSize = 14 });
                tb.Inlines.Add(new Run("------------------------------------------------------------\n") { Foreground = Brushes.SlateBlue });
                tb.Inlines.Add("\nPath of *.avsi files: " + avsrepo.GetPaths(Win64).Scripts);
                tb.Inlines.Add("\nFound " + script_dups.Count + " potential conflicts: \n");
                if (script_dups.Count > 0)
                {
                    foreach (var dup in script_dups)
                    {
                        tb.Inlines.Add("\nFunction name: ");
                        tb.Inlines.Add(new Run(dup.Key + "\n") { FontWeight = FontWeights.Bold });
                        foreach (var file in dup.Value)
                        {
                            tb.Inlines.Add(new Run("\t" + Path.GetDirectoryName(file) + @"\") { Foreground = Brushes.Silver });
                            tb.Inlines.Add(new Run(Path.GetFileName(file) + "\n"));
                        }
                    }
                }
                else
                {
                    tb.Inlines.Add(new Run("\nNo duplicate functions found in *.avsi files") { FontWeight = FontWeights.Bold, Foreground = Brushes.Green });
                }

                ScrollViewer.Content = richtextbox;
                AppIsWorking(false);
            }
                
            

            
        }

    }


}
