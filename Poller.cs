using System;

namespace BetfairNG
{
    /// <summary>
    /// Use to control the polling timings for the Multi Period listeners
    /// </summary>
    public class Poller : IDisposable
    {
        private readonly IDisposable _poller;
        internal DateTime LatestDataRequestStart = DateTime.Now;
        internal DateTime LatestDataRequestFinish = DateTime.Now;

        public Poller(IDisposable poller)
        {
            _poller = poller;
        }
        public void Dispose()
        {
            _poller.Dispose();
        }
    }
}