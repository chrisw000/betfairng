using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using BetfairNG.Data;

namespace BetfairNG
{
    public abstract class MarketListenerMultiPeriod : ListenderMultiPeriod<MarketBook>
    {
        public IObservable<Runner> SubscribeRunner(string marketId, long selectionId, long pollinterval)
        {
            var marketTicks = Subscribe(marketId, pollinterval);

            var observable = Observable.Create<Runner>(
              observer =>
              {
                  var subscription = marketTicks.Subscribe(tick =>
                  {
                      var runner = tick.Runners.First(c => c.SelectionId == selectionId);
                      // attach the book
                      runner.MarketBook = tick;
                      observer.OnNext(runner);
                  });

                  return Disposable.Create(() => subscription.Dispose());
              })
              .Publish()
              .RefCount();

            return observable;
        }

        protected sealed override void UpdateObservers(IEnumerable<MarketBook> marketBooks)
        {
            foreach (var market in marketBooks)
            {
                IObserver<MarketBook> o;
                if (!Observers.TryGetValue(market.MarketId, out o)) continue;

                // check to see if the market is finished
                if (market.Status == MarketStatus.CLOSED ||
                    market.Status == MarketStatus.INACTIVE)
                    o.OnCompleted();
                else
                    o.OnNext(market);
            }
        }
    }
}
