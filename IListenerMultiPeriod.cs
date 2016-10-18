using System;

namespace BetfairNG
{
    //TODO
    public interface IListenerMultiPeriodComplex<T, F> : IDisposable
    {
        void UpdatePollInterval(string id, double pollInterval);

        IDisposable Subscribe(string id,
                                F filter,
                                double pollIntervalInSeconds,
                                Action<T> onNextAction,
                                Action onCompletedAction,
                                Action<Exception> onErrorAction);
    }

    public interface IListenerMultiPeriod<T> : IDisposable
    {
        void UpdatePollInterval(string id, double pollInterval);

        IDisposable Subscribe(string id, 
                                double pollIntervalInSeconds,
                                Action<T> onNextAction,
                                Action onCompletedAction,
                                Action<Exception> onErrorAction);

        void Force(double pollInterval);
    }
}