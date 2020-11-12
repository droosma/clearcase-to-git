using System.Collections.Generic;

namespace GitImporter
{
    public static class Helpers
    {
        public static TC AddToCollection<TK, TC, TV>(this Dictionary<TK, TC> dict, TK key, TV toAdd) where TC : ICollection<TV>, new()
        {
            TC collection;
            if(!dict.TryGetValue(key, out collection))
            {
                collection = new TC();
                dict.Add(key, collection);
            }

            collection.Add(toAdd);
            return collection;
        }

        public static bool RemoveFromCollection<TK, TC, TV>(this Dictionary<TK, TC> dict, TK key, TV toRemove) where TC : ICollection<TV>, new()
        {
            TC collection;
            if(!dict.TryGetValue(key, out collection))
                return false;
            bool removed = collection.Remove(toRemove);
            if(collection.Count == 0)
                dict.Remove(key);
            return removed;
        }

        public static TC AddToCollection<TK, TC, TV>(this List<KeyValuePair<TK, TC>> dict, TK key, TV toAdd) where TC : ICollection<TV>, new()
        {
            TC collection = default;
            foreach(var pair in dict)
                if(pair.Key.Equals(key))
                {
                    collection = pair.Value;
                    break;
                }

            if(Equals(collection, default(TC)))
            {
                collection = new TC();
                dict.Add(new KeyValuePair<TK, TC>(key, collection));
            }

            collection.Add(toAdd);
            return collection;
        }

        public static bool RemoveFromCollection<TK, TC, TV>(this List<KeyValuePair<TK, TC>> dict, TK key, TV toRemove) where TC : ICollection<TV>, new()
        {
            TC collection = default;
            int index;
            for(index = 0;index < dict.Count;index++)
                if(dict[index].Key.Equals(key))
                {
                    collection = dict[index].Value;
                    break;
                }

            if(Equals(collection, default(TC)))
                return false;

            bool removed = collection.Remove(toRemove);
            if(collection.Count == 0)
                dict.RemoveAt(index);
            return removed;
        }
    }
}