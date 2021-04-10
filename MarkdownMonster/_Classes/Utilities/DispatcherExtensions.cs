using System;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace MarkdownMonster.Windows
{

    /// <summary>
    /// Extension methods for the System.Windows.Threading.Dispatcher object
    /// that provides an easy way for delayed execution of code.
    /// </summary>
    public static class DispatcherExtensions
    {

        /// <summary>
        /// Dispatcher.Delay Extension method that delay executes 
        /// an action. 
        /// </summary>        
        /// <param name="disp">The Dispatcher instance</param>
        /// <param name="delayMs">milliseconds to delay before executing</param>
        /// <param name="action">Single parm action to perform ; (arg) => {}</param>
        /// <param name="priority">Dispatcher priority to apply after delay</param>
        public static void Delay(this Dispatcher disp,
            int delayMs,
            Action action,
            DispatcherPriority priority = DispatcherPriority.Normal)
        {
            var ignore = Task.Delay(delayMs).ContinueWith((t) =>
            {
                disp.Invoke(action, priority);
            });
        }


        /// <summary>
        /// Dispatcher.Delay Extension method that delay executes 
        /// an action. 
        /// </summary>        
        /// <param name="disp">The Dispatcher instance</param>
        /// <param name="delayMs">milliseconds to delay before executing</param>
        /// <param name="action">Single parm action to perform ; (arg) => {}</param>
        ///<param name="parm">The parameter to pass</param>
        public static void Delay(this Dispatcher disp,
                                 int delayMs,
                                 Action<object> action,
                                 object parm = null,
                                 DispatcherPriority priority = DispatcherPriority.ApplicationIdle)
        {
            var ignore = Task.Delay(delayMs).ContinueWith((t) =>
            {
                disp.Invoke(action, priority, parm);
            });
        }



        ///// <summary>
        ///// Dispatcher.Delay Extension method that delay executes 
        ///// an action. 
        ///// </summary>        
        ///// <param name="disp">The Dispatcher instance</param>
        ///// <param name="delayMs">milliseconds to delay before executing</param>
        ///// <param name="action">Single parm action to perform ; (arg) => {}</param>
        ///// <param name="parm">The parameter to pass</param>
        ///// <param name="priority">optional Dispatcher priority</param>
        //public static void DelayWithPriority(this Dispatcher disp, int delayMs,
        //                  Action<object> action, object parm = null,
        //                  DispatcherPriority priority = DispatcherPriority.ApplicationIdle)
        //{
        //    var ignore = Task.Delay(delayMs).ContinueWith((t) =>
        //    {
        //        disp.BeginInvoke(action, priority, parm);
        //    });

        //}

        /// <summary>
        /// Dispatcher.Delay Extension method that delay executes 
        /// an action. This version awaits both the delay and the
        /// synchronized action
        /// </summary>        
        /// <param name="disp">The Dispatcher instance</param>
        /// <param name="delayMs">milliseconds to delay before executing</param>
        /// <param name="action">Single parm action to perform ; (arg) => {}</param>
        /// <param name="parm">The parameter to pass</param>
        /// <param name="priority">optional Dispatcher priority</param>
        public static async Task DelayAsync(this Dispatcher disp,
            int delayMs,
            Action<object> action,
            object parm = null,
            DispatcherPriority priority = DispatcherPriority.ApplicationIdle)
        {
            await Task.Delay(delayMs).ConfigureAwait(false);
            await disp.BeginInvoke(action, priority, parm);
        }

        /// <summary>
        /// Dispatcher.Delay Extension method that delay executes 
        /// an action. This version awaits both the delay and the
        /// synchonized action
        /// </summary>        
        /// <param name="disp">The Dispatcher instance</param>
        /// <param name="delayMs">milliseconds to delay before executing</param>
        /// <param name="action">Single parm action to perform ; (arg) => {}</param>
        /// <param name="priority">optional Dispatcher priority</param>
        public static async Task DelayAsync(this Dispatcher disp,
            int delayMs,
            Action action, 
            DispatcherPriority priority = DispatcherPriority.ApplicationIdle)
        {
            await Task.Delay(delayMs).ConfigureAwait(false);
            await disp.BeginInvoke(action, priority);
        }
    }
}
