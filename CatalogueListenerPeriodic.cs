﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using BetfairNG.Data;

namespace BetfairNG
{
    public class CatalogueFilter
    {
        public string FilterId => GetHashCode().ToString();

        public MarketFilter MarketFilter
        {
            get
            {
                _baseMarketFilter.MarketStartTime = new TimeRange()
                {
                    From = DateTime.Now.AddHours(_startOffsetHours),
                    To = DateTime.Now.AddHours(_endOffsetHours)
                };

                return _baseMarketFilter;
            }
        }

        public ISet<MarketProjection> Projection { get; set; }
        //public int Period { get; set; } = 3600; // 1 hour TODO: add when changing CatalogueListener to multi period
        public int MaxResult { get; set; } = 25;
        public MarketSort? MarketSort { get; set; } = Data.MarketSort.FIRST_TO_START;

        private MarketFilter _baseMarketFilter;
        private double _startOffsetHours;
        private double _endOffsetHours;

        private CatalogueFilter()
        {
            // Hide this
        }

        public CatalogueFilter(MarketFilter baseMarketFilter)
        {
            _startOffsetHours = baseMarketFilter.MarketStartTime.From.Subtract(DateTime.Now).TotalHours;
            _endOffsetHours = baseMarketFilter.MarketStartTime.To.Subtract(DateTime.Now).TotalHours;

            _baseMarketFilter = baseMarketFilter;
        }

        public static CatalogueFilter HorseRacing
                    => new CatalogueFilter(BFHelpers.HorseRaceFilter())
                                    {
                                        Projection = BFHelpers.HorseRaceProjection(),
                                        MaxResult = 10
                                        //,Period = 60 // TODO this isnt hooked up yet
                                    };
    }

    public class CatalogueListenerPeriodic : IDisposable
    {
        private readonly BetfairClient _client;

        //TODO check this as this time is per request, but we're looking at multiple Market Filter items (marketlistener requests all the market ids in one go)
        private DateTime _latestDataRequestStart = DateTime.Now;
        private DateTime _latestDataRequestFinish = DateTime.Now;

        private readonly object _lockObj = new object();

        private readonly ConcurrentDictionary<string, CatalogueFilter> _filters =
            new ConcurrentDictionary<string, CatalogueFilter>();

        private readonly ConcurrentDictionary<string, IObservable<List<MarketCatalogue>>> _catalogues =
            new ConcurrentDictionary<string, IObservable<List<MarketCatalogue>>>();

        // the observer is the dispatcher per filter, which t
        private readonly ConcurrentDictionary<string, IObserver<List<MarketCatalogue>>> _observers =
            new ConcurrentDictionary<string, IObserver<List<MarketCatalogue>>>();

        private readonly IDisposable _polling;

        private CatalogueListenerPeriodic(BetfairClient client,
            double periodInSec)
        {
            _client = client;
            _polling = Observable.Interval(TimeSpan.FromSeconds(periodInSec),
                                          NewThreadScheduler.Default).Subscribe(l => DoWork());
        }

        public static CatalogueListenerPeriodic Create(BetfairClient client,
            double periodInSec)
        {
            return new CatalogueListenerPeriodic(client, periodInSec);
        }

        public IObservable<List<MarketCatalogue>> SubscribeFilter(CatalogueFilter filter)
        {
            IObservable<List<MarketCatalogue>> lookup;
            if (_catalogues.TryGetValue(filter.FilterId, out lookup))
                return lookup;

            var observable = Observable.Create<List<MarketCatalogue>>(
               (IObserver<List<MarketCatalogue>> observer) =>
               {
                   _observers.AddOrUpdate(filter.FilterId, observer, (key, existingVal) => existingVal);
                   return Disposable.Create(() =>
                   {
                           CatalogueFilter f;
                           IObserver<List<MarketCatalogue>> ob;
                           IObservable<List<MarketCatalogue>> o;
                           _filters.TryRemove(filter.FilterId, out f);
                           _catalogues.TryRemove(filter.FilterId, out o);
                           _observers.TryRemove(filter.FilterId, out ob);
                       });
               })
               .Publish()
               .RefCount();

            _filters.AddOrUpdate(filter.FilterId, filter, (key, existingVal) => existingVal);
            _catalogues.AddOrUpdate(filter.FilterId, observable, (key, existingVal) => existingVal);
            return observable;
        }

        public void Force() => DoWork();


        private void DoWork()
        {
            // TODO: ideally need to change this so it polls once per filter key
            // IE 1 Poller per filter.Key - similar to the multi poller in MarketListenerMultiPeriod
            foreach (var key in _filters.Keys)
            {
                DoWorkInner(key, _filters[key]);
            }
        }
        private void DoWorkInner(string key, CatalogueFilter cf)
        { 

        var book = _client.ListMarketCatalogue(
                    cf.MarketFilter, 
                    cf.Projection,
                    cf.MarketSort,
                    cf.MaxResult).Result;

            if (book.HasError)
            {
                foreach (var observer in _observers)
                    observer.Value.OnError(book.Error);
                return;
            }

            // we may have fresher data than the response to this request
            if (book.RequestStart < _latestDataRequestStart && book.LastByte > _latestDataRequestFinish)
                return;

            //TODO: locking here is per MarketFilter request... but we do multiples requests; 1 per CatalogueFilter... 
            lock (_lockObj)
            {
                _latestDataRequestStart = book.RequestStart;
                _latestDataRequestFinish = book.LastByte;
            }

            IObserver<List<MarketCatalogue>> o;
            if (!_observers.TryGetValue(key, out o)) return;

            //TODO: would we ever need to call OnCompleted... don't think so?
            //if (someCondition)
            // o.OnCompleted();
            // else
                o.OnNext(book.Response);

        }

        public void Dispose()
        {
            _polling?.Dispose();
        }
    }
}
