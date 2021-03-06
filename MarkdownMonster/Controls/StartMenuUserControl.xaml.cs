using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace MarkdownMonster.Controls
{
    /// <summary>
    /// Interaction logic for StartMenuUserControl.xaml
    /// </summary>
    public partial class StartMenuUserControl : UserControl
    {
        public StartMenuUserControl()
        {
            InitializeComponent();

            if (mmApp.Configuration.ApplicationTheme == Themes.Dark)
                HyperSwitchAppTheme.Inlines.Add("Switch to Light theme");
            else
                HyperSwitchAppTheme.Inlines.Add("Switch to Dark theme");

            mmApp.Configuration.CleanupRecentFilesAndFolders();
        }

        private void HyperSwitchAppTheme_Click(object sender, RoutedEventArgs e)
        {
            // toggle theme
            Themes theme = mmApp.Configuration.ApplicationTheme = Themes.Dark;
            if (mmApp.Configuration.ApplicationTheme == Themes.Dark)
                theme = Themes.Light;
            
            mmApp.Model.Commands.SwitchThemeCommand.Execute(theme.ToString());

            HyperSwitchAppTheme.Inlines.Clear();
            if (mmApp.Configuration.ApplicationTheme == Themes.Dark)
                HyperSwitchAppTheme.Inlines.Add("Switch to Light theme");
            else
                HyperSwitchAppTheme.Inlines.Add("Switch to Dark theme");
        }
    }
    
}
