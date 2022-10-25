
using System.Collections.Generic;

namespace TradingBot.Utils
{
    public class LinkedListWithMaxSize<T> : LinkedList<T>
    {
        int _maxSize;
        public LinkedListWithMaxSize(int maxSize)
        {
            _maxSize = maxSize;
        }

        public void Add(T item)
        {
            AddLast(item);
            if (Count > _maxSize)
                RemoveFirst();
        }
    }
}
