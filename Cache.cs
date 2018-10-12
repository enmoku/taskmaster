//
// Cache.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2018 M.A.
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using Serilog;

namespace Taskmaster
{
	// TODO: Turn K2 into a delegate

	struct CacheItem<K1, K2, T> where T : class where K2 : class
	{
		public K1 AccessKey;
		public K2 ReturnKey;
		public T Item;
		public DateTime Access;
		public long Desirability;
	}

	sealed public class Cache<K1, K2, T> : IDisposable where T : class where K2 : class
	{
		public enum EvictStrategy
		{
			LeastUsed,
			LeastRecent
		};

		EvictStrategy CacheEvictStrategy = EvictStrategy.LeastRecent;

		public enum StoreStrategy
		{
			ReplaceAlways,
			ReplaceNoMatch,
			Fail,
		};

		StoreStrategy CacheStoreStrategy = StoreStrategy.ReplaceNoMatch;

		readonly Dictionary<K1, CacheItem<K1, K2, T>> Items = new Dictionary<K1, CacheItem<K1, K2, T>>();

		public long Count => Items.Count;
		public long Hits { get; private set; } = 0;
		public long Misses { get; private set; } = 0;

		readonly object cache_lock = new object();

		Timer pruneTimer = new Timer(1000 * 60 * 10);

		int Overflow = 10;
		int MaxCache = 20;
		int MinCache = 10;

		int MinAge = 5;
		int MaxAge = 60;

		public Cache(double interval = 10, int maxItems = 100, int minItems = 10,
					 StoreStrategy store = StoreStrategy.ReplaceNoMatch, EvictStrategy evict = EvictStrategy.LeastRecent)
		{
			CacheStoreStrategy = store;
			CacheEvictStrategy = evict;

			MaxCache = maxItems;
			MinCache = minItems;
			Overflow = (MaxCache / 2).Constrain(2, 50);

			pruneTimer.Interval = interval * 1000 * 60;
			pruneTimer.Elapsed += (s, e) =>
			{
				try
				{
					Prune();
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
				}
			};
			pruneTimer.Start();
		}

		int prune_in_progress = 0;

		/// <summary>
		/// Prune cache.
		/// </summary>
		void Prune()
		{
			if (!Atomic.Lock(ref prune_in_progress)) return; // only one instance.

			try
			{
				if (Items.Count <= MinCache) return; // just don't bother

				if (Items.Count <= MaxCache && CacheEvictStrategy == EvictStrategy.LeastUsed) return;

				lock (cache_lock)
				{
					var list = Items.Values.ToList(); // would be nice to cache this list

					list.Sort(delegate (CacheItem<K1, K2, T> x, CacheItem<K1, K2, T> y)
					{
						if (CacheEvictStrategy == EvictStrategy.LeastRecent)
						{
							if (x.Access < y.Access) return -1;
							if (x.Access > y.Access) return 1;
						// Both have equal at this point
						if (x.Desirability < y.Desirability)
								return -1;
							if (x.Desirability > y.Desirability) return 1;
						}
						else
						{
							if (x.Desirability < y.Desirability) return -1;
							if (x.Desirability > y.Desirability) return 1;
						// Both have equal at this point
						if (x.Access < y.Access)
								return -1;
							if (x.Access > y.Access) return 1;
						}

						return 0;
					});

					// Log.Debug("CACHE STATE: FIRST({First}) LAST({LAST})", list.First().Access, list.Last().Access);
					// Log.Debug("CACHE ITEMS BEFORE PRUNE: {Items}", Items.Count);
					while (Items.Count > MaxCache)
					{
						var bu = list.ElementAt(0);
						var key = bu.AccessKey;
						// Log.Debug("--- REMOVING: Key:{Key}, Last Access: {Date}, Desirability: {Value}",
						// 		  key, bu.Access, bu.Desirability);
						Items.Remove(key);
						list.RemoveAt(0);
					}
					// Log.Debug("CACHE ITEMS AFTER PRUNE: {Items}", Items.Count);

					double bi = double.NaN;

					var deleteItem = new Action<K1>(
						(K1 key) =>
						{
							if (Taskmaster.Trace) Log.Verbose("Removing {time} min old item.", string.Format("{0:N1}", bi));
							Items.Remove(key);
							list.RemoveAt(0);
						}
					);

					var now = DateTime.Now;
					while (list.Count > 0)
					{
						var bu = list.ElementAt(0);
						bi = now.TimeSince(bu.Access).TotalMinutes;

						if (CacheEvictStrategy == EvictStrategy.LeastRecent)
						{
							if (bi > MaxAge)
								deleteItem(bu.AccessKey);
							else
								break;
						}
						else // .LeastUsed, TM is never going to reach this.
						{
							if (bi > MinAge)
								deleteItem(bu.AccessKey);
							else
								break;
						}
					}
				}
			}
			finally
			{
				Atomic.Unlock(ref prune_in_progress);
			}
		}

		/// <summary>
		/// Add cache entry.
		/// </summary>
		/// <returns>The add.</returns>
		/// <param name="accesskey">Accesskey.</param>
		/// <param name="returnkey">Returnkey.</param>
		/// <param name="item">Item.</param>
		public bool Add(K1 accesskey, K2 returnkey, T item)
		{
			Misses++;

			lock (cache_lock)
			{
				if (Items.ContainsKey(accesskey))
				{
					if (CacheStoreStrategy == StoreStrategy.Fail)
						return false;

					Items.Remove(accesskey); // .Replace
				}

				var ci = new CacheItem<K1, K2, T> { AccessKey = accesskey, ReturnKey = returnkey, Item = item, Access = DateTime.Now, Desirability = 1 };
				CacheItem<K1, K2, T> t = ci;
				Items.Add(accesskey, t);
			}

			return true;
		}

		/// <summary>
		/// Get cached entry.
		/// </summary>
		/// <returns>The get.</returns>
		/// <param name="key">Key.</param>
		/// <param name="cacheditem">Cacheditem.</param>
		/// <param name="returnkeytest">Returnkeytest.</param>
		public K2 Get(K1 key, out T cacheditem, K2 returnkeytest = null)
		{
			try
			{
				lock (cache_lock)
				{
					CacheItem<K1, K2, T> item;
					if (Items.TryGetValue(key, out item))
					{
						if (returnkeytest != null && !item.ReturnKey.Equals(returnkeytest)) // == does not match positively identical strings for some reason
						{
							cacheditem = null;
							Misses++;
							Drop(key);
							return null;
						}

						item.Desirability++;
						item.Access = DateTime.Now;
						cacheditem = item.Item;
						Hits++;
						return item.ReturnKey;
					}
				}
			}
			catch
			{
				// NOP, don't caree
			}

			// this is bad design
			if (Count > MaxCache + Overflow)
			{
				// TODO: make restart timer or something?
				/*
				 * Prune();
				 */
			}

			Misses++;
			cacheditem = null;
			return null;
		}

		public void Drop(K1 key) => Items.Remove(key);

		#region IDisposable Support
		bool disposed = false; // To detect redundant calls

		void Dispose(bool disposing)
		{
			if (!disposed)
			{
				if (disposing)
				{
					Utility.Dispose(ref pruneTimer);
				}

				disposed = true;
			}
		}

		public void Dispose()
		{
			Dispose(true);
		}
		#endregion
	}

	sealed public class CacheEventArgs : EventArgs
	{
		public long Objects;
		public long Hits;
		public long Misses;
	}
}