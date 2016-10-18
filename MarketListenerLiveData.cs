using System.Collections.Concurrent;
using System.Linq;
using BetfairNG.Data;

namespace BetfairNG
{
    /// <summary>
    /// Market Listener which calls the Betfair Client to get live data.
    /// </summary>
    public class MarketListenerLiveData : MarketListenerMultiPeriod
    {
        private readonly OrderProjection? _orderProjection;
        private readonly MatchProjection? _matchProjection;
        private readonly PriceProjection _priceProjection;
        private readonly BetfairClient _client;

        private MarketListenerLiveData(BetfairClient client,
            PriceProjection priceProjection,
            OrderProjection? orderProjection,
            MatchProjection? matchProjection)
        {
            _client = client;
            _priceProjection = priceProjection;
            _orderProjection = orderProjection;
            _matchProjection = matchProjection;
        }

        public static MarketListenerLiveData Create(BetfairClient client,
            PriceProjection priceProjection,
            OrderProjection? orderProjection = null,
            MatchProjection? matchProjection = null)
        {
            return new MarketListenerLiveData(client, priceProjection, orderProjection, matchProjection);
        }

        protected override void DoWork(double pollinterval)
        {
            ConcurrentDictionary<string, bool> bag;
            if (!PollIntervals.TryGetValue(pollinterval, out bag)) return;

            var book = _client.ListMarketBook(bag.Keys, _priceProjection, _orderProjection, _matchProjection).Result;

            if (book.HasError)
            {
                foreach (var observer in Observers.Where(k => bag.Keys.Contains(k.Key)))
                {
                    observer.Value.OnError(book.Error);
                }
                return;
            }

            // we may have fresher data than the response to this pollinterval request
            Poller p;
            if (!Pollers.TryGetValue(pollinterval, out p)) return;

            if (book.RequestStart < p.LatestDataRequestStart && book.LastByte > p.LatestDataRequestFinish)
                return;

            lock (LockObj)
            {
                p.LatestDataRequestStart = book.RequestStart;
                p.LatestDataRequestFinish = book.LastByte;
            }

            UpdateObservers(book.Response);
        }
    }
}