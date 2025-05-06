using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FunkAssetBundles
{

    public static class ListE
    {
        public static List<T> Clone<T>(this List<T> list)
        {
            var newList = new List<T>(list.Count);

            for (var i = 0; i < list.Count; ++i)
            {
                newList.Add(list[i]);
            }

            return newList;
        }

        public static List<T> ShuffleClone<T>(this List<T> list)
        {
            var listCopy = list.Clone();
            var shuffledList = new List<T>(list.Count);

            while (listCopy.Count > 0)
            {
                var listEntryIndex = listCopy.RandomIndex();
                var listEntry = listCopy[listEntryIndex];
                shuffledList.Add(listEntry);

                listCopy.RemoveAt(listEntryIndex);
            }

            return shuffledList;
        }

        public static void FillN<T>(this List<T> self, int count, T value)
        {
            for (int i = 0; i < count; ++i)
                self.Add(value);
        }

        public static void ClearFillN<T>(this List<T> self, int count, T value)
        {
            self.Clear();
            for (int i = 0; i < count; ++i)
                self.Add(value);
        }

        public static void FillNDefault<T>(this List<T> self, int count)
            where T : new()
        {
            for (int i = 0; i < count; ++i)
                self.Add(new T());
        }

        public static void StackLimitN<T>(this List<T> self, int count)
        {
            while (self.Count > count)
                self.RemoveAt(self.Count - 1);
        }

        public static void StackLimitNReverse<T>(this List<T> self, int count)
        {
            while (self.Count > count)
                self.RemoveAt(0);
        }

        public static T RandomEntry<T>(this List<T> self)
        {
            return self[Random.Range(0, self.Count)];
        }

        public static T RandomEntrySafe<T>(this List<T> self)
            where T : class
        {
            if (self.Count <= 0)
                return (T)null;

            return self[Random.Range(0, self.Count)];
        }

        public static T RandomEntrySafeStruct<T>(this List<T> self)
            where T : struct
        {
            if (self.Count <= 0)
                return default;

            return self[Random.Range(0, self.Count)];
        }

        public static int RandomIndex<T>(this List<T> self)
        {
            return Random.Range(0, self.Count);
        }

        public static void SwapWithEnd<T>(this List<T> self, int index)
        {
            var item = self[index];
            self.RemoveAt(index);
            self.Add(item);
        }

        public static void Append<T>(this List<T> self, List<T> append)
        {
            foreach (var other in append)
            {
                self.Add(other);
            }
        }

        public static void Append<T>(this List<T> self, T[] append)
        {
            foreach (var other in append)
            {
                self.Add(other);
            }
        }

        public static void AddUnique<T>(this List<T> self, T item) where T : struct
        {
            if(!self.Contains(item))
            {
                self.Add(item); 
            }
        }
    }
}
