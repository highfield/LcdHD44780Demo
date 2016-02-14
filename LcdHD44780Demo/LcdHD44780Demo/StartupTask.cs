using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Http;
using Windows.ApplicationModel.Background;
using LcdHD44780;

// The Background Application template is documented at http://go.microsoft.com/fwlink/?LinkID=533884&clcid=0x409

namespace LcdHD44780Demo
{
    public sealed class StartupTask : IBackgroundTask
    {
        //see: https://msdn.microsoft.com/en-us/library/windows.applicationmodel.background.backgroundtaskdeferral.aspx
        public async void Run(IBackgroundTaskInstance taskInstance)
        {
            //
            // Create the deferral by requesting it from the task instance.
            //
            BackgroundTaskDeferral deferral = taskInstance.GetDeferral();

            //
            // Call asynchronous method(s) using the await keyword.
            //

            //init/start the LCD driver
            await DriverHD44780.Instance.StartAsync(DriverHD44780.Layout20x4);

            //uncomment the desired demo to run
            //var demo = new BasicDemo();
            var demo = new RssDemo();
            await demo.RunAsync();

            //
            // Once the asynchronous method(s) are done, close the deferral.
            //
            deferral.Complete();
        }
    }
}
