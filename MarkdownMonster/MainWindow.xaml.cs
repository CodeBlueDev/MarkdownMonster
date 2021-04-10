﻿#region

/*
 **************************************************************
 *  Author: Rick Strahl
 *          © West Wind Technologies, 2016
 *          http://www.west-wind.com/
 *
 * Created: 04/28/2016
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 **************************************************************
*/

#endregion

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Dragablz;
using FontAwesome.WPF;
using LibGit2Sharp;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using MarkdownMonster._Classes;
using MarkdownMonster.AddIns;
using MarkdownMonster.Annotations;
using MarkdownMonster.Controls.ContextMenus;
using MarkdownMonster.Services;
using MarkdownMonster.Utilities;
using MarkdownMonster.Windows;
using MarkdownMonster.Windows.PreviewBrowser;
using Westwind.Utilities;
using Binding = System.Windows.Data.Binding;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using ContextMenu = System.Windows.Controls.ContextMenu;
using DataFormats = System.Windows.DataFormats;
using DataObject = System.Windows.DataObject;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Image = System.Windows.Controls.Image;
using MenuItem = System.Windows.Controls.MenuItem;
using MessageBox = System.Windows.MessageBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Orientation = System.Windows.Controls.Orientation;
using ToolBar = System.Windows.Controls.ToolBar;

