namespace Broker.Utils
{
    public class LinkedListWithMaxSize<T> : LinkedList<T>
    {
        int _capacity;
        public LinkedListWithMaxSize(int capacity)
        {
            _capacity = capacity;
        }

        public LinkedListWithMaxSize(int capacity, IEnumerable<T> values)
            : this(capacity)
        {
            foreach (T value in values)
                AddLast(value);
        }

        public new void AddLast(T value)
        {
            base.AddLast(value);
            if (Count > _capacity)
                RemoveFirst();
        }

        public new void AddLast(LinkedListNode<T> node)
        {
            base.AddLast(node);
            if (Count > _capacity)
                RemoveFirst();
        }

        public new void AddAfter(LinkedListNode<T> node, T value)
        {
            base.AddAfter(node, value);
            if (Count > _capacity)
                RemoveFirst();
        }

        public new void AddAfter(LinkedListNode<T> node, LinkedListNode<T> newNode)
        {
            base.AddAfter(node, newNode);
            if (Count > _capacity)
                RemoveFirst();
        }

        public new void AddBefore(LinkedListNode<T> node, T value)
        {
            base.AddBefore(node, value);
            if (Count > _capacity)
                RemoveFirst();
        }

        public new void AddBefore(LinkedListNode<T> node, LinkedListNode<T> newNode)
        {
            base.AddBefore(node, newNode);
            if (Count > _capacity)
                RemoveFirst();
        }

        public new void AddFirst(T value)
        {
            base.AddFirst(value);
            if (Count > _capacity)
                RemoveFirst();
        }

        public new void AddFirst(LinkedListNode<T> node)
        {
            base.AddFirst(node);
            if (Count > _capacity)
                RemoveFirst();
        }
    }

    public static class LinkedListUtils
    {
        public static IEnumerable<T> ToLinkedList<T>(this IEnumerable<T> enumerable)
        {
            return new LinkedList<T>(enumerable);
        }
    }
}
