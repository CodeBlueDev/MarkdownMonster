﻿using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MarkdownMonster;
using MarkdownMonster.Windows.PreviewBrowser;
using UserControl = System.Windows.Controls.UserControl;

namespace WebViewPreviewerAddin
{
    /// <summary>
    /// Interaction logic for ChromiumPreviewControl.xaml
    /// </summary>
    public partial class WebViewPreviewControl : UserControl, IPreviewBrowser
    {

        public WebViewPreviewControl()
        {
            InitializeComponent();

            Model = mmApp.Model;
            Window = Model.Window;
            
            DataContext = Model;

            PreviewBrowser = new WebViewPreviewHandler(WebBrowser);

        }




        public AppModel Model { get; set; }

        public MainWindow Window { get; set; }

        IPreviewBrowser PreviewBrowser { get; set; }

        public void PreviewMarkdownAsync(MarkdownDocumentEditor editor = null, bool keepScrollPosition = false,
            string renderedHtml = null, int editorLineNumber = -1)
        {
            if (editor == null)
            {
                editor = mmApp.Model?.ActiveEditor;
                if (editor == null)
                    return; // not ready
            }
            
            PreviewBrowser?.PreviewMarkdownAsync(editor, keepScrollPosition, renderedHtml);
        }

        public void PreviewMarkdown(MarkdownDocumentEditor editor = null, bool keepScrollPosition = false, bool showInBrowser = false,
            string renderedHtml = null, int editorLineNumber = -1)
        {
            if (editor == null)
            {
                editor = mmApp.Model?.ActiveEditor;
                if (editor == null)
                    return; // not ready
            }

            PreviewBrowser?.PreviewMarkdown(editor, keepScrollPosition, showInBrowser, renderedHtml);
        }

        public void Navigate(string url)
        {
            PreviewBrowser?.Navigate(url);
        }

        public void Refresh(bool forceRefresh)
        {
            PreviewBrowser?.Refresh(forceRefresh);
        }

        public void ExecuteCommand(string command, params object[] args)
        {
            PreviewBrowser?.ExecuteCommand(command, args);
        }

        public void ShowDeveloperTools()
        {
            WebBrowser.CoreWebView2.OpenDevToolsWindow();
        }

        
        public void ScrollToEditorLine(int editorLineNumber = -1, bool updateCodeBlocks = false, bool noScrollContentTimeout = false, bool noScrollTopAdjustment = false)
        {
            PreviewBrowser?.ScrollToEditorLine(editorLineNumber, updateCodeBlocks, noScrollContentTimeout,
                noScrollTopAdjustment);
        }

        public async Task ScrollToEditorLineAsync(int editorLineNumber = -1, bool updateCodeBlocks = false, bool noScrollContentTimeout = false, bool noScrollTopAdjustment = false)
        {
            await PreviewBrowser?.ScrollToEditorLineAsync(editorLineNumber, updateCodeBlocks,
                noScrollContentTimeout,
                noScrollTopAdjustment);
        }

        public void Dispose()
        {
            PreviewBrowser.Dispose();
            PreviewBrowser = null;
        }
        
    }

    public class WebViewControlModel : INotifyPropertyChanged
    {

        public string Url
        {
            get { return _Url; }
            set
            {
                if (value == _Url) return;
                _Url = value;
                OnPropertyChanged(nameof(Url));
            }
        }
        private string _Url = string.Empty;

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