namespace MarkdownMonster
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow
        //, IPreviewBrowser
    {
        public AppModel Model { get; set; }

        public NamedPipeManager PipeManager { get; set; }

        public IntPtr Hwnd
        {
            get
            {
                if (_hwnd == IntPtr.Zero)
                    _hwnd = new WindowInteropHelper(this).EnsureHandle();

                return _hwnd;
            }
        }

        private IntPtr _hwnd = IntPtr.Zero;

        private DateTime _invoked = DateTime.MinValue;


        /// <summary>
        /// Manages the Preview Rendering in a WebBrowser Control
        /// </summary>
        public IPreviewBrowser PreviewBrowser { get; set; }

        public PreviewBrowserWindow PreviewBrowserWindow
        {
            set { _previewBrowserWindow = value; }
            get
            {
                if (Model.Configuration.IsPreviewVisible && (_previewBrowserWindow == null || _previewBrowserWindow.IsClosed))
                {
                    _previewBrowserWindow = new PreviewBrowserWindow();
                    if (Model.Configuration.WindowPosition.PreviewDisplayMode ==
                        PreviewWindowDisplayModes.ActivatedByMainWindow)
                        _previewBrowserWindow.Owner = this;

                }

                return _previewBrowserWindow;
            }
        }

        private PreviewBrowserWindow _previewBrowserWindow;


        /// <summary>
        /// The Preview Browser Container Grid that contains the
        /// Web Browser control that handles the Document tied
        /// preview.
        /// </summary>
        public Grid PreviewBrowserContainer { get; set; }


        /// <summary>
        /// The Preview Browser Tab if active that is used
        /// for image and URL previews (ie. the Preview
        /// without an associated editor)
        /// </summary>
        public TabItem PreviewTab { get; set; }

        public TabItem FavoritesTab { get; set; }

        public TabItem SearchTab { get; set; }

        public TabItem LintingErrorTab { get; set; }


        private IEWebBrowserControl previewBrowser;

        /// <summary>
        /// Display Statusbar messages easily with pre-configuraed helper  methods
        /// </summary>
        StatusBarHelper StatusBarHelper { get; }

        /// <summary>
        /// Keybindings for the window and editor.
        /// </summary>
        public KeyBindingsManager KeyBindings { get; set; }



        public MainWindow()
        {
            // Initially don't show an icon until we have loaded
            // the form in the right location (in Activate -> Onload after FixMonitorPosition)
            ShowInTaskbar = false;

            InitializeComponent();

            Model = new AppModel(this);
            AddinManager.Current.RaiseOnModelLoaded(Model);

            // This doesn't fire when first started, but fires when
            // addins are added at from the Addin Manager at runtie
            AddinManager.Current.AddinsLoaded = OnAddinsLoaded;

            Model.WindowLayout = new MainWindowLayoutModel(this);

            DataContext = Model;

            TabControl.ClosingItemCallback = TabControlDragablz_TabItemClosing;




            Loaded += OnLoaded;
            Drop += MainWindow_Drop;
            AllowDrop = true;
            Activated += OnActivated;
            StateChanged += MainWindow_StateChanged;


            // Singleton App startup - server code that listens for other instances
            if (mmApp.Configuration.UseSingleWindow && !App.ForceNewWindow)
            {
                // Listen for other instances launching and pick up
                // forwarded command line arguments
                PipeManager = new NamedPipeManager("MarkdownMonster");
                PipeManager.StartServer();
                PipeManager.ReceiveString += HandleNamedPipe_OpenRequest;
            }

            // Override some of the theme defaults (dark header specifically)
            mmApp.SetThemeWindowOverride(this, isMainWindow: true);

            StatusBarHelper = new StatusBarHelper(StatusText, StatusIcon);
        }




        #region Opening and Closing

        private void OnLoaded(object sender, RoutedEventArgs e)
        {

            // Load either default preview browser or addin-overridden browser
            LoadPreviewBrowser();

            RestoreSettings();

            var opener = new CommandLineOpener(this);
            opener.OpenFilesFromCommandLine();

            CheckForFirstRun();

            BindTabHeaders();
            SetWindowTitle();

            var left = Left;
            Left = 300000;

            Model.IsPresentationMode = App.StartInPresentationMode;
            if (!Model.IsPresentationMode)
                Model.IsPresentationMode = mmApp.Configuration.OpenInPresentationMode;

            // run out of band
            Dispatcher.InvokeAsync(() =>
            {
                Left = left;

                FixMonitorPosition();
                ShowInTaskbar = true;


                if (Model.IsPresentationMode)
                {
                    Dispatcher.InvokeAsync(() => Model.WindowLayout.SetPresentationMode(),
                        DispatcherPriority.ApplicationIdle);
                }

                OpenFavorites(noActivate: true);

                //OpenSearchPane(noActivate: true);
            }, DispatcherPriority.Background);

            // run when app is loaded
            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    AddinManager.Current.InitializeAddinsUi(this);
                }
                catch (Exception exception)
                {
                    mmApp.Log("Addin UI Loading failed.", exception);
                }

                AddinManager.Current.RaiseOnWindowLoaded();


                // Tab Header double click to open new tab
                var tabHeaderContainer = TabControl.FindChild<Grid>("HeaderContainerGrid");
                tabHeaderContainer.Background = Brushes.Transparent; // REQUIRED OR CLICK NOT FIRING!
                tabHeaderContainer.MouseLeftButtonDown += TabHeader_DoubleClick;

                var config = Model.Configuration;

                // Start the built-in localhost Web Server if marked for AutoStart
                if (config.WebServer.AutoStart)
                    WebServerLauncher.StartMarkdownMonsterWebServer();

            }, DispatcherPriority.ApplicationIdle);

            // TODO: Check to see why this fails in async block above ^^^
            // this fails to load asynchronously so do it here
            KeyBindings = new MarkdownMonsterKeybindings(this);
            if (!File.Exists(KeyBindings.KeyBindingsFilename))
                KeyBindings.SaveKeyBindings();
            else
            {
                KeyBindings.LoadKeyBindings();
                // always write back out
                Task.Run(() => KeyBindings.SaveKeyBindings());
            }
            KeyBindings.SetKeyBindings();

        }

        /// <summary>
        /// Capture WindowState and 'old' size for maximized windows
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                // capture window size before maximizing
                Model.Configuration.WindowPosition.WindowState = WindowState.Maximized;
                Model.Configuration.WindowPosition.Top = (int) Top;
                Model.Configuration.WindowPosition.Left = (int) Left;
                Model.Configuration.WindowPosition.Width = (int) Width;
                Model.Configuration.WindowPosition.Height = (int) Height;
            }
        }

        void CheckForFirstRun()
        {
            if (mmApp.Configuration.ApplicationUpdates.FirstRun)
            {
                var rect = WindowUtilities.GetScreenDimensions(this);
                var ratio = WindowUtilities.GetDpiRatio(this);
                rect.Width = Convert.ToInt32(Convert.ToDecimal(rect.Width) / ratio);
                rect.Height = Convert.ToInt32(Convert.ToDecimal(rect.Height) / ratio);

                Width = rect.Width - 60;
                if (Width > 1600)
                    Width = 1600;
                Left = 30;
                Model.Configuration.WindowPosition.InternalPreviewWidth = (int) (Convert.ToInt32(Width) * 0.45);

                Height = rect.Height - 75; // account for statusbar
                if (Height > 1100)
                    Height = 1100;
                Top = 10;

                if (TabControl.Items.Count == 0)
                {
                    try
                    {
                        string tempFile = Path.Combine(Path.GetTempPath(), "SampleMarkdown.md");
                        File.Copy(Path.Combine(App.InitialStartDirectory, "SampleMarkdown.md"), tempFile, true);
                        OpenTab(tempFile);
                    }
                    catch (Exception ex)
                    {
                        mmApp.Log("Handled: Unable to copy file to temp folder.", ex);
                    }
                }

                mmApp.Configuration.ApplicationUpdates.FirstRun = false;
            }
        }


        /// <summary>
        /// This is called only if addin loading takes very long
        /// Potentially fired off
        /// </summary>
        public void OnAddinsLoaded()
        {
            // Check to see if we are using another preview browser and load
            // that instead
            Dispatcher.InvokeAsync(() => { LoadPreviewBrowser(); }, DispatcherPriority.ApplicationIdle);
        }


        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
        }


        /// <summary>
        /// Keep track whether the editor is focused on deactivation
        /// </summary>
        bool _saveIsEditorFocused = true;

        protected override void OnDeactivated(EventArgs e)
        {
            var editor = Model.ActiveEditor;
            if (editor == null) return;

            var doc = Model.ActiveDocument;
            if (doc == null) return;

            doc.IsActive = true;

            _saveIsEditorFocused = Model.IsEditorFocused;

            doc.LastEditorLineNumber = editor.GetLineNumber();
            if (doc.LastEditorLineNumber == -1)
                doc.LastEditorLineNumber = 0;

            base.OnDeactivated(e);

            mmApp.SetWorkingSet(10000000, 5000000);
        }

        protected void OnActivated(object sender, EventArgs e)
        {
            // Active Menu Item deactivation don't refocus
            if (MainMenu.Items.OfType<MenuItem>().Any(item => item.IsHighlighted))
                return;

            if (_saveIsEditorFocused && Model.ActiveEditor != null)
            {
                try
                {
                    Model.ActiveEditor.SetEditorFocus();
                    Model.ActiveEditor.RestyleEditor();
                }
                catch
                {
                    // ignore interop errors
                }
            }
        }


        public bool ForceClose = false;

        protected override void OnClosing(CancelEventArgs e)
        {
            // force method to abort - we'll force a close explicitly
            e.Cancel = true;

            if (ForceClose)
            {
                // cleanup code already ran
                e.Cancel = false;
                return;
            }

            // execute shutdown logic - set ForceClose and call Close() to
            // actually close the window
            Dispatcher.InvokeAsync(ShutdownApplication, DispatcherPriority.Normal);
        }

        private void ShutdownApplication()
        {
            try
            {
                // have to do this here to capture open windows etc. in SaveSettings()
                mmApp.Configuration.ApplicationUpdates.AccessCount++;
                _previewBrowserWindow?.Close();
                _previewBrowserWindow = null;
                PreviewBrowser = null;
                PreviewBrowserContainer = null;
                SaveSettings();

                if (!CloseAllTabs())
                {
                    // tab closing was cancelled
                    mmApp.Configuration.ApplicationUpdates.AccessCount--;
                    return;
                }

                // hide the window quickly
                Top -= 10000;


                FolderBrowser?.ReleaseFileWatcher();
                bool isNewVersion = ApplicationUpdater.CheckForNewVersion(false, false);

                var displayCount = 6;
                if (mmApp.Configuration.ApplicationUpdates.AccessCount > 250)
                    displayCount = 1;
                else if (mmApp.Configuration.ApplicationUpdates.AccessCount > 100)
                    displayCount = 2;
                else if (mmApp.Configuration.ApplicationUpdates.AccessCount > 50)
                    displayCount = 5;

                if (!isNewVersion && mmApp.Configuration.ApplicationUpdates.AccessCount % displayCount == 0 &&
                    !UnlockKey.IsAppRegistered())
                {
                    // bring the window back
                    Top += 10000;
                    Opacity = 0;

                    var rd = new RegisterDialog(true);
                    rd.Owner = this;
                    rd.ShowDialog();
                }

                PipeManager?.StopServer();
                WebServer?.StopServer();


                AddinManager.Current.RaiseOnApplicationShutdown();
                AddinManager.Current.UnloadAddins();

                App.Mutex?.Dispose();

                PipeManager?.WaitForThreadShutDown(5000);
                mmApp.Shutdown();

                ForceClose = true;
                Close();
            }
            catch (Exception ex)
            {
                mmApp.Log("Shutdown: " + ex.Message, ex, logLevel: LogLevels.Error);
                ForceClose = true;
                Close();
            }
        }

        public void AddRecentFile(string file, bool noConfigWrite = false)
        {
            Dispatcher.InvokeAsync(() =>
            {
                mmApp.Configuration.AddRecentFile(file);

                if (!noConfigWrite)
                    mmApp.Configuration.Write();

                try
                {
                    MostRecentlyUsedList.AddToRecentlyUsedDocs(Path.GetFullPath(file));
                }
                catch
                {
                }
            }, DispatcherPriority.ApplicationIdle);
        }


        /// <summary>
        /// Adds a fontawesome icon to the editor toolbar (or any toolbar you specify explicitly).
        /// Specify the FontAwesome Icon image name (FontAwesome.Wpf proper Case Syntax) 
        /// </summary>
        /// <param name="iconName">FontAwesome.WPF Icon name (proper case name)</param>
        /// <param name="markdownActionCommand">string action from MarkupMarkdow() implementation or wrap with `html|tag`</param>
        /// <param name="toolbar"></param>
        /// <param name="command"></param>
        public void AddEditToolbarIcon(string iconName, string markdownActionCommand, ToolBar toolbar = null,
            ICommand command = null)
        {
            ImageSource icon = null;
            if (FontAwesomeIcon.TryParse(iconName, out FontAwesomeIcon iconId))
            {
                icon = ImageAwesome.CreateImageSource(iconId, ToolbarEdit.Foreground);
            }
            AddEditToolbarIcon(icon, markdownActionCommand, toolbar, command);
        }


        /// <summary>
        /// Adds an image icon to the editor toolbar 
        /// </summary>
        /// <param name="icon">An image source icon - should size well for 16px high</param>
        /// <param name="markdownActionCommand">string action from MarkupMarkdow() implementation or wrap with `html|tag`</param>
        /// <param name="toolbar"></param>
        /// <param name="command"></param>
        public void AddEditToolbarIcon(ImageSource icon, string markdownActionCommand, ToolBar toolbar = null,
            ICommand command = null)
        {
            if (toolbar == null) toolbar = ToolbarEdit;
            if (command == null) command = Model.Commands.ToolbarInsertMarkdownCommand;

            var tb = new Button() { Command = command, CommandParameter = markdownActionCommand };
            if (icon == null)
                icon = ImageAwesome.CreateImageSource(FontAwesomeIcon.QuestionCircle, ToolbarEdit.Foreground);

            tb.Content = new Image()
            {
                Source = icon,
                Height = 16,
                Margin = new Thickness(5, 0, 5, 0),
                ToolTip = markdownActionCommand
            };

            var idx = toolbar.Items.IndexOf(ButtonEmoji);
            if (idx > -1)
                toolbar.Items.Insert(idx + 1, tb);
            else
                toolbar.Items.Add(tb);

        }

        private TabItem OpenRecentDocuments()
        {
            ApplicationConfiguration conf = Model.Configuration;
            TabItem selectedTab = null;

            // prevent TabSelectionChanged to fire
            batchTabAction = true;

            // since docs are inserted at the beginning we need to go in reverse
            foreach (var doc in conf.OpenDocuments.Take(mmApp.Configuration.RememberLastDocumentsLength)) //.Reverse();
            {
                if (doc.Filename == null)
                    continue;

                if (File.Exists(doc.Filename))
                {
                    var tab = OpenTab(doc.Filename, selectTab: false,
                        batchOpen: true,
                        initialLineNumber: doc.LastEditorLineNumber);

                    if (tab == null)
                        continue;

                    var editor = tab.Tag as MarkdownDocumentEditor;
                    if (editor == null)
                        continue;

                    if (doc.IsActive)
                    {
                        selectedTab = tab;

                        // have to explicitly notify initial activation
                        // since we surpress it on all tabs during startup

                        AddinManager.Current.RaiseOnDocumentActivated(editor.MarkdownDocument);
                    }
                }
            }

            batchTabAction = false;

            return selectedTab;
        }


        void RestoreSettings()
        {
            var conf = mmApp.Configuration;

            if (conf.WindowPosition.Width != 0)
            {
                Left = conf.WindowPosition.Left;
                Top = conf.WindowPosition.Top;
                Width = conf.WindowPosition.Width;
                Height = conf.WindowPosition.Height;
                WindowUtilities.EnsureWindowIsVisible(this);
            }

            if (conf.WindowPosition.WindowState == WindowState.Maximized)
                Dispatcher.InvokeAsync(() => WindowState = WindowState.Maximized, DispatcherPriority.ApplicationIdle);


            if (mmApp.Configuration.RememberLastDocumentsLength > 0 && mmApp.Configuration.UseSingleWindow)
            {
                //var selectedDoc = conf.RecentDocuments.FirstOrDefault();
                TabItem selectedTab = null;

                string firstDoc = conf.RecentDocuments.FirstOrDefault();


                if (!App.ForceNewWindow)
                    selectedTab = OpenRecentDocuments();

                TabControl.SelectedIndex = -1;
                TabControl.SelectedItem = null;
                if (selectedTab == null)
                    TabControl.SelectedIndex = 0;
                else
                    TabControl.SelectedItem = selectedTab;

                BindTabHeaders();
            }

            Model.IsPreviewBrowserVisible = mmApp.Configuration.IsPreviewVisible;

            ShowFolderBrowser(!mmApp.Configuration.FolderBrowser.Visible);

            // force background so we have a little more contrast
            if (mmApp.Configuration.ApplicationTheme == Themes.Light)
            {
                ContentGrid.Background = (SolidColorBrush) new BrushConverter().ConvertFromString("#eee");
                ToolbarPanelMain.Background = (SolidColorBrush) new BrushConverter().ConvertFromString("#D5DAE8");
            }
            else
                ContentGrid.Background = (SolidColorBrush) new BrushConverter().ConvertFromString("#333");


            //Button Name = "ButtonLink" Margin = "7,0" ToolTip = "Insert link (Ctrl+K)"
            //Command = "{Binding Commands.ToolbarInsertMarkdownCommand }"
            //CommandParameter = "href"
            //fa: Awesome.Content = "ExternalLink"


            //TextElement.FontFamily = "pack://application:,,,/FontAwesome.WPF;component/#FontAwesome"
            //                         />
            foreach (var buttonItem in Model.Configuration.Editor.AdditionalToolbarIcons)
            {
                AddEditToolbarIcon(buttonItem.Key, buttonItem.Value);
            }

        }

        /// <summary>
        /// Save active settings of the UI that are persisted in the configuration
        /// </summary>
        public void SaveSettings()
        {
            var config = mmApp.Configuration;

            if (Model != null)
                config.IsPreviewVisible = Model.IsPreviewBrowserVisible;
            config.WindowPosition.IsTabHeaderPanelVisible = true;

            if (WindowState == WindowState.Normal)
            {
                config.WindowPosition.Left = mmFileUtils.TryConvertToInt32(Left);
                config.WindowPosition.Top = mmFileUtils.TryConvertToInt32(Top);
                config.WindowPosition.Width = mmFileUtils.TryConvertToInt32(Width, 900);
                config.WindowPosition.Height = mmFileUtils.TryConvertToInt32(Height, 700);
            }

            if (WindowState != WindowState.Minimized)
                config.WindowPosition.WindowState = WindowState;


            if (LeftSidebarColumn.Width.Value > 20)
            {
                if (LeftSidebarColumn.Width.IsAbsolute)
                    config.FolderBrowser.WindowWidth =
                        mmFileUtils.TryConvertToInt32(LeftSidebarColumn.Width.Value, 220);
                config.FolderBrowser.Visible = true;
            }
            else
                config.FolderBrowser.Visible = false;

            config.FolderBrowser.FolderPath = FolderBrowser.FolderPath;


            if (!App.ForceNewWindow)
                SaveOpenDocuments();
            else
            {
                // only save if no other instances are open
                if (!Process.GetProcesses()
                    .Any(p => p.ProcessName.Equals("markdownmonster", StringComparison.InvariantCultureIgnoreCase)))
                    SaveOpenDocuments();
            }

            config.Write();
        }


        /// <summary>
        /// Keeps track of the open documents based on the tabs
        /// that are open along with the tab order.
        /// </summary>
        void SaveOpenDocuments()
        {
            var config = Model.Configuration;
            config.OpenDocuments.Clear();
            if (mmApp.Configuration.RememberLastDocumentsLength > 0)
            {
                IEnumerable<DragablzItem> headers = null;
                try
                {
                    var ditems = GetDragablzItems();
                    headers = TabControl.HeaderItemsOrganiser.Sort(ditems);
                }
                catch (Exception ex)
                {
                    mmApp.Log("TabControl.GetOrderedHeaders() failed. Saving unordered.", ex,
                        logLevel: LogLevels.Warning);

                    // This works, but doesn't keep tab order intact
                    headers = new List<DragablzItem>();
                    foreach (var recent in config.RecentDocuments.Take(config.RememberLastDocumentsLength))
                    {
                        var tab = GetTabFromFilename(recent);
                        if (tab != null)
                            ((List<DragablzItem>) headers).Add(new DragablzItem() {Content = tab});
                    }
                }

                if (headers != null)
                {
                    // Important: collect all open tabs in the **original tab order**
                    foreach (var dragablzItem in headers)
                    {
                        if (dragablzItem == null)
                            continue;

                        var tab = dragablzItem.Content as TabItem;

                        var editor = tab.Tag as MarkdownDocumentEditor;
                        var doc = editor?.MarkdownDocument;
                        if (doc == null)
                            continue;

                        doc.LastEditorLineNumber = editor.GetLineNumber();
                        if (doc.LastEditorLineNumber < 1)
                            doc.LastEditorLineNumber = editor.InitialLineNumber; // if document wasn't accessed line is never set
                        if (doc.LastEditorLineNumber < 0)
                            doc.LastEditorLineNumber = 0;

                        config.OpenDocuments.Add(new OpenFileDocument(doc));
                    }
                }

                // now figure out which were recent
                var recents = mmApp.Configuration.RecentDocuments.Take(mmApp.Configuration.RememberLastDocumentsLength)
                    .ToList();

                // remove all those that aren't in the recent list
                var removeList = new List<OpenFileDocument>();
                foreach (var doc in config.OpenDocuments)
                {
                    if (!recents.Any(r => r.Equals(doc.Filename, StringComparison.InvariantCultureIgnoreCase)))
                        removeList.Add(doc);
                }

                foreach (var remove in removeList)
                    config.OpenDocuments.Remove(remove);
            }
        }


        public bool SaveFile(bool secureSave = false)
        {
            var tab = TabControl.SelectedItem as TabItem;
            if (tab == null)
                return false;

            var editor = tab.Tag as MarkdownDocumentEditor;
            var doc = editor?.MarkdownDocument;
            if (doc == null)
                return false;

            // prompt for password on a secure save
            if (secureSave && editor.MarkdownDocument.Password == null)
            {
                var pwdDialog = new FilePasswordDialog(editor.MarkdownDocument, false);
                pwdDialog.ShowDialog();
            }

            if (!editor.SaveDocument())
            {
                //var res = await this.ShowMessageOverlayAsync("Unable to save Document",
                //    "Unable to save document most likely due to missing permissions.");

                MessageBox.Show("Unable to save document most likely due to missing permissions.",
                    mmApp.ApplicationName);
                return false;
            }

            return true;
        }
        #endregion

        #region Remote Activation: Singleton Pipe and Web Server access

        /// <summary>
        /// Internal instance of a local Web Server that can be used to push commands
        /// to Markdown Monster.
        /// </summary>
        public WebServer WebServer = null;
       
      

        private void HandleNamedPipe_OpenRequest(string filesToOpen)
        {
            Dispatcher.Invoke(() =>
            {
                if (!string.IsNullOrEmpty(filesToOpen))
                {
                    var parms = StringUtils.GetLines(filesToOpen.Trim());

                    var opener = new CommandLineOpener(this);
                    opener.OpenFilesFromCommandLine(parms);

                    BindTabHeaders();
                }

                Topmost = true;

                if (WindowState == WindowState.Minimized)
                    WindowState = WindowState.Normal;

                WindowUtilities.SetForegroundWindow(Hwnd);

                Dispatcher.InvokeAsync(() => Topmost = false, DispatcherPriority.ApplicationIdle);
            });
        }

        #endregion

        #region Tab Handling

        /// <summary>
        /// High level wrapper around OpenTab() that checks for
        /// different file types like images and projects that
        /// have non-tab behavior.
        ///
        /// Use this function for generically opening files
        /// by filename .
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="showPreviewIfActive"></param>
        /// <param name="syntax"></param>
        /// <param name="selectTab"></param>
        /// <param name="rebindTabHeaders"></param>
        /// <param name="batchOpen"></param>
        /// <param name="initialLineNumber"></param>
        /// <param name="readOnly"></param>
        /// <param name="noFocus"></param>
        /// <param name="isPreview"></param>
        /// <param name="noShellNavigation">If true last resort editing will open in editor rather than shell execute</param>
        /// <returns>Tab or Null</returns>
        public TabItem OpenFile(string filename,
            MarkdownDocumentEditor editor = null,
            bool showPreviewIfActive = false,
            string syntax = "markdown",
            bool selectTab = true,
            bool rebindTabHeaders = false,
            bool batchOpen = false,
            int initialLineNumber = 0,
            bool readOnly = false,
            bool noFocus = false,
            bool isPreview = false,
            bool noShellNavigation = false)
        {
            if (string.IsNullOrEmpty(filename))
                return null;

            var ext = Path.GetExtension(filename).ToLowerInvariant();
            var fname = Path.GetFileName(filename);

            if (filename.Contains('%'))
            {
                filename = Environment.ExpandEnvironmentVariables(filename);
            }

            if (ext == ".jpg" || ext == ".png" || ext == ".gif" || ext == ".jpeg")
            {
                return OpenBrowserTab(filename, isImageFile: true);
            }

            if (ext == ".mdproj")
            {
                Model.Commands.LoadProjectCommand.Execute(filename);
                return null;
            }

            // Executables - don't execute but open in Explorer
            if (StringUtils.Inlist(ext, ".exe", ".ps1", ".bat", ".cmd", ".vbs", ".sh", ".com", ".reg"))
            {
                ShellUtils.OpenFileInExplorer(filename);
                return null;
            }

            string format = "markdown";

            format = mmFileUtils.GetEditorSyntaxFromFileType(filename);

            // Open a Tab for editable formats
            if (!string.IsNullOrEmpty(format) || noShellNavigation)
            {
                if (string.IsNullOrEmpty(format))
                    format = "markdown";

                if (!noFocus && PreviewTab != null)
                    CloseTab(PreviewTab);

                var tab = ActivateTab(filename, false, false, !showPreviewIfActive,
                    !selectTab, noFocus, readOnly, isPreview);
                if (tab != null)
                {
                    if (initialLineNumber > 0)
                    {
                        if (tab.Tag is MarkdownDocumentEditor ed)
                            Dispatcher.InvokeAsync(() => ed.GotoLine(initialLineNumber), DispatcherPriority.ApplicationIdle);
                    }

                    return tab;
                }

                return OpenTab(filename, editor, showPreviewIfActive, syntax,
                    selectTab, rebindTabHeaders, batchOpen,
                    initialLineNumber, readOnly, noFocus, isPreview);
            }

            try
            {
                if (File.Exists(filename))
                    ShellUtils.GoUrl(filename);
                else
                    ShowStatusError($"Can't open file {filename}. File doesn't exist.");
            }
            catch
            {
                ShowStatusError($"Unable to open file {filename}");
            }

            return null;
        }


        ///  <summary>
        ///  Opens a tab by a filename
        ///  </summary>
        ///  <param name="mdFile"></param>
        ///  <param name="editor"></param>
        ///  <param name="showPreviewIfActive"></param>
        ///  <param name="syntax"></param>
        ///  <param name="selectTab"></param>
        ///  <param name="rebindTabHeaders">
        ///  Rebinds the headers which should be done whenever a new Tab is
        ///  manually opened and added but not when opening in batch.
        ///
        ///  Checks to see if multiple tabs have the same filename open and
        ///  if so displays partial path.
        ///
        ///  New Tabs are opened at the front of the tab list at index 0
        ///  </param>
        /// <param name="batchOpen"></param>
        /// <param name="initialLineNumber"></param>
        /// <param name="readOnly"></param>
        /// <param name="noFocus"></param>
        /// <param name="isPreview"></param>
        /// <returns></returns>
        public TabItem OpenTab(string mdFile = null,
            MarkdownDocumentEditor editor = null,
            bool showPreviewIfActive = false,
            string syntax = "markdown",
            bool selectTab = true,
            bool rebindTabHeaders = false,
            bool batchOpen = false,
            int initialLineNumber = 0,
            bool readOnly = false,
            bool noFocus = false,
            bool isPreview = false)
        {
            if (mdFile != null && mdFile != "untitled" &&
                (!File.Exists(mdFile) ||
                 !AddinManager.Current.RaiseOnBeforeOpenDocument(mdFile)))
                return null;

            var tab = new TabItem();
            tab.Background = Background;


            if (mdFile != null)
            {
                var ext = Path.GetExtension(mdFile).ToLowerInvariant();
                if (ext == ".jpg" || ext == ".png" || ext == ".gif" || ext == ".jpeg")
                {
                    return OpenBrowserTab(mdFile, isImageFile: true);
                }
            }

            HeaderedControlHelper.SetHeaderFontSize(tab, 13f);
            
            if (editor == null)
            {
                editor = new MarkdownDocumentEditor
                {
                    Window = this,
                    InitialLineNumber = initialLineNumber,
                    IsReadOnly = readOnly,
                    NoInitialFocus = noFocus,
                    IsPreview = isPreview
                };

                tab.Content = editor.EditorPreviewPane;
                tab.Tag = editor;


                // tab is temporary until edited
                if (isPreview)
                {
                    if (PreviewTab != null && PreviewTab != tab)
                        TabControl.Items.Remove(PreviewTab);
                    PreviewTab = tab;
                }

                var doc = new MarkdownDocument() {
                    Filename = mdFile ?? "untitled",
                    EditorSyntax = "markdown",
                    Dispatcher = Dispatcher};
                if (doc.Filename != "untitled")
                {
                    doc.Filename = FileUtils.GetPhysicalPath(doc.Filename);

                    if (doc.HasBackupFile())
                    {
                        try
                        {
                            ShowStatusError("Auto-save recovery files have been found and opened in the editor.");
                            {
                                File.Copy(doc.BackupFilename, doc.BackupFilename + ".md");
                                OpenTab(doc.BackupFilename + ".md");
                                File.Delete(doc.BackupFilename + ".md");
                            }
                        }
                        catch (Exception ex)
                        {
                            string msg = "Unable to open backup file: " + doc.BackupFilename + ".md";
                            mmApp.Log(msg, ex);
                            MessageBox.Show(
                                "A backup file was previously saved, but we're unable to open it.\r\n" + msg,
                                "Cannot open backup file",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        }
                    }

                    if (doc.Password == null && doc.IsFileEncrypted())
                    {
                        var pwdDialog = new FilePasswordDialog(doc, true) {Owner = this};
                        bool? pwdResult = pwdDialog.ShowDialog();
                        if (pwdResult == false)
                        {
                            ShowStatus("Encrypted document not opened, due to missing password.",
                                mmApp.Configuration.StatusMessageTimeout);

                            return null;
                        }
                    }


                    if (!doc.Load())
                    {
                        if (!batchOpen)
                        {
                            var msg = "Most likely you don't have access to the file";
                            if (doc.Password != null && doc.IsFileEncrypted())
                                msg = "Invalid password for opening this file";
                            var file = Path.GetFileName(doc.Filename);

                            MessageBox.Show(
                                $"{msg}.\r\n\r\n{file}",
                                "Can't open File", MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        }

                        return null;
                    }
                }

                editor.MarkdownDocument = doc;
                SetTabHeaderBinding(tab, doc, "FilenameWithIndicator");
            }
            else
                tab.Tag = editor;

            var filename = Path.GetFileName(editor.MarkdownDocument.Filename);
            editor.LoadDocument();

            // is the tab already open?
            TabItem existingTab = null;
            if (filename != "untitled")
            {
                foreach (TabItem tb in TabControl.Items)
                {
                    var lEditor = tb.Tag as MarkdownDocumentEditor;
                    if (lEditor == null)
                        continue;

                    if (lEditor.MarkdownDocument.Filename == editor.MarkdownDocument.Filename)
                    {
                        existingTab = tb;
                        break;
                    }
                }
            }

            if (existingTab != null)
                TabControl.Items.Remove(existingTab);

            tab.IsSelected = false;
            if (TabControl.Items.Count > 0)
                TabablzControl.AddItem(tab, TabControl.Items[0], AddLocationHint.First);
            else
                TabControl.Items.Insert(0, tab);


            // Make the tab draggable for moving into bookmarks or anything else that can accept filenames
            // have to drag down - sideways drag re-orders.
            try
            {
                var dragablzItem = GetDragablzItemFromTabItem(tab);
                if (dragablzItem != null)
                {
                    dragablzItem.PreviewMouseMove += DragablzItem_PreviewMouseMove;
                    dragablzItem.PreviewMouseLeftButtonDown += DragablzItem_PreviewMouseLeftButtonDown;
                    //dragablzItem.PreviewGiveFeedback += (object sender, GiveFeedbackEventArgs e) =>
                    //{
                    //    //e.Effects = DragDropEffects.Copy;
                    //    e.UseDefaultCursors = true;
                    //    if (Cursor != Cursors.Cross)
                    //        Mouse.SetCursor(Cursors.Cross);
                    //};
                }
            }
            catch (Exception ex)
            {
                // this failure can be caused if there's a modal dialog popped up when the app starts
                mmApp.Log("Failed to load DragablzItem for tab drag and drop", ex, false, LogLevels.Warning);
            }


            if (selectTab)
            {
                TabControl.SelectedItem = tab;
                SetWindowTitle();
            }


            if (!isPreview)
            {
                Model.OpenDocuments.Add(editor.MarkdownDocument);
                if (!string.IsNullOrEmpty(editor.MarkdownDocument.Filename) &&
                    !editor.MarkdownDocument.Filename.Equals("untitled", StringComparison.InvariantCultureIgnoreCase))
                    Model.Configuration.LastFolder = Path.GetDirectoryName(editor.MarkdownDocument.Filename);

            }

            AddinManager.Current.RaiseOnAfterOpenDocument(editor.MarkdownDocument);


            if (rebindTabHeaders)
                BindTabHeaders();

            // force tabstate bindings to update
            Model.OnPropertyChanged(nameof(AppModel.IsTabOpen));
            Model.OnPropertyChanged(nameof(AppModel.IsNoTabOpen));

            return tab;
        }




        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (batchTabAction)
                return;



            var editor = GetActiveMarkdownEditor();
            if (editor == null)
                return;


            var tab = TabControl.SelectedItem as TabItem;

            SetWindowTitle();

            foreach (var doc in Model.OpenDocuments)
                doc.IsActive = false;

            Model.ActiveDocument = editor.MarkdownDocument;
            Model.ActiveDocument.IsActive = true;

            AddRecentFile(Model.ActiveDocument?.Filename, noConfigWrite: true);

            AddinManager.Current.RaiseOnDocumentActivated(Model.ActiveDocument);

            ((Grid) PreviewBrowserContainer.Parent)?.Children.Remove(PreviewBrowserContainer);
            editor.EditorPreviewPane.PreviewBrowserContainer.Children.Add(PreviewBrowserContainer);

            if (tab.Content is Grid grid)
                grid.Children.Add(PreviewBrowserContainer);


            // handle preview tab closing
            if (PreviewTab != null && tab != PreviewTab)
            {
                if (PreviewTab.Tag == null)
                    CloseTab(PreviewTab); // browser preview
                else
                {
                    // preview Markdown Tab
                    var changedDoc = PreviewTab.Tag as MarkdownDocumentEditor;
                    if (changedDoc != null)
                    {
                        if (changedDoc.MarkdownDocument.IsDirty)
                        {
                            // keep the document open
                            changedDoc.IsPreview = false;
                            PreviewTab = null;
                        }
                        else
                            CloseTab(PreviewTab);
                    }
                }
            }


            Model.WindowLayout.IsPreviewVisible = mmApp.Configuration.IsPreviewVisible;

            if (mmApp.Configuration.IsPreviewVisible)
                PreviewBrowser?.PreviewMarkdown();

            editor.RestyleEditor();
            // Don't automatically set focus - we need to do this explicitly
            //editor.SetEditorFocus();

            Dispatcher.InvokeAsync(() => { UpdateDocumentOutline(); }, DispatcherPriority.ApplicationIdle);
        }


        /// <summary>
        /// Refreshes an already loaded tab with contents of a new (or the same file) file
        /// by just replacing the document's text.
        ///
        /// If the tab is not found a new tab is opened.
        ///
        /// Note: File must already be open for this to work
        /// </summary>
        /// <param name="editorFile">File name to display int the tab</param>
        /// <param name="maintainScrollPosition">If possible preserve scroll position if refreshing</param>
        /// <param name="noPreview">If true don't refresh the preview after updating the file</param>
        /// <param name="noSelectTab"></param>
        /// <param name="noFocus">if true don't focus the editor</param>
        /// <param name="readOnly">if true document can't be edited</param>
        /// <param name="isPreview">Determines whether this tab is treated like a preview tab</param>
        /// <returns>selected tab item or null</returns>
        public TabItem RefreshTabFromFile(string editorFile,
            bool maintainScrollPosition = false,
            bool noPreview = false,
            bool noSelectTab = false,
            bool noFocus = false,
            bool readOnly = false,
            bool isPreview = false)
        {

            var tab = GetTabFromFilename(editorFile);
            if (tab == null)
                return OpenTab(editorFile, rebindTabHeaders: true, readOnly: readOnly, noFocus: noFocus,
                    selectTab: !noSelectTab, isPreview: isPreview);

            // load the underlying document
            var editor = tab.Tag as MarkdownDocumentEditor;
            if (editor == null)
                return null;

            editor.IsPreview = isPreview;
            if (isPreview)
                PreviewTab = tab;
            else
                PreviewTab = null;

            editor.MarkdownDocument.Load(editorFile);

            if (!maintainScrollPosition)
                editor.SetCursorPosition(0, 0);

            editor.SetMarkdown(editor.MarkdownDocument.CurrentText);

            if (!noSelectTab)
                TabControl.SelectedItem = tab;
            if (!noFocus)
                editor.SetEditorFocus();
            editor.IsPreview = isPreview;

            if (!noPreview)
                PreviewMarkdownAsync();

            return tab;
        }

        /// <summary>
        /// Activates a tab from an active tab instance
        /// </summary>
        /// <param name="tab"></param>
        /// <returns></returns>
        public TabItem ActivateTab(TabItem tab, bool setFocus = false)
        {
            TabControl.SelectedItem = tab;

            if (setFocus)
                Dispatcher.InvokeAsync(() => Model.ActiveEditor.SetEditorFocus(), DispatcherPriority.ApplicationIdle);

            return tab;
        }

        /// <summary>
        /// Activates a tab by checking from a filename and activating
        /// or optionally open the a new tab.
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        public TabItem ActivateTab(string filename, bool openIfNotFound = false,
            bool maintainScrollPosition = false,
            bool noPreview = false,
            bool noSelectTab = false,
            bool noFocus = false,
            bool readOnly = false,
            bool isPreview = false)
        {
            var tab = GetTabFromFilename(filename);
            if (tab == null)
                return OpenTab(filename, rebindTabHeaders: true, readOnly: readOnly, noFocus: noFocus,
                    selectTab: !noSelectTab, isPreview: isPreview);

            // load the underlying document
            var editor = tab.Tag as MarkdownDocumentEditor;
            if (editor == null)
                return null;

            editor.IsPreview = isPreview;
            if (isPreview)
                PreviewTab = tab;
            else
                PreviewTab = null;

            if (!maintainScrollPosition)
                editor.SetCursorPosition(0, 0);

            if (!noSelectTab)
                TabControl.SelectedItem = tab;
            if (!noFocus)
                editor.SetEditorFocus();

            if (!noPreview)
                PreviewMarkdownAsync();

            ActivateTab(tab);

            return tab;
        }

        /// <summary>
        /// Opens a preview tab
        /// </summary>
        /// <param name="url"></param>
        /// <param name="selectTab"></param>
        /// <returns></returns>
        public TabItem OpenBrowserTab(string url,
            bool selectTab = true,
            bool isImageFile = false,
            ImageSource icon = null,
            string tabHeaderText = "Preview")
        {

            // if a document preview tab is open close it
            if (PreviewTab?.Tag is MarkdownDocumentEditor)
            {
                var tab = PreviewTab;
                PreviewTab = null;
                CloseTab(tab);
            }

            if (PreviewTab == null)
            {
                PreviewTab = new TabItem();

                var grid = new Grid();
                PreviewTab.Header = grid;
                var col1 = new ColumnDefinition {Width = new GridLength(20)};
                var col2 = new ColumnDefinition {Width = GridLength.Auto};
                grid.ColumnDefinitions.Add(col1);
                grid.ColumnDefinitions.Add(col2);

                if (icon == null)
                {
                    if (isImageFile)
                        icon = FolderStructure.IconList.GetIconFromType("image");
                    else
                        icon = FolderStructure.IconList.GetIconFromType("preview");
                }

                var img = new Image()
                {
                    Source = icon, Height = 16, Margin = new Thickness(0, 1, 5, 0), Name = "IconImage"
                };
                img.SetValue(Grid.ColumnProperty, 0);
                grid.Children.Add(img);


                var textBlock = new TextBlock
                {
                    Name = "HeaderText",
                    Text = tabHeaderText,
                    FontWeight = FontWeights.SemiBold,
                    FontStyle = FontStyles.Italic
                };
                textBlock.SetValue(Grid.ColumnProperty, 1);
                grid.Children.Add(textBlock);

                HeaderedControlHelper.SetHeaderFontSize(PreviewTab, 13F);

                previewBrowser = new IEWebBrowserControl();
                PreviewTab.Content = previewBrowser;
                TabControl.Items.Add(PreviewTab);

                PreviewTab.HorizontalAlignment = HorizontalAlignment.Right;
                PreviewTab.HorizontalContentAlignment = HorizontalAlignment.Right;
            }
            else
            {
                if (icon == null)
                {
                    if (isImageFile)
                        icon = FolderStructure.IconList.GetIconFromType("image");
                    else
                        icon = FolderStructure.IconList.GetIconFromType("preview");
                }


                var grid = PreviewTab.Header as Grid;


                var imgCtrl = grid.Children[0] as Image; //.FindChild<Image>("IconImage");
                imgCtrl.Source = icon;
                var header = grid.Children[1] as TextBlock; // FindChild<TextBlock>("HeaderText");
                header.Text = tabHeaderText;


            }

            PreviewTab.ToolTip = url;

            try
            {

                if (isImageFile)
                {
                    // Image files get a special preview tab that displays an HTML page with image link                    
                    var file = Path.Combine(App.InitialStartDirectory, "PreviewThemes", "ImagePreview.html");
                    string fileInfo = null;

                    try
                    {
                        string filename = Path.GetFileName(url);
                        string fileDimension;
                        using (var bmp = new Bitmap(url))
                        {
                            fileDimension = $"{bmp.Width}x{bmp.Height}";
                        }

                        var fileSize = ((decimal) (new FileInfo(url).Length) / 1000).ToString("N1");
                        fileInfo = $"<b>{filename}</b> - {fileDimension} &nbsp; {fileSize}kb";
                    }
                    catch
                    {
                    }

                    var content = File.ReadAllText(file).Replace("{{imageUrl}}", url).Replace("{{fileInfo}}", fileInfo);
                    File.WriteAllText(file.Replace("ImagePreview.html", "_ImagePreview.html"), content);
                    url = Path.Combine(App.InitialStartDirectory, "PreviewThemes", "_ImagePreview.html");
                }

                previewBrowser.Navigate(url);
            }
            catch
            {
                previewBrowser.Navigate("about: blank");
            }

            if (PreviewTab != null && selectTab)
                TabControl.SelectedItem = PreviewTab;

            // HACK: force refresh of display model
            Model.OnPropertyChanged(nameof(AppModel.IsTabOpen));
            Model.OnPropertyChanged(nameof(AppModel.IsNoTabOpen));

            return PreviewTab;
        }


        /// <summary>
        /// Closes a tab and ask for confirmation if the tab doc
        /// is dirty.
        /// </summary>
        /// <param name="tab"></param>
        /// <param name="rebindTabHeaders">
        /// When true tab headers are rebound to handle duplicate filenames
        /// with path additions.
        /// </param>
        /// <param name="dontPromptForSave"></param>
        /// <returns>true if tab can close, false if it should stay open</returns>
        public bool CloseTab(TabItem tab, bool rebindTabHeaders = true, bool dontPromptForSave = false)
        {
            if (tab == null)
                return false;

            if (tab == PreviewTab)
            {
                tab.Content = null;
                PreviewTab = null;
                TabControl.Items.Remove(tab);

                tab.Tag = null;
                tab = null;
                return true;
            }

            var editor = tab?.Tag as MarkdownDocumentEditor;
            if (editor == null)
                return false;

            bool returnValue = true;

            tab.Padding = new Thickness(200);

            var doc = editor.MarkdownDocument;

            doc.CleanupBackupFile();

            if (doc.IsDirty && !dontPromptForSave)
            {
                var res = MessageBox.Show(this, Path.GetFileName(doc.Filename) + "\r\n\r\nhas been modified.\r\n" +
                                          "Do you want to save changes?",
                    "Save Document",
                    MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
                if (res == MessageBoxResult.Cancel)
                {
                    return false; // don't close
                }

                if (res == MessageBoxResult.No)
                {
                    // close but don't save
                }
                else
                {
                    if (doc.Filename == "untitled")
                        Model.Commands.SaveAsCommand.Execute(ButtonSaveAsFile);
                    else if (!SaveFile())
                        returnValue = false;
                }
            }

            doc.LastEditorLineNumber = editor.GetLineNumber();
            if (doc.LastEditorLineNumber == -1)
                doc.LastEditorLineNumber = 0;


            // *** IMPORTANT: Clean up Tab controls
            editor.ReleaseEditor();
            tab.Tag = null;
            editor = null;
            TabControl.Items.Remove(tab);
            tab = null;

            if (TabControl.Items.Count == 0)
            {
                Model.ActiveDocument = null;
                StatusStats.Text = null;

                TabDocumentOutline.Visibility = Visibility.Collapsed;
                if (SidebarContainer.SelectedItem == TabDocumentOutline)
                    SidebarContainer.SelectedItem = TabFolderBrowser;

                Title = "Markdown Monster" +
                        (UnlockKey.IsUnlocked ? "" : " (unregistered)");
                Model.Window.Focus();
            }

            if (rebindTabHeaders)
                BindTabHeaders();

            Model.OnPropertyChanged(nameof(AppModel.IsTabOpen));
            Model.OnPropertyChanged(nameof(AppModel.IsNoTabOpen));

            return returnValue; // close
        }

        /// <summary>
        /// Closes a tab and ask for confirmation if the tab doc
        /// is dirty.
        /// </summary>
        /// <param name="filename">
        /// The absolute path to the file opened in the tab that
        /// is going to be closed
        /// </param>
        /// <returns>true if tab can close, false if it should stay open or
        /// filename not opened in any tab</returns>
        public bool CloseTab(string filename)
        {
            var tab = GetTabFromFilename(filename);

            if (tab != null)
                return CloseTab(tab);

            return false;
        }

        public bool CloseAllTabs(TabItem allExcept = null)
        {
            batchTabAction = true;
            for (int i = TabControl.Items.Count - 1; i > -1; i--)
            {
                var tab = TabControl.Items[i] as TabItem;

                if (tab != null)
                {
                    if (allExcept != null && tab == allExcept)
                        continue;

                    if (!CloseTab(tab, rebindTabHeaders: false))
                        return false;
                }
            }

            batchTabAction = false;

            return true;
        }


        /// <summary>
        /// Retrieves an open tab based on its filename.
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        public TabItem GetTabFromFilename([CanBeNull] string filename)
        {
            if (string.IsNullOrEmpty(filename))
                return null;

            if (filename == "Preview")
                return PreviewTab;

            TabItem tab = null;
            foreach (TabItem tabItem in TabControl.Items.Cast<TabItem>())
            {
                string tabFile =
                    (tabItem.Tag as MarkdownDocumentEditor)?.MarkdownDocument?.Filename ??
                    (tabItem.ToolTip as string);

                if (tabFile == null)
                    continue;

                if (tabFile.Equals(filename, StringComparison.InvariantCultureIgnoreCase))
                {
                    tab = tabItem;
                    break;
                }
            }

            return tab;
        }

        private void TabControl_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Explicitly force focus into the editor
            // Programmatic tab selection does not automatically set focus
            // unless explicitly specified. Click on a tab explicitly sets focus
            // via this operation.
            Model.ActiveEditor?.SetEditorFocus();
        }

        /// <summary>
        ///  Flag used to let us know we don't want to perform tab selection operations
        /// </summary>
        internal bool batchTabAction = false;

        /// <summary>
        /// Binds all Tab Headers
        /// </summary>
        public void BindTabHeaders()
        {
            var tabList = new List<TabItem>();
            foreach (TabItem tb in TabControl.Items)
                tabList.Add(tb);

            var tabItems = tabList
                .Where(tb => tb.Tag is MarkdownDocumentEditor)
                .Select(tb => Path.GetFileName(((MarkdownDocumentEditor) tb.Tag).MarkdownDocument.Filename.ToLower()))
                .GroupBy(fn => fn)
                .Select(tbCol => new {Filename = tbCol.Key, Count = tbCol.Count()});

            foreach (TabItem tb in TabControl.Items)
            {
                var doc = ((MarkdownDocumentEditor) tb.Tag)?.MarkdownDocument;
                if (doc == null)
                    continue;

                if (tabItems.Any(ti => ti.Filename == Path.GetFileName(doc.Filename.ToLower()) &&
                                       ti.Count > 1))

                    SetTabHeaderBinding(tb, doc, "FilenamePathWithIndicator");
                else
                    SetTabHeaderBinding(tb, doc, "FilenameWithIndicator");
            }
        }

        /// <summary>
        /// Returns a list of DragablzItems
        /// </summary>
        /// <returns></returns>
        public List<DragablzItem> GetDragablzItems()
        {
            // UGLY UGLY Hack but only way to get access to the internal controls of Dragablz
            var control = ReflectionUtils.GetField(TabControl, "_dragablzItemsControl") as DragablzItemsControl;
            if (control == null)
            {
                throw new InvalidOperationException("_dragablzItemsControl is null");
            }

            var ditems = ReflectionUtils.CallMethod(control, "DragablzItems") as List<DragablzItem>;
            return ditems;
        }

        /// <summary>
        /// Returns a DragablzItem from a TabItem
        /// </summary>
        /// <param name="tab"></param>
        /// <returns></returns>
        public DragablzItem GetDragablzItemFromTabItem(TabItem tab)
        {
            var items = GetDragablzItems();
            return items.FirstOrDefault(it => it.Content as TabItem == tab);
        }


        /// <summary>
        /// Binds the tab header to our custom controls/container that
        /// shows a customized tab header
        /// </summary>
        /// <param name="tab"></param>
        /// <param name="document"></param>
        /// <param name="propertyPath"></param>
        private void SetTabHeaderBinding(TabItem tab, MarkdownDocument document,
            string propertyPath = "FilenameWithIndicator",
            ImageSource icon = null)
        {
            if (document == null || tab == null)
                return;
            var editor = tab.Tag as MarkdownDocumentEditor;

            try
            {
                var grid = new Grid();

                tab.Header = grid;
                var col1 = new ColumnDefinition {Width = new GridLength(20)};
                var col2 = new ColumnDefinition {Width = GridLength.Auto};
                grid.ColumnDefinitions.Add(col1);
                grid.ColumnDefinitions.Add(col2);



                if (icon == null)
                {
                    icon = FolderStructure.IconList.GetIconFromFile(document.Filename);
                    if (icon == AssociatedIcons.DefaultIcon && Model.ActiveEditor != null)
                        icon = FolderStructure.IconList.GetIconFromType(Model.ActiveEditor.MarkdownDocument.EditorSyntax);
                }

                var img = new Image() {Source = icon, Height = 16, Margin = new Thickness(0, 1, 5, 0)};
                img.SetValue(Grid.ColumnProperty, 0);
                grid.Children.Add(img);


                var textBlock = new TextBlock();
                textBlock.SetValue(Grid.ColumnProperty, 1);


                var headerBinding = new Binding
                {
                    Source = document, Path = new PropertyPath(propertyPath), Mode = BindingMode.OneWay
                };
                BindingOperations.SetBinding(textBlock, TextBlock.TextProperty, headerBinding);

                var tooltipBinding = new Binding
                {
                    Source = document, Path = new PropertyPath("Filename"), Mode = BindingMode.OneWay
                };
                BindingOperations.SetBinding(tab,TabItem.ToolTipProperty, tooltipBinding);

                var fontStyleBinding = new Binding
                {
                    Source = editor,
                    Path = new PropertyPath("IsPreview"),
                    Mode = BindingMode.OneWay,
                    Converter = new FontStyleFromBoolConverter()
                };
                BindingOperations.SetBinding(textBlock, TextBlock.FontStyleProperty, fontStyleBinding);


                var fontWeightBinding = new Binding
                {
                    Source = tab,
                    Path = new PropertyPath("IsSelected"),
                    Mode = BindingMode.OneWay,
                    Converter = new FontWeightFromBoolConverter()
                };
                BindingOperations.SetBinding(textBlock, TextBlock.FontWeightProperty, fontWeightBinding);

                grid.Children.Add(textBlock);
            }
            catch
            {
                // mmApp.Log("SetTabHeaderBinding Failed. Assigning explicit path", ex);
                tab.Header = document.FilenameWithIndicator;
            }
        }


        private void TabControlDragablz_TabItemClosing(ItemActionCallbackArgs<TabablzControl> e)
        {
            var tab = e.DragablzItem.DataContext as TabItem;
            if (tab == null)
                return;

            if (!CloseTab(tab))
                e.Cancel(); // don't do default tab removal
        }

        /// <summary>
        /// Adds a new panel to the sidebar, and adds header text and icon explicitly.
        /// This overload provides a simpler way to add icon and header
        /// </summary>
        /// <param name="tabItem">Adds the TabItem. If null the tabs are refreshed and tabs removed if down to single tab</param>
        /// <param name="tabHeaderText">Optional - header text to set on the tab either just text or in combination with icon</param>
        /// <param name="tabHeaderIcon">Optional - Icon for the tab as an Image Source</param>
        /// <param name="selectItem"></param>
        public void AddLeftSidebarPanelTabItem(TabItem tabItem,
            string tabHeaderText = null,
            ImageSource tabHeaderIcon = null,
            bool selectItem = true)
        {
            if (tabItem != null)
            {
                // Create the header as Icon and Text
                var panel = new StackPanel();
                panel.Margin = new Thickness(0, 5, 0, 5);

                Image img = null;

                if (tabHeaderIcon != null)
                {
                    img = new Image {Source = tabHeaderIcon, Height = 22, ToolTip = tabHeaderText};
                    //panel.Children.Add(new TextBlock { Text = tabHeaderText });
                }
                else if (!string.IsNullOrEmpty(tabHeaderText))
                {
                    img = new Image
                    {
                        Source = ImageAwesome.CreateImageSource(FontAwesomeIcon.QuestionCircle, Brushes.SteelBlue,
                            22),
                        ToolTip = tabHeaderText
                    };
                }

                panel.Children.Add(img);
                tabItem.Header = panel;

                //ControlsHelper.SetHeaderFontSize(tabItem, 14);
                SidebarContainer.Items.Add(tabItem);

                if (selectItem)
                    SidebarContainer.SelectedItem = tabItem;
            }
        }

        /// <summary>
        /// Adds a new panel to the right sidebar
        /// </summary>
        /// <param name="tabItem">Adds the TabItem. If null the tabs are refreshed and tabs removed if down to single tab</param>
        /// <param name="tabHeaderText"></param>
        /// <param name="tabHeaderIcon"></param>
        /// <param name="selectItem"></param>
        public void AddRightSidebarPanelTabItem(TabItem tabItem = null,
            string tabHeaderText = null,
            ImageSource tabHeaderIcon = null,
            bool selectItem = true)
        {
            if (tabItem != null)
            {
                if (tabHeaderIcon != null)
                {
                    // Create the header as Icon and Text
                    var panel = new StackPanel {Orientation = Orientation.Horizontal};
                    panel.Children.Add(new Image
                    {
                        Source = tabHeaderIcon, Height = 16, Margin = new Thickness(4, 0, 4, 0)
                    });
                    panel.Children.Add(new TextBlock {Text = tabHeaderText});
                    tabItem.Header = panel;

                }
                else if (!string.IsNullOrEmpty(tabHeaderText))
                    tabItem.Header = tabHeaderText;



                HeaderedControlHelper.SetHeaderFontSize(tabItem, 14);
                RightSidebarContainer.Items.Add(tabItem);

                if (selectItem)
                    RightSidebarContainer.SelectedItem = tabItem;
            }

            ShowRightSidebar();
        }


        /// <summary>
        /// Sets the Window Title followed by Markdown Monster (registration status)
        /// by default the filename is used and it's updated whenever tabs are changed.
        ///
        /// Note: ActiveTab change causes the title to be automatically updates and
        /// generally you **don't want to set a custom title** because it'll get
        /// overwritten.
        ///
        /// Just call this when you need to have the title updated due to
        /// file name change that doesn't change the active tab.
        /// </summary>
        /// <param name="title"></param>
        public void SetWindowTitle(string title = null)
        {
            if (title == null)
            {
                var editor = GetActiveMarkdownEditor();
                if (editor == null)
                {
                    Title = "Markdown Monster" +
                             (Model.Configuration.ShowVersionNumberInTitle ? " " + mmApp.GetVersionForDisplay() : string.Empty);
                    return;
                }

                if (Model.Configuration.TitlebarDisplay == TitlebarDisplayModes.FullPath)
                    title = editor.MarkdownDocument.Filename;
                else if(Model.Configuration.TitlebarDisplay == TitlebarDisplayModes.FilenameOnly)
                    title = editor.MarkdownDocument.FilenameWithIndicator.Replace("*", "");
                else if
                    (Model.Configuration.TitlebarDisplay == TitlebarDisplayModes.FileNameAndParentPath)
                    title = editor.MarkdownDocument.FilenamePathWithIndicator.Replace("*", "");

                if (!Model.ActiveProject.IsEmpty)
                    title = title + " • " + Path.GetFileName(Model.ActiveProject.Filename);
            }

            Title = title +
                    "  - Markdown Monster " +
                    (Model.Configuration.ShowVersionNumberInTitle
                        ? mmApp.GetVersionForDisplay() + " " 
                        : "") +
                    (UnlockKey.IsUnlocked ? "" : " (unregistered)");
        }

        /// <summary>
        /// Helper method that sets editor focus
        /// </summary>
        public void SetEditorFocus()
        {
            Dispatcher.Invoke(() => Model.ActiveEditor?.SetEditorFocus());
        }

        private void TabControl_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var context = new TabContextMenu();
            context.ShowContextMenu();
            e.Handled = true;
        }

        private void TabHeader_DoubleClick(object sender, MouseButtonEventArgs ev)
        {
            if (ev.ClickCount == 2)
                OpenTab("untitled");
        }

        #endregion

        #region Tab Drag and Drop

        private System.Windows.Point _dragablzStartPoint;

        private void DragablzItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragablzStartPoint = e.GetPosition(null);
        }

        /// <summary>
        ///  Drag and Drop into the BookMarks dialog
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DragablzItem_PreviewMouseMove(object sender, MouseEventArgs e)
        {

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var curPoint = e.GetPosition(null);
                var diff = curPoint - _dragablzStartPoint;
                if (_dragablzStartPoint.Y > 0 && (diff.Y > 30 || diff.Y < -30))
                {
                    var drag = sender as DragablzItem;
                    if (drag == null)
                        return;
                    var tab = drag.Content as TabItem;

                    var editor = tab.Tag as MarkdownDocumentEditor;
                    if (editor == null)
                        return;

                    //var dragData = new DataObject(DataFormats.UnicodeText, editor.MarkdownDocument.Filename);
                    var files = new[] {editor.MarkdownDocument.Filename};
                    var dragData = new DataObject(DataFormats.FileDrop, files);

                    var fav = FavoritesTab.Content as FavoritesControl;
                    if (fav != null)
                        fav.IsDragging = true; // notify that we're ready to drop on favorites potentially

                    DragDrop.DoDragDrop(drag, dragData, DragDropEffects.Copy);

                    _dragablzStartPoint.Y = 0;
                    _dragablzStartPoint.X = 0;
                }
            }
        }

        #endregion

        #region Document Outline

        private void SidebarContainer_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = SidebarContainer.SelectedItem as TabItem;
            if (selected == null)
                return;

            if (selected.Content is DocumentOutlineSidebarControl)
            {
                Dispatcher.InvokeAsync(() =>
                {
                    if (DocumentOutline.Model?.DocumentOutline == null)
                        UpdateDocumentOutline();
                }, DispatcherPriority.ApplicationIdle);
            }
            else if (selected.Content is FavoritesControl)
                OpenFavorites();
        }

        public void UpdateDocumentOutline(int editorLineNumber = -1)
        {
            DocumentOutline?.RefreshOutline(editorLineNumber);
        }

        public void OpenFavorites(bool noActivate = false)
        {
            if (FavoritesTab == null)
            {
                FavoritesTab = new MetroTabItem();
                FavoritesTab.Content = new FavoritesControl();

                AddLeftSidebarPanelTabItem(FavoritesTab, "Favorite Files and Folders",
                    ImageAwesome.CreateImageSource(FontAwesomeIcon.Star, Brushes.Goldenrod, 11),
                    selectItem: !noActivate);
            }
            else if (!noActivate)
            {
                SidebarContainer.SelectedItem = FavoritesTab;

                Dispatcher.InvokeAsync(() =>
                {
                    var control = FavoritesTab.Content as FavoritesControl;
                    control?.TextSearch.Focus();
                });
            }
        }



        public FileSearchControl OpenSearchPane(bool noActivate = false)
        {
            if (SearchTab == null)
            {
                SearchTab = new MetroTabItem() {Content = new FileSearchControl()};

                AddLeftSidebarPanelTabItem(SearchTab, "File Search",
                    ImageAwesome.CreateImageSource(FontAwesomeIcon.Search, Brushes.SteelBlue, 11), !noActivate);
            }
            else
            {
                SidebarContainer.SelectedItem = SearchTab;

                Dispatcher.InvokeAsync(() =>
                {
                    var control = SearchTab.Content as FileSearchControl;
                    control?.SearchPhrase.Focus();
                });
            }

            return SearchTab.Content as FileSearchControl;
        }

        #endregion


        #region Preview and UI Visibility Helpers

        public void PreviewMarkdown(MarkdownDocumentEditor editor = null, bool keepScrollPosition = false,
            bool showInBrowser = false, string renderedHtml = null)
        {
            PreviewBrowser?.PreviewMarkdown(editor, keepScrollPosition, showInBrowser, renderedHtml);
        }

        public void PreviewMarkdownAsync(MarkdownDocumentEditor editor = null, bool keepScrollPosition = false,
            string renderedHtml = null)
        {
            PreviewBrowser?.PreviewMarkdownAsync(editor, keepScrollPosition, renderedHtml);
        }


        public void Navigate(string url)
        {
            PreviewBrowser?.Navigate(url);
        }


        /// <summary>
        /// Shows or hides the preview browser
        /// </summary>
        /// <param name="hide"></param>
        public void ShowPreviewBrowser(bool hide = false, bool refresh = false)
        {
            var origPreviewVisible = Model.WindowLayout.IsPreviewVisible;

            if (!hide && Model.Configuration.PreviewMode != PreviewModes.None)
            {
                if (Model.Configuration.PreviewMode == PreviewModes.InternalPreview)
                {
                    var origVisible = Model.Configuration.IsPreviewVisible;

                    if (Model.Configuration.IsPreviewVisible)
                    {
                        Model.WindowLayout.IsPreviewVisible = true;
                    }
                    else
                    {
                        Model.WindowLayout.IsPreviewVisible = false;
                        return;
                    }

                    // check if we're already active - if not assign and preview immediately
                    if (!(PreviewBrowser is IPreviewBrowser))
                    {
                        LoadPreviewBrowser();
                        return;
                    }

                    // close external window if it's open
                    if (_previewBrowserWindow != null && PreviewBrowserWindow.Visibility == Visibility.Visible)
                    {
                        PreviewBrowserWindow?.Close();

                        Model.Window.Width += PreviewBrowserWindow.Width - 10;
                        
                        _previewBrowserWindow = null;
                        LoadPreviewBrowser();
                        return;
                    }

                    if (!refresh)
                    {
                        if (Model.Configuration.WindowPosition.SplitterPosition < 100)
                            Model.Configuration.WindowPosition.SplitterPosition = 600;
                    }
                }
                else if (Model.Configuration.PreviewMode == PreviewModes.ExternalPreviewWindow)
                {
                    // make sure it's visible
                    PreviewBrowserWindow?.Show();

                    // check if we're already active - if not assign and preview immediately
                    if (!(PreviewBrowser is PreviewBrowserWindow))
                    {
                        PreviewBrowser = PreviewBrowserWindow;
                        PreviewBrowser?.PreviewMarkdownAsync();
                    }

                    Model.WindowLayout.IsPreviewVisible = false;

                    if (origPreviewVisible)
                        Model.Window.Width -= Model.Configuration.WindowPosition.PreviewWidth - 10;

                    // clear the preview
                    ((IPreviewBrowser) PreviewBrowserContainer.Children[0]).Navigate("about:blank");
                }
            }
            else
            {
                if (Model.Configuration.PreviewMode == PreviewModes.InternalPreview)
                {
                    Model.WindowLayout.IsPreviewVisible = false;
                }
                else if (Model.Configuration.PreviewMode == PreviewModes.ExternalPreviewWindow)
                {
                    if (_previewBrowserWindow != null)
                    {
                        PreviewBrowserWindow?.Close();
                        _previewBrowserWindow = null;
                        PreviewBrowser = null;
                    }
                }

            }
        }

        /// <summary>
        /// Shows or hides the File Browser
        /// </summary>
        /// <param name="hide"></param>
        /// <param name="folder">Folder or File. If File the file will be selected in the folder</param>
        public void ShowFolderBrowser(bool hide = false, string folder = null)
        {
            var layoutModel = Model.WindowLayout;
            if (hide)
            {
                layoutModel.IsLeftSidebarVisible = false;
                mmApp.Configuration.FolderBrowser.Visible = false;
            }
            else
            {
                if (folder == null)
                    folder = FolderBrowser.FolderPath;
                if (folder == null)
                    folder = mmApp.Configuration.FolderBrowser.FolderPath;

                Dispatcher.InvokeAsync(() =>
                {
                    if (string.IsNullOrEmpty(folder) && Model.ActiveDocument != null)
                        folder = Path.GetDirectoryName(Model.ActiveDocument.Filename);

                    FolderBrowser.FolderPath = folder;
                }, DispatcherPriority.ApplicationIdle);

                layoutModel.IsLeftSidebarVisible = true;
                mmApp.Configuration.FolderBrowser.Visible = true;
                SidebarContainer.SelectedIndex = 0; // folder browser tab

                Model.Configuration.LastFolder = folder;
            }
        }

        public void ShowLeftSidebar(bool hide = false)
        {
            if (!hide && SidebarContainer.Items.Count == 1)
            {
                ShowFolderBrowser();
                return;
            }

            Model.WindowLayout.IsLeftSidebarVisible = !hide;
        }

        public void ShowRightSidebar(bool hide = false)
        {
            Model.WindowLayout.IsRightSidebarVisible = !hide;
        }

        /// <summary>
        /// Create an instance of the Preview Browser either using the
        /// default IE based preview browser, or if an addin has registered
        /// a custom preview browser.
        /// </summary>
        public void LoadPreviewBrowser()
        {
            var previewBrowser = AddinManager.Current.RaiseGetPreviewBrowserControl();
            if (previewBrowser == null || PreviewBrowser != previewBrowser)
            {
                if (previewBrowser == null)
                    PreviewBrowser = new IEWebBrowserControl() {Name = "PreviewBrowser"};
                else
                    PreviewBrowser = previewBrowser;

                if (PreviewBrowserContainer == null)
                    PreviewBrowserContainer = new Grid();

                PreviewBrowserContainer.Children.Clear();
                PreviewBrowserContainer.Children.Add(PreviewBrowser as UIElement);

                ShowPreviewBrowser();
            }

            // show or hide
            PreviewMarkdownAsync();
        }

        #endregion

        #region Worker Functions

        public MarkdownDocumentEditor GetActiveMarkdownEditor()
        {
            var tab = TabControl?.SelectedItem as TabItem;
            return tab?.Tag as MarkdownDocumentEditor;
        }


        /// <summary>
        /// Check to see if the window is visible in the bounds of the
        /// virtual screen space. If not adjust to main monitor off 0 position.
        /// </summary>
        /// <returns></returns>
        void FixMonitorPosition()
        {
            var virtualScreenHeight = SystemParameters.VirtualScreenHeight;
            var virtualScreenWidth = SystemParameters.VirtualScreenWidth;


            if (Left > virtualScreenWidth - 150)
                Left = 20;
            if (Top > virtualScreenHeight - 150)
                Top = 20;

            if (Left < SystemParameters.VirtualScreenLeft)
                Left = SystemParameters.VirtualScreenLeft;
            if (Top < SystemParameters.VirtualScreenTop)
                Top = SystemParameters.VirtualScreenTop;

            if (Width > virtualScreenWidth)
                Width = virtualScreenWidth - 40;
            if (Height > virtualScreenHeight)
                Height = virtualScreenHeight - 40;
        }

        #endregion

        #region Button Handlers

        /// <summary>
        /// Generic button handler that handles a number of simple
        /// tasks in a single method to minimize class noise.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public void Button_Handler(object sender, RoutedEventArgs e)
        {
            var button = sender;
            if (button == null)
                return;

            if (button == ButtonOpenFromHtml)
            {
                var fd = new OpenFileDialog
                {
                    DefaultExt = ".htm",
                    Filter = "Html files (*.htm,*.html)|*.htm;*.html|" +
                             "All files (*.*)|*.*",
                    CheckFileExists = true,
                    RestoreDirectory = true,
                    Multiselect = true,
                    Title = "Open Html as Markdown"                    
                };

                if (!string.IsNullOrEmpty(mmApp.Configuration.LastFolder))
                    fd.InitialDirectory = mmApp.Configuration.LastFolder;

                var res = fd.ShowDialog();
                if (res == null || !res.Value)
                    return;

                string html = mmFileUtils.OpenTextFile(fd.FileName);
                if (html == null)
                    return;

                var markdown = MarkdownUtilities.HtmlToMarkdown(html);

                OpenTab("untitled");
                var editor = GetActiveMarkdownEditor();
                editor.MarkdownDocument.CurrentText = markdown;
                PreviewBrowser.PreviewMarkdown();
            }
            else if (button == ToolbarButtonRecentFiles)
            {
                var mi = button as Button;

                var contextMenu = new RecentDocumentsContextMenu(this);
                contextMenu.UpdateRecentDocumentsContextMenu(RecentFileDropdownModes.ToolbarDropdown);
                if (mi.ContextMenu != null)
                    mi.ContextMenu.IsOpen = true;
                e.Handled = true;
            }
            else if (button == ButtonExit)
            {
                Close();
            }

            else if (button == MenuOpenConfigFolder)
            {
                ShellUtils.GoUrl(mmApp.Configuration.CommonFolder);
            }
            else if (button == MenuOpenPreviewFolder)
            {
                ShellUtils.GoUrl(Path.Combine(App.InitialStartDirectory, "PreviewThemes",
                    mmApp.Configuration.PreviewTheme));
            }
            else if (button == MenuMarkdownMonsterSite)
            {
                ShellUtils.GoUrl(mmApp.Urls.WebSiteUrl);
            }
            else if (button == MenuBugReport)
            {
                ShellUtils.GoUrl(mmApp.Urls.SupportUrl);
            }
            else if (button == MenuCheckNewVersion)
            {
                ShowStatus("Checking for new version...");
                if (!ApplicationUpdater.CheckForNewVersion(true, failTimeout: 5000))
                    ShowStatusSuccess("Your version of Markdown Monster is up to date.");
            }
            else if (button == MenuRegister)
            {
                Window rf = new RegistrationForm();
                rf.Owner = this;
                rf.ShowDialog();
            }
            else if (button == ButtonAbout)
            {
                Window about = new About();
                about.Owner = this;
                about.Show();
            }
            else if (button == Button_Find)
            {
                var editor = GetActiveMarkdownEditor();
                if (editor == null)
                    return;
                editor.ExecEditorCommand("find");
            }
            else if (button == Button_FindNext)
            {
                var editor = GetActiveMarkdownEditor();
                if (editor == null)
                    return;
                editor.ExecEditorCommand("findnext");
            }
            else if (button == Button_Replace)
            {
                var editor = GetActiveMarkdownEditor();
                if (editor == null)
                    return;
                editor.ExecEditorCommand("replace");
            }
            else if (button == ButtonWordWrap ||
                     button == ButtonLineNumbers ||
                     button == ButtonShowInvisibles ||
                     button == ButtonCenteredView)
            {
                Model.ActiveEditor?.RestyleEditor();
            }
            else if (button == ButtonStatusEncrypted)
            {
                if (Model.ActiveDocument == null)
                    return;

                var dialog = new FilePasswordDialog(Model.ActiveDocument, false) {Owner = this};
                dialog.ShowDialog();
            }
            else if (button == MenuDocumentation)
                ShellUtils.GoUrl(mmApp.Urls.DocumentationBaseUrl);
            else if (button == MenuMarkdownBasics)
                ShellUtils.GoUrl(mmApp.Urls.DocumentationBaseUrl + "_4ne1eu2cq.htm");
            else if (button == MenuCreateAddinDocumentation)
                ShellUtils.GoUrl(mmApp.Urls.DocumentationBaseUrl + "_4ne0s0qoi.htm");
            else if (button == MenuShowSampleDocument)
                OpenTab(Path.Combine(App.InitialStartDirectory, "SampleMarkdown.md"));
            else if (button == MenuShowErrorLog)
            {
                string logFile = Path.Combine(mmApp.Configuration.CommonFolder, "MarkdownMonsterErrors.txt");
                if (File.Exists(logFile))
                    ShellUtils.GoUrl(logFile);
                else
                    MessageBox.Show("There are no errors in your log file.",
                        mmApp.ApplicationName,
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
            }
            else if (button == MenuResetConfiguration)
            {
                if (MessageBox.Show(
                        "This operation will reset all of your configuration settings and shut down Markdown Monster.\r\n\r\nAre you sure?",
                        "Reset Configuration Settings",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes)
                {
                    mmApp.Configuration.Backup();
                    mmApp.Configuration.Reset();
                }
            }
            else if (button == MenuBackupConfiguration)
            {
                string filename = mmApp.Configuration.Backup();
                ShowStatus($"Configuration backed up to: {Path.GetFileName(filename)}",
                    mmApp.Configuration.StatusMessageTimeout);
                ShellUtils.OpenFileInExplorer(filename);
            }
            else if (button == ButtonAllowScriptTags)
            {
                if (!Model.Configuration.MarkdownOptions.AllowRenderScriptTags &&
                    (Model.Configuration.MarkdownOptions.UseMathematics ||  Model.Configuration.MarkdownOptions.MermaidDiagrams) )
                {
                    if (MessageBox.Show(@"Disabling this option also disables:

  • Mathematics 
  • Mermaid Diagrams

as these options require JavaScript scripts in order to work.

Do you want to continue anyway?", "Disable Markdown Script Rendering",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question,
                            MessageBoxResult.Yes) == MessageBoxResult.No)
                        Model.Configuration.MarkdownOptions.AllowRenderScriptTags = true;
                    else
                    {
                        Model.Configuration.MarkdownOptions.UseMathematics = false;
                        Model.Configuration.MarkdownOptions.MermaidDiagrams = false;
                    }
                }

                // force preview to refresh
                Model.Commands.RefreshPreviewCommand.Execute(null);
            }
        }

        #endregion

        #region Miscelleaneous Events

        /// <summary>
        /// Handle drag and drop of file. Note only works when dropped on the
        /// window - doesn't not work when dropped on the editor.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MainWindow_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[]) e.Data.GetData(DataFormats.FileDrop);

                foreach (var file in files)
                {
                    var ext = Path.GetExtension(file.ToLower());
                    if (File.Exists(file) && ("," +  mmApp.AllowedFileExtensions + ",").Contains($",{ext},"))
                    {
                        OpenTab(file, rebindTabHeaders: true);
                    }
                }
            }
        }

        private void PreviewBrowser_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            //if (e.NewSize.Width > 100)
            //{
            //	int width = Convert.ToInt32(MainWindowPreviewColumn.Width.Value);
            //	if (width > 100)
            //		mmApp.Configuration.WindowPosition.SplitterPosition = width;
            //}
        }

        private void EditorTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            foreach (TabItem tab in TabControl.Items)
            {
                var editor = tab.Tag as MarkdownDocumentEditor;
                editor?.RestyleEditor();
            }

            PreviewBrowser?.PreviewMarkdownAsync();
        }

        private void PreviewTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Model.ActiveEditor?.AceEditor?.SetEditorStyling();
            PreviewBrowser?.PreviewMarkdownAsync();
        }


        //public void AppTheme_MenuButtonClick(object sender, RoutedEventArgs e)
        //{
        //    var button = sender as MenuItem;
        //    var text = button.Header as string;

        //    var selected = (Themes) Enum.Parse(typeof(Themes), text);
        //    var oldVal = mmApp.Configuration.ApplicationTheme;

        //    if (oldVal != selected &&
        //        MessageBox.Show("Application theme changes require that you restart.\r\n\r\nDo you want to restart Markdown Monster?",
        //                        "Theme Change", MessageBoxButton.YesNo,
        //                        MessageBoxImage.Question, MessageBoxResult.Yes) == MessageBoxResult.Yes)
        //    {
        //        mmApp.Configuration.ApplicationTheme = selected;
        //        if (mmApp.Configuration.ApplicationTheme == Themes.Light)
        //        {
        //            mmApp.Configuration.EditorTheme = "vscodelight";
        //            mmApp.Configuration.PreviewTheme = "Github";
        //        }
        //        else
        //            mmApp.Configuration.EditorTheme = "vscodedark";

        //        mmApp.Configuration.Write();

        //        PipeManager.StopServer();
        //        ForceClose = true;
        //        Close();

        //        // execute with delay
        //        ShellUtils.ExecuteProcess(Path.Combine(App.InitialStartDirectory, "MarkdownMonster.exe"), "-delay");
        //        Environment.Exit(0);
        //    }
        //}

        private void DocumentType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Model.ActiveEditor == null)
                return;

            Model.ActiveEditor.SetEditorSyntax(Model.ActiveEditor.MarkdownDocument.EditorSyntax);
            SetTabHeaderBinding(TabControl.SelectedItem as TabItem, Model.ActiveEditor.MarkdownDocument);

            // Refresh the Preview to show preview for html and markdown and hide it for others
            Model.Window.PreviewMarkdownAsync();
        }

        private void ButtonRecentFiles_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            var menu = new RecentDocumentsContextMenu(this);
            menu.UpdateRecentDocumentsContextMenu(RecentFileDropdownModes.MenuDropDown);
        }

        private void LeftSidebarExpand_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Model.Commands.OpenLeftSidebarPanelCommand.Execute(null);
        }

        private void RightSidebarExpand_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Model.WindowLayout.IsRightSidebarVisible = true;
            Model.WindowLayout.RightSidebarWidth = GridLengthHelper.FromInt(300);
        }

        private void MarkdownParserName_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (mmApp.Configuration != null &&
                !string.IsNullOrEmpty(mmApp.Configuration.MarkdownOptions.MarkdownParserName))
            {
                MarkdownParserFactory.GetParser(parserAddinId: mmApp.Configuration.MarkdownOptions.MarkdownParserName,
                    forceLoad: true);
                PreviewBrowser?.PreviewMarkdownAsync();
            }
        }

        private void ButtonLangugeDropDown_Click(object sender, RoutedEventArgs e)
        {
            var context = new LanguagesContextMenu(this);
            context.OpenContextMenu();
        }


        private void ButtonWindowSizesDropdown_Click(object sender, RoutedEventArgs e)
        {
            var menu = new WindowSizesContextMenu(this);
            menu.OpenContextMenu();
        }

        private void Refresh_Styling(object sender, RoutedEventArgs e)
        {
            Model.ActiveEditor?.RestyleEditor();
        }

        #endregion

        #region Window Menu Items
        public List<MenuItem> GenerateContextMenuItemsFromOpenTabs(ContextMenu ctx = null)
        {
            var menuItems = new List<MenuItem>();
            var icons = new AssociatedIcons();
            var selectedTab = TabControl.SelectedItem as TabItem;

            var headers = TabControl.GetOrderedHeaders();
            foreach (var hd in headers)
            {
                var tab = hd.Content as TabItem;

                StackPanel sp;
                string commandParameter;
                var doc = tab.Tag as MarkdownDocumentEditor;

                if (tab == PreviewTab && doc == null)
                {
                    var icon = (tab.Header as Grid).FindChild<Image>("IconImage")?.Source;
                    var txt = (tab.Header as Grid).FindChild<TextBlock>("HeaderText")?.Text;

                    sp = new StackPanel { Orientation = Orientation.Horizontal };
                    sp.Children.Add(new Image
                    {
                        Source = icon,
                        Width = 16,
                        Height = 16,
                        Margin = new Thickness(0, 0, 20, 0)
                    });
                    sp.Children.Add(new TextBlock { Text = txt });
                    commandParameter = "Preview";

                    sp = new StackPanel { Orientation = Orientation.Horizontal };
                    sp.Children.Add(new Image
                    {
                        Source = icon,
                        Width = 16,
                        Height = 16,
                        Margin = new Thickness(0, 0, 20, 0)
                    });
                    sp.Children.Add(new TextBlock { Text = txt });
                    commandParameter = "Preview";
                }
                else
                {
                    if (doc == null) continue;

                    var filename = doc.MarkdownDocument.FilenamePathWithIndicator;
                    var icon = icons.GetIconFromFile(doc.MarkdownDocument.Filename);

                    sp = new StackPanel { Orientation = Orientation.Horizontal };
                    sp.Children.Add(new Image
                    {
                        Source = icon,
                        Width = 16,
                        Height = 16,
                        Margin = new Thickness(0, 0, 20, 0)
                    });
                    sp.Children.Add(new TextBlock { Text = filename });
                    commandParameter = doc.MarkdownDocument.Filename;
                }


                var mi = new MenuItem();
                mi.Header = sp;
                mi.Command = Model.Commands.TabControlFileListCommand;
                mi.CommandParameter = commandParameter;
                if (tab == selectedTab)
                {
                    mi.FontWeight = FontWeights.Bold;
                    mi.Foreground = Brushes.SteelBlue;
                }

                menuItems.Add(mi);
            }

            return menuItems;
        }

        private void MainMenuWindow_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            Model.Commands.WindowMenuCommand.Execute(null);
        }
        #endregion

        #region StatusBar Display

        public void ShowStatus(string message = null, int milliSeconds = 0,
            FontAwesomeIcon icon = FontAwesomeIcon.None,
            Color color = default(Color),
            bool spin = false) => StatusBarHelper.ShowStatus(message, milliSeconds, icon, color, spin);

        /// <summary>
        /// Displays an error message using common defaults for a timeout milliseconds
        /// </summary>
        /// <param name="message">Message to display</param>
        /// <param name="timeout">optional timeout</param>
        /// <param name="icon">optional icon (warning)</param>
        /// <param name="color">optional color (firebrick)</param>
        public void ShowStatusError(string message, int timeout = -1,
            FontAwesomeIcon icon = FontAwesomeIcon.Warning,
            Color color = default(Color)) => StatusBarHelper.ShowStatusError(message, timeout, icon, color);


        /// <summary>
        /// Shows a success message with a green check icon for the timeout
        /// </summary>
        /// <param name="message">Message to display</param>
        /// <param name="timeout">optional timeout</param>
        /// <param name="icon">optional icon (warning)</param>
        /// <param name="color">optional color (firebrick)</param>
        public void ShowStatusSuccess(string message, int timeout = -1,
            FontAwesomeIcon icon = FontAwesomeIcon.CheckCircle,
            Color color = default(Color)) => StatusBarHelper.ShowStatusSuccess(message, timeout, icon, color);


        /// <summary>
        /// Displays an Progress message using common defaults including a spinning icon
        /// </summary>
        /// <param name="message">Message to display</param>
        /// <param name="timeout">optional timeout</param>
        /// <param name="icon">optional icon (warning)</param>
        /// <param name="color">optional color (firebrick)</param>
        /// <param name="spin"></param>
        public void ShowStatusProgress(string message, int timeout = -1,
            FontAwesomeIcon icon = FontAwesomeIcon.CircleOutlineNotch,
            Color color = default(Color),
            bool spin = true) => StatusBarHelper.ShowStatusProgress(message, timeout, icon, color);

        /// <summary>
        /// Status the statusbar icon on the left bottom to some indicator
        /// </summary>
        /// <param name="icon"></param>
        /// <param name="color"></param>
        /// <param name="spin"></param>
        public void SetStatusIcon(FontAwesomeIcon icon, Color color, bool spin = false) => StatusBarHelper.SetStatusIcon(icon, color, spin);

        /// <summary>
        /// Resets the Status bar icon on the left to its default green circle
        /// </summary>
        public void SetStatusIcon() => StatusBarHelper.SetStatusIcon();

        /// <summary>
        /// Helper routine to show a Metro Dialog. Note this dialog popup is fully async!
        /// </summary>
        /// <param name="title"></param>
        /// <param name="message"></param>
        /// <param name="style"></param>
        /// <param name="settings"></param>
        /// <returns></returns>
        public async Task<MessageDialogResult> ShowMessageOverlayAsync(string title, string message,
            MessageDialogStyle style = MessageDialogStyle.Affirmative,
            MetroDialogSettings settings = null)
        {
            return await this.ShowMessageAsync(title, message, style, settings);
        }

        private void StatusZoomLevel_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            mmApp.Configuration.Editor.ZoomLevel = 100;
            Model.ActiveEditor?.RestyleEditor();
        }

        private void StatusZoomLevel_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var text = StatusZoomLevel.Text;
            text = text.Replace("%", "");
            if (int.TryParse(text, out int num))
            {
                Model.Configuration.Editor.ZoomLevel = num;
                Model.ActiveEditor?.RestyleEditor();
            }
        }

        private void StatusZoomLevel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var text = StatusZoomLevel.Text;
            text = text.Replace("%", "");

            if (int.TryParse(text, out int num))
                Model.Configuration.Editor.ZoomLevel = num;

            Model.ActiveEditor?.RestyleEditor();
        }


        private void StatusEncoding_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Model.ActiveDocument == null)
                return;

            var encoding = Model.ActiveDocument.Encoding;

            string enc = StatusEncoding.SelectedItem as string;
            if (enc == null || enc.StartsWith("——"))
            {
                StatusEncoding.SelectedValue = mmFileUtils.GetEncodingName(encoding);
                return;
            }
            
            if (enc.StartsWith("Load additional"))
            {
                Model.EncodingTypes = new ObservableCollection<string>(mmFileUtils.GetEncodingList(true));
                StatusEncoding.SelectedValue = mmFileUtils.GetEncodingName(encoding);

                Dispatcher.InvokeAsync(() => StatusEncoding.IsDropDownOpen = true);
                return;
            }

            encoding = mmFileUtils.GetEncoding(enc);
            if (Model.ActiveDocument.Encoding.Equals(encoding))
            {
                Model.Window.ShowStatusSuccess($"Document already set to {StatusEncoding.SelectedValue} encoding.");
                return;
            }

            if (Model.ActiveDocument.IsDirty &&
                MessageBox.Show(
                    "This document has unsaved changes that have to be saved before the document Encoding can be updated.\n\n" +
                    "Do you want to save changes?",
                    "Document Changes Pending",
                    MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.No)
            {
                Model.Window.ShowStatusError("Encoding has not been updated. Please save your document first.");
                return;
            }

           
            Model.ActiveDocument.Encoding = encoding;

            // otherwise re-open
            if (!Model.ActiveDocument.Load(Model.ActiveDocument.Filename, encoding: encoding))
                Model.Window.ShowStatusError("Couldn't reopen file with the " + encoding.EncodingName + " Encoding.");
            else
            {
                // Save Selection and past new content
                Model.ActiveEditor.ReplaceContent(Model.ActiveDocument.CurrentText);
                Model.ActiveEditor.PreviewMarkdownCallback(true);
                Model.ActiveDocument.IsDirty = true;
            }

            StatusEncoding.SelectedValue = mmFileUtils.GetEncodingName(encoding);
            Model.Window.ShowStatusSuccess($"The document has been reloaded with {StatusEncoding.SelectedValue} encoding.");
        }

        #endregion

        private void TextLinefeedMode_MouseUp(object sender, MouseButtonEventArgs e)
        {
            Model.Commands.SettingsVisualCommand?.Execute("Linefeed");
        }

        private void MainApplicationWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            mmApp.Model.WindowLayout.FixUpEditorSize();
        }

    }

    public class RecentDocumentListItem
    {   
        public string Filename { get; set; }
        public string DisplayFilename { get; set; }
    }


    public enum RecentFileDropdownModes
    {
        ToolbarDropdown,
        MenuDropDown
    }
}
