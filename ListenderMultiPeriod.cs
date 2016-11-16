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
                            TryCleanUpPolling(id);
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
                TryCleanUpPolling(id);
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

        private void TryCleanUpPolling(string id)
        {
            // Check if we're still holding the id against a polling interval
            // Had there been multiple subscriptions for an id.. the first cleanup would have processed this
            // already, meaning subsequent cleanups are not required.
            if (PollIntervals.Any(search => search.Value.Keys.Contains(id)) == false)
            {
                return;
            }

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
                foreach (var poller in Pollers)
                {
                    poller.Value?.Dispose();
                }
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
        //~ListenerMultiPeriodic()
        //{
        //    Dispose(false);
        //}
        #endregion
    }
}