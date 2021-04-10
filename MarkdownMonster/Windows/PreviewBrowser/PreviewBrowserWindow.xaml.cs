﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MahApps.Metro.Controls;
using MarkdownMonster.AddIns;
using MarkdownMonster.Windows.PreviewBrowser;
using Westwind.Utilities;

namespace MarkdownMonster.Windows
{
    /// <summary>
    /// Interaction logic for PreviewBrowserWindow.xaml
    /// </summary>
    public partial class PreviewBrowserWindow : MetroWindow, IPreviewBrowser
    {

        public bool IsClosed { get; set; }

        public AppModel Model { get; set; }

        IPreviewBrowser PreviewBrowser { get; set; }

        public List<KeyValuePair<string, string>> WindowDisplayModes { get; set; } 

        public PreviewBrowserWindow()
        {
            InitializeComponent();

            mmApp.SetThemeWindowOverride(this);

            Model = mmApp.Model;
            DataContext = Model;

            WindowDisplayModes = new List<KeyValuePair<string, string>>();
            WindowDisplayModes.Add(
                new KeyValuePair<string, string>("ActivatedByMainWindow", "Activated by main window"));
            WindowDisplayModes.Add(
                new KeyValuePair<string, string>("AlwaysOnTop", "Always on top"));
            WindowDisplayModes.Add(
                new KeyValuePair<string, string>("ManualActivation", "Manually activated"));

            ComboWindowDisplayModes.ItemsSource = WindowDisplayModes;
            ComboWindowDisplayModes.BorderBrush = Brushes.Silver;

            LoadInternalPreviewBrowser();
        }

        void LoadInternalPreviewBrowser()
        {
            // Allow addins to load their PreviewBrowser control
            PreviewBrowser = AddinManager.Current.RaiseGetPreviewBrowserControl();


            // if not we use the default 
            if (PreviewBrowser == null)
                PreviewBrowser = new IEWebBrowserControl() {Name = "PreviewBrowser"};

            PreviewBrowserContainer.Children.Add(PreviewBrowser as UIElement);

            Dispatcher.InvokeAsync(SetWindowPositionFromConfig);
        }


        public void SetWindowPositionFromConfig()
        {
            var config = mmApp.Model.Configuration.WindowPosition;

            Left = config.PreviewLeft;
            Top = config.PreviewTop;
            Width = config.PreviewWidth;
            Height = config.PreviewHeight;

            FixMonitorPosition();

            Topmost = config.PreviewDisplayMode == PreviewWindowDisplayModes.AlwaysOnTop;
            if (config.PreviewDocked)
                Dispatcher.InvokeAsync(() => AttachDockingBehavior(),
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        protected override void OnClosing(CancelEventArgs e)
        {            
            IsClosed = true;


            // explicitly closed with close button - turn preview 
            bool wasCodeClosed = new StackTrace().GetFrames().FirstOrDefault(x => x.GetMethod() == typeof(Window).GetMethod("Close")) != null;
            if (!wasCodeClosed)
            {
                mmApp.Model.IsPreviewBrowserVisible = false;
            }

            
            var config = mmApp.Model.Configuration.WindowPosition;

            config.PreviewLeft = Convert.ToInt32(Left);
            config.PreviewTop = Convert.ToInt32(Top);
            config.PreviewWidth = Convert.ToInt32(Width);
            config.PreviewHeight = Convert.ToInt32(Height);

            AttachDockingBehavior(true);


            //Model.Window.PreviewBrowser = PreviewBrowser;            
            PreviewBrowserContainer.Children.Clear();
            PreviewBrowser = null;
            Model.Window.PreviewBrowser = null;
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


            if (Left > virtualScreenWidth - 250)
                Left = 20;
            if (Top > virtualScreenHeight - 250)
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

        #region AlwaysOnTop and Docking Behaviors
        public void AttachDockingBehavior(bool turnOn = true)
        {
            if (turnOn)
            {
                Model.Window.LocationChanged += Window_LocationChanged;
                Model.Window.SizeChanged += Window_SizeChanged;
                DockToMainWindow();
                FixMonitorPosition();
            }
            else
            {
                Model.Window.LocationChanged -= Window_LocationChanged;
                Model.Window.SizeChanged -= Window_SizeChanged;
            }
        }


        public void DockToMainWindow()
        {
            Left = Model.Window.Left + Model.Window.Width + 5;
            Top = Model.Window.Top;
            Height = Model.Window.Height;
        }
        

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DockToMainWindow();
        }

        private void Window_LocationChanged(object sender, EventArgs e)
        {
            DockToMainWindow();
        }


        private void CheckPreviewDocked_Click(object sender, RoutedEventArgs e)
        {
            if (Model.Configuration.WindowPosition.PreviewDocked)
                AttachDockingBehavior();
            else
                AttachDockingBehavior(false);
        }

        #endregion

        #region IPreviewBrowser
        public void PreviewMarkdownAsync(MarkdownDocumentEditor editor = null, bool keepScrollPosition = false, string renderedHtml = null, int editorLineNumber = -1)
        {            
            PreviewBrowser.PreviewMarkdownAsync(editor, keepScrollPosition, renderedHtml);
        }

        public void PreviewMarkdown(MarkdownDocumentEditor editor = null, bool keepScrollPosition = false, bool showInBrowser = false, string renderedHtml = null, int editorLineNumber = -1)
        {
            PreviewBrowser.PreviewMarkdown(editor, keepScrollPosition,showInBrowser,renderedHtml);
        }

        
        public void ScrollToEditorLine(int editorLineNumber = -1, bool updateCodeBlocks = false, bool noScrollTimeout = false, bool noScrollTopAdjustment = false)
        {
            PreviewBrowser.ScrollToEditorLine(editorLineNumber, updateCodeBlocks, noScrollTimeout, noScrollTopAdjustment);
        }

        public async Task ScrollToEditorLineAsync(int editorLineNumber = -1, bool updateCodeBlocks = false, bool noScrollTimeout = false, bool noScrollTopAdjustment = false)
        {
            await PreviewBrowser.ScrollToEditorLineAsync(editorLineNumber, updateCodeBlocks, noScrollTimeout, noScrollTopAdjustment);
        }


        public void Navigate(string url)
        {
            PreviewBrowser.Navigate(url);
        }

        public void Refresh(bool noCache = false)
        {
            PreviewBrowser.Refresh(noCache);
            PreviewMarkdownAsync();
        }

        public void ExecuteCommand(string command, params dynamic[] args)
        {
            PreviewBrowser.ExecuteCommand(command, args);
        }

        public void ShowDeveloperTools()
        {
            MessageBox.Show(mmApp.Model.Window,
                "This browser doesn't support Developer tools.", "Developer Tools",
                MessageBoxButton.OK,
                MessageBoxImage.Exclamation);
        }
        #endregion

        public void Dispose()
        {
            PreviewBrowser.Dispose();
        }

        bool firstLoad = true;

        private void ComboWindowDisplayModes_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (firstLoad)
            {
                firstLoad = false;
                return;
            }

            Close();

            // reload the form
            Dispatcher.Invoke(() => Model.Commands.PreviewModesCommand.Execute("ExternalPreviewWindow"),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    }
}
