using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace QuerySight.Extension
{
    /// <summary>
    /// This class exposes the tool window, which contains the QuerySight control.
    /// </summary>
    [Guid("d304a37f-5cfd-487b-a010-85f02bc7cbef")]
    public class QuerySightToolWindow : ToolWindowPane
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="QuerySightToolWindow"/> class.
        /// </summary>
        public QuerySightToolWindow() : base(null)
        {
            this.Caption = "QuerySight SQL Chart";
            
            // Set the WPF control as the content of the ToolWindowPane
            this.Content = new QuerySightToolWindowControl();
        }
    }
}
