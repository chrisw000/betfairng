using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        private readonly Action<System.Exception, string> _logger;

        private MarketListenerLiveData(BetfairClient client,
            PriceProjection priceProjection,
            OrderProjection? orderProjection,
            MatchProjection? matchProjection,
            Action<System.Exception, string> logger)
        {
            _client = client;
            _priceProjection = priceProjection;
            _orderProjection = orderProjection;
            _matchProjection = matchProjection;
            _logger = logger;
        }

        public static MarketListenerLiveData Create(BetfairClient client,
            PriceProjection priceProjection,
            OrderProjection? orderProjection = null,
            MatchProjection? matchProjection = null,
            Action<System.Exception, string> logger = null)
        {
            return new MarketListenerLiveData(client, priceProjection, orderProjection, matchProjection, logger);
        }

        protected override void DoWork(double pollinterval)
        {
            ConcurrentDictionary<string, bool> bag;
            if (!PollIntervals.TryGetValue(pollinterval, out bag)) return;

            BetfairServerResponse<List<MarketBook>> book;
            try
            {
                book = _client.ListMarketBook(bag.Keys, _priceProjection, _orderProjection, _matchProjection).Result;
            }
            catch (AggregateException ex)
            {
                foreach (var e in ex.Flatten().InnerExceptions)
                {
                    _logger.Invoke(e, $"pollInterval {pollinterval}");
                }
                
                return;
            }

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