using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using BetfairNG.Data;

namespace BetfairNG
{
    /// <summary>
    /// This is a wrapper to allow the multi period market listener to call out to
    /// some function that returns a list of MarketBooks.
    /// Typically the intention here is to re play a series of MarketBooks that have
    /// previously been saved to disk
    /// </summary>
    public class MarketListenerPluggable : MarketListenerMultiPeriod
    {
        private readonly Func<IEnumerable<string>, IEnumerable<MarketBook>> _listMarketBook;

        private MarketListenerPluggable(Func<IEnumerable<string>, IEnumerable<MarketBook>> listMarketBook)
        {
            _listMarketBook = listMarketBook;
        }

        public static MarketListenerPluggable Create(Func<IEnumerable<string>, IEnumerable<MarketBook>> listMarketBook)
        {
            return new MarketListenerPluggable(listMarketBook);
        }

        protected sealed override void DoWork(double pollinterval)
        {
            ConcurrentDictionary<string, bool> mpi;
            if (!PollIntervals.TryGetValue(pollinterval, out mpi)) return;

            UpdateObservers(_listMarketBook(mpi.Keys));
        }
    }
}