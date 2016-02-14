using LcdHD44780;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Foundation;

namespace LcdHD44780Demo
{
    class BasicDemo
    {

        public async Task RunAsync()
        {
            //write a static string
            DriverHD44780.Instance.DrawString(
                "This is a basic demo",
                new Point(0, 0)
                );

            int n = 0;
            while (true)
            {
                //display a simple counter
                DriverHD44780.Instance.DrawString(
                    $"Counting...{n}",
                    new Point(0, 1)
                    );

                //display current time and date
                var now = DateTime.Now;
                DriverHD44780.Instance.DrawString(
                    now.ToString("T") + "   ",
                    new Point(0, 2)
                    );

                DriverHD44780.Instance.DrawString(
                    now.ToString("M") + "   ",
                    new Point(0, 3)
                    );

                n++;
                await Task.Delay(1000);
            }
        }

    }
}
