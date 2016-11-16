using System;
using System.Collections.Concurrent;
using System.Configuration;
using System.IO;
using System.Threading;
using BetfairNG;
using BetfairNG.Data;

//using Betfair.ESAClient.Cache;
//using Betfair.ESAClient.Protocol;

// This example pulls the latest horse races in the UK markets
// and displays them to the console.
namespace ConsoleExample
{
    public class ConsoleExample
    {
        public static ConcurrentQueue<MarketCatalogue> Markets = new ConcurrentQueue<MarketCatalogue>();

        public static void Main()
        {
            BetfairClient client;

            //TODO: see chat re TLS1.0 vs 2.0 bug + urls: #FRAMEWORK?
            if (File.Exists("App.config"))
            {
                client = new BetfairClient(ConfigurationManager.AppSettings["bfAppKey"]);
                client.Login(ConfigurationManager.AppSettings["logOnCertFile"],
                    SecureStringManager.Unprotect("logOnCertFilePassword", true),
                    SecureStringManager.Unprotect("bfUsername", true),
                    SecureStringManager.Unprotect("bfPassword", true));
            }
            else
            {
                // TODO:// replace with your app details and Betfair username/password
                client = new BetfairClient("APPKEY");
                client.Login(@"client-2048.p12", "certpass", "username", "password");
            }
        
            /*
         * OriginalExample runs the code originally in here, using the standard MarketListener
         * PeriodicExample runs a version of MarketListener (MarketListenerPeriodic), using an RX interval, specified in seconds
         * MultiPeriodExample runs a version of MarketListenerPeriodic (MarketListenerMultiPeriod), using potentially differing poll intervals per market book
         */

            var example = new OriginalExample(client); // This example blocks within GO
            //var example = new StreamingExample(client, streamingClient); // Betfair Exchange Streaming API example
            //var example = new PeriodicExample(client, 0.5);
            //var example = new MultiPeriodExample(client);
            example.Go();

            if(!example.IsBlocking) Thread.Sleep(TimeSpan.FromMinutes(20));

            Console.WriteLine("done.");
            Console.ReadLine();
        }
    }
}
