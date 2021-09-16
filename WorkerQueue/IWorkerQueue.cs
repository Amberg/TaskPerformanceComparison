using System;

namespace Airborne.Generic.Collections
{
	/// <summary>
	/// This generic class provides a queue with own worker thread. The jobs are weighted, only if
	/// the weight threshold is exceeded the queue worked is started with a list of all jobs.
	/// As client just add weighted items to the queue.
	/// </summary>
	/// <typeparam name="T">Type of queue item</typeparam>
	public interface IWorkerQueue<T> : IDisposable, IWorkerQueueInformation
	{
		/// <summary>
		/// Flag which indicates that new items can be added or not.
		/// </summary>
		bool CanAddItems
		{
			get;
			set;
		}

		/// <summary>
		/// Adds an item to the queue including the weight
		/// </summary>
		bool AddItem(T item, int weight);

		/// <summary>
		/// Adds an item to the queue with default weight 1.
		/// </summary>
		bool AddItem(T item);

		/// <summary>
		/// Starts the queue processing in a new thread.
		/// </summary>
		void Start();

		/// <summary>
		/// Flushes the queue.
		/// </summary>
		void Flush();

		/// <summary>
		/// Forces a flush and wait until worker thread is finished.
		/// </summary>
		void FlushAndStop(bool canAddNewItemsDuringFlush);

		/// <summary>
		/// Stops the worker queue thread and discards remaining items.
		/// </summary>
		void DiscardRemainingItemsAndStop();
	}
}
