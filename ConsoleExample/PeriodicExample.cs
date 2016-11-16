using System;
using System.Collections.Concurrent;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using BetfairNG;
using BetfairNG.Data;

namespace ConsoleExample
{
    public class PeriodicExample : IDisposable
    {
        private readonly ConcurrentQueue<MarketCatalogue> _markets = new ConcurrentQueue<MarketCatalogue>();
        private readonly MarketListenerPeriodic _marketListener;

        private IDisposable _marketSubscription;
        public bool IsBlocking => false;

        public PeriodicExample(BetfairClient client, double pollIntervalInSeconds)
        {
            var betfairClient = client;

            var marketCatalogues = betfairClient.ListMarketCatalogue(
                BFHelpers.HorseRaceFilter("GB"),
                BFHelpers.HorseRaceProjection(),
                MarketSort.FIRST_TO_START,
                25).Result.Response;

            marketCatalogues.ForEach(c =>
            {
                _markets.Enqueue(c);
            });

            _marketListener = MarketListenerPeriodic.Create(betfairClient
                , BFHelpers.HorseRacePriceProjection()
                ,pollIntervalInSeconds);
        }

        public void Go()
        {
            MarketCatalogue marketCatalogue;
            _markets.TryDequeue(out marketCatalogue);

            _marketSubscription = _marketListener.SubscribeMarketBook(marketCatalogue.MarketId)
                .SubscribeOn(Scheduler.Default)
                .Subscribe(
                    tick =>
                    {
                        Console.WriteLine(BFHelpers.MarketBookConsole(marketCatalogue, tick, marketCatalogue.Runners));
                    },
                    () =>
                    {
                        Console.WriteLine("Market finished");
                    });
        }

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
                _marketSubscription?.Dispose();
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
        //~PeriodicExample()
        //{
        //    Dispose(false);
        //}
        #endregion
    }
}
