﻿using System;
using System.Collections.Concurrent;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Timers;
using BetfairNG;
using BetfairNG.Data;

namespace ConsoleExample
{
    public class MultiPeriodExample : IDisposable
    {
        private readonly BetfairClient _client;
        private readonly ConcurrentQueue<MarketCatalogue> _markets = new ConcurrentQueue<MarketCatalogue>();

        private MarketListenerMultiPeriod _listener;

        private IDisposable _marketSubscription1;
        private IDisposable _marketSubscription2;

        private string _id1;
        private string _id2;

        private Timer _aTimer;

        public MultiPeriodExample(BetfairClient client)
        {
            _client = client;
        }

        public bool IsBlocking => false;

        public void Go()
        {
            var marketCatalogues = _client.ListMarketCatalogue(
                BFHelpers.HorseRaceFilter("GB"),
                BFHelpers.HorseRaceProjection(),
                MarketSort.FIRST_TO_START,
                25).Result.Response;

            marketCatalogues.ForEach(c =>
            {
                _markets.Enqueue(c);
                Console.WriteLine(c.MarketName);
            });
            Console.WriteLine();

            _listener = MarketListenerLiveData.Create(_client, BFHelpers.HorseRacePriceProjection());

            MarketCatalogue marketCatalogue1;
            MarketCatalogue marketCatalogue2;
            // Assume these just work...
            _markets.TryDequeue(out marketCatalogue1);
            _markets.TryDequeue(out marketCatalogue2);

            // Save the market ids so we can change the poll interval later
            _id1 = marketCatalogue1.MarketId;
            _id2 = marketCatalogue2.MarketId;

            // Red, every 1 second
            _marketSubscription1 = _listener.Subscribe(_id1, 1)
                .SubscribeOn(Scheduler.Default)
                .Subscribe(
                    marketBook =>
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine(marketBook.MarketId);
                        //Console.WriteLine(BFHelpers.MarketBookConsole(marketCatalogue1, marketBook, marketCatalogue1.Runners));
                        //Console.WriteLine();
                    },
                    () =>
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("Market finished");
                    }
                );

            // Blue, every 2.5 second
            _marketSubscription2 = _listener.Subscribe(_id2, 2.5)
                .SubscribeOn(Scheduler.Default)
                .Subscribe(
                    marketBook =>
                    {
                        Console.ForegroundColor = ConsoleColor.Blue;
                        Console.WriteLine(marketBook.MarketId);
                        //Console.WriteLine(BFHelpers.MarketBookConsole(marketCatalogue2, marketBook, marketCatalogue2.Runners));
                        //Console.WriteLine();
                    },
                    () =>
                    {
                        Console.ForegroundColor = ConsoleColor.Blue;
                        Console.WriteLine("Market finished");
                    }
                );

            // Now setup a timer so that periodically we swap over the timings of the markets...
            // this will keep going until the Markets close
            _aTimer = new Timer(TimeSpan.FromSeconds(20).TotalMilliseconds); // every 20000 milliseconds change the poll interval for the markets
            // Hook up the Elapsed event for the timer.
            _aTimer.Elapsed += OnTimedEvent;
            _aTimer.Enabled = true;
        }

        private void OnTimedEvent(object source, ElapsedEventArgs e)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Flip Flop");

            if (_flipFlop)
            {
                _listener.UpdatePollInterval(_id1, 2.5);
                _listener.UpdatePollInterval(_id2, 1);
            }
            else
            {
                _listener.UpdatePollInterval(_id1, 1);
                _listener.UpdatePollInterval(_id2, 2.5);
            }
            _flipFlop = !_flipFlop;
        }

        private bool _flipFlop = true;

        #region Dispose
        // http://stackoverflow.com/a/31016954/3744570
        private bool _disposed;
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        protected virtual void Dispose(bool disposing)
        {
            // Check to see if Dispose has already been called. 
            if (_disposed) return;

            // Dispose all managed resources. 
            if (disposing)
            {
                // Dispose managed resources.
                _marketSubscription1?.Dispose();
                _marketSubscription2?.Dispose();
            }

            // Dispose all unmanaged resources. If anything goes here - uncomment the finalizer
            // ... 

            // Note disposing has been done.
            _disposed = true;
        }

        // https://msdn.microsoft.com/en-us/library/ms244737.aspx?f=255&MSPPError=-2147217396
        // NOTE: Leave out the finalizer altogether if this class doesn't   
        // own unmanaged resources itself, but leave the other methods  
        // exactly as they are.   
        //~MultiPeriodExample()
        //{
        //    Dispose(false);
        //}
        #endregion
    }
}