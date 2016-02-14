using LcdHD44780;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.Foundation;

namespace LcdHD44780Demo
{
    class RssDemo
    {

        public async Task RunAsync()
        {
            //write a static string
            DriverHD44780.Instance.DrawString(
                "Getting RSS...",
                new Point(0, 0)
                );

            //get the latest news using a normal HTTP GET request
            var http = new HttpClient();
            var endpoint = new Uri("http://feeds.bbci.co.uk/news/rss.xml");

            var srss = await http.GetStringAsync(endpoint);
            var xrss = XDocument.Parse(srss);

            //extract the news items, and sort them by date-time descending
            var xnews = xrss.Root
                .Element("channel")
                .Elements("item")
                .OrderByDescending(_ => (DateTime)_.Element("pubDate"))
                .ToList();

            int n = 0;
            while (true)
            {
                /**
                * Loop the news as one per page
                **/

                //the first row is for the publication date-time
                var dt = (DateTime)xnews[n].Element("pubDate");
                DriverHD44780.Instance.DrawString(
                    dt.ToString("g"),
                    new Point(0, 0)
                    );

                //the three other rows are for the title
                var title = (string)xnews[n].Element("title");
                title = title + new string(' ', 60);

                for (int row = 0; row < 3; row++)
                {
                    DriverHD44780.Instance.DrawString(
                        title.Substring(row * 20, 20),
                        new Point(0, row + 1)
                        );
                }

                //wait some seconds before flipping page
                n = (n + 1) % xnews.Count;
                await Task.Delay(3000);
            }
        }

    }
}
