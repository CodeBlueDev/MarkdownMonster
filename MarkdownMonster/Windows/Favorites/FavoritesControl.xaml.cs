﻿using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MarkdownMonster.Controls;
using MarkdownMonster.Favorites;
using Microsoft.Win32;


namespace MarkdownMonster.Windows
{
    /// <summary>
    /// Interaction logic for Favorites.xaml
    /// </summary>
    public partial class FavoritesControl : UserControl
    {

        public FavoritesModel FavoritesModel { get; set; }

        public FavoritesControl()
        {
            InitializeComponent();

            FavoritesModel = new FavoritesModel() {AppModel = mmApp.Model, Window = mmApp.Model.Window};
            FavoritesModel.LoadFavorites();

            DataContext = FavoritesModel;
        }


        void StartEditing(FavoriteItem favorite, bool stopEditing = false)
        {

            if (stopEditing)
            {
                FavoritesModel.EditedFavorite = new FavoriteItem();
            }
            else
            {
                FavoritesModel.EditedFavorite = favorite;
                favorite.DisplayState.IsEditing = true;
                TextFavoriteTitle.Focus();
            }
            
        }

        #region Button Event Handlers

        

        private void ButtonFavorite_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var favorite = button?.DataContext as FavoriteItem;
            if (favorite == null)
                return;

            if (favorite.IsFolder)
            {
                favorite.IsExpanded = !favorite.IsExpanded;
                return;
            }

            if (Directory.Exists(favorite.File))
                FavoritesModel.AppModel.Commands.OpenFolderBrowserCommand.Execute(favorite.File);
            else
            {
                var tab = FavoritesModel.AppModel.Window.GetTabFromFilename(favorite.File);
                if (tab == null)
                    FavoritesModel.AppModel.Window.OpenFile(favorite.File, isPreview: true);
                else
                    FavoritesModel.AppModel.Window.ActivateTab(tab);
            }
        }


        private void ButtonFavorite_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var button = sender as Button;
            var favorite = button?.DataContext as FavoriteItem;
            if (favorite == null)
                return;

            Dispatcher.InvokeAsync(() =>
            {
                if (!Directory.Exists(favorite.File))
                {
                   FavoritesModel.AppModel.Window.OpenFile(favorite.File, isPreview: false, noFocus: false);
                }
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        }

        private void ButtonAddFavorite_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as MenuItem;
            var favorite = button?.DataContext as FavoriteItem;
            
            favorite = FavoritesModel.AddFavorite(favorite);
            FavoritesModel.SaveFavorites();

            StartEditing(favorite);
        }

        private void ButtonAddFolder_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as MenuItem;
            var favorite = button?.DataContext as FavoriteItem;

            favorite = FavoritesModel.AddFavorite(favorite, new FavoriteItem { Title = "Grouping",  IsFolder = true});
            FavoritesModel.SaveFavorites();

