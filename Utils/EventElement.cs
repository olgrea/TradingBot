using System;

namespace TradingBot.Utils
{
    internal class EventElement<T1, T2>
    {
        event Action<T1, T2> _event;

        public bool HasSubscribers => _event != null;

        public void Invoke(T1 val1, T2 val2)
        {
            _event?.Invoke(val1, val2);
        }

        public static EventElement<T1, T2> operator +(EventElement<T1, T2> element, Action<T1, T2> callback)
        {
            element._event += callback;
            return element;
        }

        public static EventElement<T1, T2> operator -(EventElement<T1, T2> element, Action<T1, T2> callback)
        {
            element._event -= callback;
            return element;
        }

        public void Clear()
        {
            _event = null;
        }
    }
}
