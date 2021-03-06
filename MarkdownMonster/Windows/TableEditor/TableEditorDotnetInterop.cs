using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Westwind.Utilities;

namespace MarkdownMonster.Windows
{
    public class TableEditorDotnetInterop : BaseBrowserInterop
    {
       

        public TableEditorDotnetInterop(object instance) : base(instance)
        {
        }

        #region Call into JavaScript from .NET

        public TableData GetJsonTableData()
        {
            string tdata = Invoke("parseTable", true) as string;  // asJson
            if(string.IsNullOrEmpty(tdata))
                return null;

            var td = JsonSerializationUtils.Deserialize<TableData>(tdata);
            return td;
        }

        public void UpdateHtmlTable(TableData data, TableLocation location)
        {
            Invoke("renderTable",
                 BaseBrowserInterop.SerializeObject(data),
                BaseBrowserInterop.SerializeObject(location));
        }
            
        #endregion
    }

    /// <summary>
    /// Class that is called back to from JavaScript - this is passed into the
    /// page by calling `InitializeInterop()` in script (WebBrowser.InvokeScript())
    /// </summary>
    public class TableEditorJavaScriptCallbacks 
    {
        TableEditorHtml Window;

        public TableEditorJavaScriptCallbacks(TableEditorHtml window)
        {
            Window = window;
        }

        /// <summary>
        /// Updates the stored table data. Called whenever the HTML form
        /// loses focus
        /// </summary>
        /// <param name="jsonTable"></param>
        public void UpdateTableData(string jsonTable)
        {
            var td = JsonSerializationUtils.Deserialize<TableData>(jsonTable);
            if (td != null)
                Window.TableData = td;
        }

        /// <summary>
        /// Pops up the context menu
        /// </summary>
        /// <param name="mousePosition"></param>
        public void ShowContextMenu(object mousePosition)
        {
            // get the latest editor table data
            Window.TableData = Window.Interop.GetJsonTableData();

            // incoming row data is: row 0 = header, actual rows 1 based 
            var loc = new TableLocation();
            loc.Row = Convert.ToInt32( ReflectionUtils.GetPropertyCom(mousePosition, "row") );
            loc.Column = Convert.ToInt32( ReflectionUtils.GetPropertyCom(mousePosition, "column") );
            loc.IsHeader = loc.Row < 1;

            // Fix up row number to 0 based
            if (!loc.IsHeader)
                loc.Row--;
            
            var ctx  = new TableEditorContextMenu(Window, loc);
            ctx.ShowContextMenu();
        }


        /// <summary>
        /// Ctrl-Enter callback to force the form to save
        /// </summary>
        public void KeyboardCommand(string command)
        {
            if (command == "Ctrl-Enter")
                Window.Commands.EmbedTableCommand.Execute(null);
        }

        public void RefreshPreview(object pos)
        {
            TableLocation loc = null;
            if (pos != null)
            {
                loc = new TableLocation
                {
                    Column = Convert.ToInt32(ReflectionUtils.GetPropertyExCom(pos, "column")),
                    Row = Convert.ToInt32(ReflectionUtils.GetPropertyExCom(pos, "row"))
                };
            }

            Window.RefreshPreview(false, loc);
        }
    }




    
}