            StartEditing(favorite);
        }

        private void ButtonDeleteFavorite_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as MenuItem;
            var favorite = button?.DataContext as FavoriteItem;
            if (favorite == null)
                return;

            FavoritesModel.DeleteFavorite(favorite);
        }

        private void ButtonEditComplete_Click(object sender, RoutedEventArgs e)
        {
            var favorite = FavoritesModel.EditedFavorite;
            if (favorite == null)
                return;

            favorite.DisplayState.IsEditing = false;

            var parentList = favorite.Parent?.Items;
            if (parentList == null)
                parentList = FavoritesModel.Favorites;

            // convoluted 
            var idx = parentList.IndexOf(favorite);
            if (idx == -1)
                idx = 0;
            parentList.Remove(favorite);
            parentList.Insert(idx,favorite);

            FavoritesModel.SaveFavorites();

            FavoritesModel.EditedFavorite = null;
        }

        private void ButtonCancelFavorite_Click(object sender, RoutedEventArgs e)
        {
            StartEditing(null, true);
        }

        private void ButtonStartEditing_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as MenuItem;
            var favorite = button?.DataContext as FavoriteItem;
            if (favorite == null)
                return;

            StartEditing(favorite);
        }

        private void ButtonRefreshList_Click(object sender, RoutedEventArgs e)
        {
            FavoritesModel.LoadFavorites();
        }

        private void ButtonEditList_Click(object sender, RoutedEventArgs e)
        {
            FavoritesModel.AppModel.Window.OpenTab(FavoritesModel.FavoritesFile);
        }

        private void ButtonFileSelection_Click(object sender, RoutedEventArgs e)
        {
            string folder = mmApp.Configuration.LastFolder;
            if (FavoritesModel.EditedFavorite?.File != null)
                folder = System.IO.Path.GetDirectoryName(FavoritesModel.EditedFavorite.File);

            var dlg = new OpenFileDialog();

            dlg.Title = "Select a file for this Favorite";
            dlg.InitialDirectory = folder;
            dlg.RestoreDirectory = true;
            dlg.CheckPathExists = true;
            dlg.CheckPathExists = true;
            
            bool? result = dlg.ShowDialog();
            if (result == null || !result.Value)
                return;
            
            FavoritesModel.EditedFavorite.File = dlg.FileName;
        }

        private void ButtonOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as MenuItem;
            if (button == null)
                return;

            var favorite = button.DataContext as FavoriteItem;
            if (favorite == null)
                return;

            FavoritesModel.AppModel.Commands.OpenInExplorerCommand.Execute(favorite.File);
        }

        #endregion

        #region Drag Operations

        private System.Windows.Point startPoint;
        public bool IsDragging { get; set; } 

        private void TreeFavorites_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var item = ElementHelper.FindVisualTreeParent<TreeViewItem>(e.OriginalSource as FrameworkElement);
            if (item != null)
                item.IsSelected = true;

            startPoint = e.GetPosition(null);
            IsDragging = false;
        }

    

        private void TreeFavorites_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var selected = TreeFavorites.SelectedItem as FavoriteItem;
                if (selected == null)
                    return;

                var mousePos = e.GetPosition(null);
                var diff = startPoint - mousePos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance
                    || Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    var treeView = sender as TreeView;
                    var treeViewItem = WindowUtilities.FindAnchestor<TreeViewItem>((DependencyObject)e.OriginalSource);
                    if (treeView == null || treeViewItem == null)
                        return;

                    var dragData = new DataObject(DataFormats.UnicodeText, selected.File + "|" + selected.Title);
                    DragDrop.DoDragDrop(treeViewItem, dragData, DragDropEffects.Copy);
                    IsDragging = true;
                }
            }
        }

        private void TreeViewItem_Drop(object sender, DragEventArgs e)
        {   
            if (sender is TreeView)
            {
                // dropped into treeview open space
                return; //targetItem = ActivePathItem;
            }

            string rawFilename = null;

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var tkens = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (tkens == null)
                {
                    return;
                }
                rawFilename  = tkens[0];
            }
            IsDragging = false;

            FavoriteItem targetItem;            
            e.Handled = true;

            targetItem = (e.OriginalSource as FrameworkElement)?.DataContext as FavoriteItem;
            if (targetItem == null)
                return;

            string path = rawFilename;
            if (string.IsNullOrEmpty(rawFilename))
                //  "path|title"
                path = e.Data.GetData(DataFormats.UnicodeText) as string;
            
            if (string.IsNullOrEmpty(path))
                return;

            FavoriteItem sourceItem = null;
            ObservableCollection<FavoriteItem> parentList = null;
            
            var tokens = path.Split('|');
            if (tokens.Length == 1)
            {
                // just a filename
                var newItem = new FavoriteItem
                {
                    File = path,
                    Title = System.IO.Path.GetFileName(path)
                };

                sourceItem =
                    FavoritesModel.FindFavoriteByFilenameAndTitle(FavoritesModel.Favorites, newItem.Filename, newItem.File);
                if (sourceItem == null)
                    sourceItem = newItem;
            }
            else
            {
                sourceItem =
                    FavoritesModel.FindFavoriteByFilenameAndTitle(FavoritesModel.Favorites, tokens[0], tokens[1]);
            }

            if (sourceItem == null)
                    return;

                parentList = sourceItem.Parent?.Items;
                if (parentList == null)
                    parentList = FavoritesModel.Favorites;
                parentList.Remove(sourceItem);
                parentList = null;
            


            if (targetItem.IsFolder && !sourceItem.IsFolder)
            {
                // dropped on folder: Add below
                parentList = targetItem.Items;
                sourceItem.Parent = targetItem;
            }
            else
            {
                // Dropped on file: Add after
                parentList = targetItem.Parent?.Items;
                if (parentList == null)
                {
                    parentList = FavoritesModel.Favorites;
                    sourceItem.Parent = null;
                }
                else
                    sourceItem.Parent = targetItem.Parent;
            }
            
            var index = parentList.IndexOf(targetItem);
            if (index < 0)
                index = 0;
            else
                index++;

            if(index >= parentList.Count)
                parentList.Add(sourceItem);
            else
                parentList.Insert(index,sourceItem);

            FavoritesModel.SaveFavorites();
            WindowUtilities.DoEvents();
        }

        #endregion

        private void TextSearch_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            FavoritesModel.LoadFavorites();            
        }

        private void TreeViewItem_Expanded(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            FavoritesModel.SaveFavoritesAsync();            
        }


        private void TreeFavorites_OnKeyDown(object sender, KeyEventArgs e)
        {
            var favorite = TreeFavorites.SelectedItem as FavoriteItem;
            if (favorite == null)
                return;

            if (e.Key == Key.Delete)
            {
                FavoritesModel.DeleteFavorite(favorite);
            }
            if (e.Key == Key.F2)
            {
                StartEditing(favorite);
            }
            if(e.Key == Key.N && Keyboard.IsKeyDown(Key.LeftCtrl))
            {
                favorite = FavoritesModel.AddFavorite(favorite);
                FavoritesModel.SaveFavorites();
                StartEditing(favorite);
            }
        }
    }



}
