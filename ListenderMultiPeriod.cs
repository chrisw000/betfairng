using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace BetfairNG
{
    /// <summary>
    /// Abstraction to allow a generic implementation of multi period polling subscription
    /// </summary>
    /// <typeparam name="T">The type of object being observed</typeparam>
    public abstract class ListenderMultiPeriod<T> : IListenerMultiPeriod<T>
    {
        protected readonly object LockObj = new object();

        private readonly ConcurrentDictionary<string, IObservable<T>> _observables =
            new ConcurrentDictionary<string, IObservable<T>>();

        protected readonly ConcurrentDictionary<string, IObserver<T>> Observers =
            new ConcurrentDictionary<string, IObserver<T>>();

        protected readonly ConcurrentDictionary<double, ConcurrentDictionary<string, bool>> PollIntervals =
            new ConcurrentDictionary<double, ConcurrentDictionary<string, bool>>();

        protected readonly ConcurrentDictionary<double, Poller> Pollers =
            new ConcurrentDictionary<double, Poller>();

        /// <summary>
        /// Setup the subscription for the given id
        /// </summary>
        /// <param name="id">The id of the observable to subscribe to</param>
        /// <param name="pollIntervalInSeconds">The polling interval in seconds</param>
        /// <returns></returns>
        public IObservable<T> Subscribe(string id, double pollIntervalInSeconds)
        {
            IObservable<T> t;
            if (_observables.TryGetValue(id, out t))
                return t;

            SetupPolling(id, pollIntervalInSeconds);

            var observable = Observable.Create<T>(
                    observer =>
                    {
                        Observers.AddOrUpdate(id, observer, (key, existingVal) => existingVal);
                        return Disposable.Create(() =>
                        {
                            IObserver<T> ob;
                            IObservable<T> o;
                            _observables.TryRemove(id, out o);
                            Observers.TryRemove(id, out ob);
                      
                            CleanUpPolling(id);
                        });
                    })
                .Publish()
                .RefCount();

            _observables.AddOrUpdate(id, observable, (key, existingVal) => existingVal);
            return observable;
        }

        /// <summary>
        /// Setup the subscription for the given id
        /// </summary>
        /// <param name="id">The id of the observable to subscribe to</param>
        /// <param name="pollIntervalInSeconds">The polling interval in seconds</param>
        /// <param name="onNextAction">The method to call OnNext subscribed T</param>
        /// <param name="onCompletedAction">The method to call OnCompleted subscribed T</param>
        /// <param name="onErrorAction">The method to call OnError of subscribed T</param>
        /// <returns>This subscription MUST be Disposed when done with it</returns>
        public IDisposable Subscribe(string id,
                                    double pollIntervalInSeconds,
                                    Action<T> onNextAction,
                                    Action onCompletedAction,
                                    Action<Exception> onErrorAction)
        {
            var subscription = Subscribe(id, pollIntervalInSeconds)
                .SubscribeOn(NewThreadScheduler.Default)
                .Subscribe(
                    onNext: onNextAction,
                    onCompleted: onCompletedAction,
                    onError: onErrorAction
                );

            return subscription;
        }

        /// <summary>
        /// Allow external forces to Force the DoWork
        /// </summary>
        /// <param name="pollInterval"></param>
        public void Force(double pollInterval) => DoWork(pollInterval);

        /// <summary>
        /// Override to perform the work required per polling interval
        /// </summary>
        /// <param name="pollinterval"></param>
        protected abstract void DoWork(double pollinterval);

        /// <summary>
        /// Override to perform the communication back to the observers,
        /// should be called from DoWork(pollinterval)
        /// </summary>
        /// <param name="observables">The new data to be communicated out</param>
        protected abstract void UpdateObservers(IEnumerable<T> observables);

        /// <summary>
        /// Change the polling interval for the observers with the given id
        /// </summary>
        /// <param name="id">The id of the observers</param>
        /// <param name="newPollIntervalInSeconds">New poll interval, in seconds</param>
        public void UpdatePollInterval(string id, double newPollIntervalInSeconds)
        {
            if (!_observables.Keys.Contains(id)) return;

            lock (LockObj)
            {
                // First, remove the existing entry
                CleanUpPolling(id);
                // Now put this id into the new interval
                SetupPolling(id, newPollIntervalInSeconds);
            }
        }

        private void SetupPolling(string id, double pollIntervalInSeconds)
        {
            // Keep the poll interval reasonable... Betfair docs say listMarketBook should be called max 5 times per second, per market
            if (pollIntervalInSeconds < 0.2) pollIntervalInSeconds = 0.2;

            ConcurrentDictionary<string, bool> idsForPollInterval;
            if (PollIntervals.TryGetValue(pollIntervalInSeconds, out idsForPollInterval))
            {
                idsForPollInterval.TryAdd(id, false);
            }
            else
            {
                idsForPollInterval = new ConcurrentDictionary<string, bool>();
                idsForPollInterval.TryAdd(id, false);

                PollIntervals.TryAdd(pollIntervalInSeconds, idsForPollInterval);
                Pollers.TryAdd(pollIntervalInSeconds, new Poller(
                    Observable.Interval(TimeSpan.FromSeconds(pollIntervalInSeconds), NewThreadScheduler.Default)
                        .Subscribe(
                            onNext: l => DoWork(pollIntervalInSeconds)
                            //, onCompleted: TODO: do I need some clean up here?
                        )));
            }
        }

        private void CleanUpPolling(string id)
        {
            // Find the interval that the id is now running under
            var interval = PollIntervals.First(search => search.Value.Keys.Contains(id)).Key;

            ConcurrentDictionary<string, bool> cdPollInterval;
            if (PollIntervals.TryGetValue(interval, out cdPollInterval))
            {
                bool entryForThisId;
                cdPollInterval.TryRemove(id, out entryForThisId); // Remove id

                if (!cdPollInterval.IsEmpty) return;

                // All the markets have gone for this interval, so clean the interval + polling up as well
                ConcurrentDictionary<string, bool> piEntry;
                if (PollIntervals.TryRemove(interval, out piEntry))
                {
                    Poller poller;
                    if (Pollers.TryRemove(interval, out poller))
                    {
                        poller.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// Clean up the Pollers
        /// </summary>
        public void Dispose()
        {
            foreach (var poller in Pollers)
            {
                poller.Value?.Dispose();
            }
        }
    }
}