using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace Airborne.Generic.Collections
{
	/// <summary>
	/// Handled flag must be an out parameter. Cannot use return value because this would lead to unreachable code when rethrowing in custom exception handler.
	/// </summary>
	public delegate void WorkerQueueExceptionHandler(Exception exception, out bool isHandled);

	public sealed class WorkerQueue<T> : IWorkerQueue<T>
	{
		private readonly int m_weightThreshold;
		private readonly object m_queueLock;
		private readonly object m_startStopLock = new object();
		private readonly string m_name;
		private readonly int m_maxQueueSize;
		private Queue<WorkerQueueItem<T>> m_queue;
		private int m_queueWeight;

		private AutoResetEvent m_doWorkEvent;
		private ManualResetEvent m_workerIdle;
		private CancellationTokenSource m_cancellationTokenSource;
		private bool m_canAddItems;
		private Thread m_workerThread;
		private bool m_workerAlive;

		private WorkerQueue(string name, int weightThreshold, int maxQueueSize)
		{
			m_name = name ?? throw new ArgumentNullException(nameof(name));
			m_maxQueueSize = maxQueueSize;

			m_doWorkEvent = new AutoResetEvent(false);
			m_workerIdle = new ManualResetEvent(true);
			m_canAddItems = true;

			m_weightThreshold = weightThreshold;
			m_queue = new Queue<WorkerQueueItem<T>>();
			m_queueLock = new object();
			m_queueWeight = 0;
			m_workerAlive = true;
		}

		/// <summary>
		/// Creates a new worker queue with standard exception handler and batch job processing callback.
		/// </summary>
		public WorkerQueue(string name, int weightThreshold, int maxQueueSize, Func<IWorkerQueue<T>, WorkerQueueOverrunAction> onOverrun, Action<IEnumerable<WorkerQueueItem<T>>> processItems)
			: this(name, weightThreshold, maxQueueSize)
		{
			if (onOverrun == null)
			{
				throw new ArgumentNullException(nameof(onOverrun));
			}
			if (processItems == null)
			{
				throw new ArgumentNullException(nameof(processItems));
			}
			OnOverrun = onOverrun;
			ProcessItems = processItems;
		}

		/// <summary>
		/// Creates a new worker queue with standard exception handler and single job processing callback.
		/// </summary>
		public WorkerQueue(string name, int weightThreshold, int maxQueueSize, Func<IWorkerQueue<T>, WorkerQueueOverrunAction> onOverrun, Action<T> processItem)
			: this(name, weightThreshold, maxQueueSize)
		{
			if (onOverrun == null)
			{
				throw new ArgumentNullException(nameof(onOverrun));
			}
			if (processItem == null)
			{
				throw new ArgumentNullException(nameof(processItem));
			}
			OnOverrun = onOverrun;
			ProcessItem = processItem;
		}

		/// <summary>
		/// Creates a new worker queue with custom exception handler and batch job processing callback.
		/// </summary>
		public WorkerQueue(string name, int weightThreshold, int maxQueueSize, Func<IWorkerQueue<T>, WorkerQueueOverrunAction> onOverrun, Action<IEnumerable<WorkerQueueItem<T>>> processItems, WorkerQueueExceptionHandler onException)
			: this(name, weightThreshold, maxQueueSize)
		{
			if (onOverrun == null)
			{
				throw new ArgumentNullException(nameof(onOverrun));
			}
			if (processItems == null)
			{
				throw new ArgumentNullException(nameof(processItems));
			}
			if (onException == null)
			{
				throw new ArgumentNullException(nameof(onException));
			}
			OnOverrun = onOverrun;
			ProcessItems = processItems;
			OnException = onException;
		}

		/// <summary>
		/// Creates a new worker queue with custom exception handler and single job processing callback.
		/// </summary>
		public WorkerQueue(string name, int weightThreshold, int maxQueueSize, Func<IWorkerQueue<T>, WorkerQueueOverrunAction> onOverrun, Action<T> processItem, WorkerQueueExceptionHandler onException)
			: this(name, weightThreshold, maxQueueSize)
		{
			if (onOverrun == null)
			{
				throw new ArgumentNullException(nameof(onOverrun));
			}
			if (processItem == null)
			{
				throw new ArgumentNullException(nameof(processItem));
			}
			if (onException == null)
			{
				throw new ArgumentNullException(nameof(onException));
			}
			OnOverrun = onOverrun;
			ProcessItem = processItem;
			OnException = onException;
		}

		/// <summary>
		/// Creates a new worker queue with custom actions for fill level, custom exception handler and batch job processing callback.
		/// </summary>
		public WorkerQueue(string name, int weightThreshold, int maxQueueSize, Func<IWorkerQueue<T>, WorkerQueueOverrunAction> onOverrun, Action<IEnumerable<WorkerQueueItem<T>>> processItems, WorkerQueueExceptionHandler onException, Action onStart, Action onEnd, Action onEmpty)
			: this(name, weightThreshold, maxQueueSize)
		{
			if (onOverrun == null)
			{
				throw new ArgumentNullException(nameof(onOverrun));
			}
			if (processItems == null)
			{
				throw new ArgumentNullException(nameof(processItems));
			}
			if (onException == null)
			{
				throw new ArgumentNullException(nameof(onException));
			}
			if (onStart == null)
			{
				throw new ArgumentNullException(nameof(onStart));
			}
			if (onEnd == null)
			{
				throw new ArgumentNullException(nameof(onEnd));
			}
			if (onEmpty == null)
			{
				throw new ArgumentNullException(nameof(onEmpty));
			}

			OnOverrun = onOverrun;
			ProcessItems = processItems;
			OnException = onException;
			OnStart = onStart;
			OnEnd = onEnd;
			OnEmpty = onEmpty;
		}

		private Action<IEnumerable<WorkerQueueItem<T>>> ProcessItems
		{
			get;
		}

		private Action<T> ProcessItem
		{
			get;
		}

		private Func<IWorkerQueue<T>, WorkerQueueOverrunAction> OnOverrun
		{
			get;
		}

		internal int CurrentWeightForUtest => m_queueWeight;

		public int Count
		{
			get
			{
				lock (m_queueLock)
				{
					return m_queue.Count;
				}
			}
		}

		private WorkerQueueExceptionHandler OnException
		{
			get;
		}

		private Action OnStart
		{
			get;
		}

		private Action OnEnd
		{
			get;
		}

		private Action OnEmpty
		{
			get;
		}

		public bool CanAddItems
		{
			get => m_canAddItems;
			set => m_canAddItems = value;
		}

		public string Name => m_name;

		/// <summary>
		/// Default exception handling when no custom exception handler provided or custom exception handler does not mark exception as handled.
		/// Behavior is to wrap into an aggregate exception and rethrow to the app domain default exception handler.
		/// Worker thread will NOT continue it's execution.
		/// </summary>
		/// <exception cref="AggregateException">The source exception is provided as inner exception</exception>
		private void DefaultExceptionHandler(Exception exception)
		{
			throw new AggregateException(FormattableString.Invariant($"Exception in worker queue: {m_name}"), exception);
		}

		/// <summary>
		/// Calls custom exception handler if given.
		/// Custom handler can decide whether it can handle the exception or not.
		/// If not handled the default exception handler is executed after custom exception handler.
		/// </summary>
		private void HandleException(Exception exception)
		{
			var isHandled = false;
			OnException?.Invoke(exception, out isHandled);
			if (!isHandled)
			{
				// Set flag to false if an unhandled exception killed the thread to avoid deadlock in flush
				m_workerAlive = false;
				DefaultExceptionHandler(exception);
			}
		}

		/// <exception cref="InvalidOperationException">The worker queue already started</exception>
		public void Start()
		{
			lock (m_startStopLock)
			{
				if (m_workerThread == null)
				{
					m_doWorkEvent.Dispose();
					m_workerIdle.Dispose();
					m_doWorkEvent = new AutoResetEvent(false);
					m_workerIdle = new ManualResetEvent(true);
					m_cancellationTokenSource = new CancellationTokenSource();
					m_workerThread = new Thread(DoWork) { Name = m_name };
					m_canAddItems = true;
					m_workerThread.Start();
				}
				else
				{
					throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, "Worker queue {0} already started", m_name));
				}
			}
		}

		public void FlushAndStop(bool canAddNewItemsDuringFlush)
		{
			lock (m_startStopLock)
			{
				if (m_workerThread != null)
				{
					m_canAddItems = canAddNewItemsDuringFlush;
					Flush();
					m_cancellationTokenSource.Cancel();
					m_workerThread.Join();
					m_workerThread = null;
				}
			}
		}

		public void DiscardRemainingItemsAndStop()
		{
			lock (m_startStopLock)
			{
				m_canAddItems = false;
				lock (m_queueLock)
				{
					m_queue = new Queue<WorkerQueueItem<T>>();
				}
				if (m_workerThread != null)
				{
					m_cancellationTokenSource.Cancel();
					m_workerThread.Join();
					m_workerThread = null;
				}
			}
		}

		/// <summary>
		/// Forces a flush by setting the work event. Do not call the process item delegate directly to ensure that the delegate
		/// is always called from the same thread and also that it runs in sequence.
		/// </summary>
		public void Flush()
		{
			// Wait for the worker to be idle before triggering the processing execution
			m_workerIdle.WaitOne();

			// Trigger processing - if the worker is still active
			if (m_workerAlive)
			{
				m_workerIdle.Reset();
				m_doWorkEvent.Set();
				m_workerIdle.WaitOne();
			}
		}

		/// <summary>
		/// Flushes the queue internal. This method can only be called from the worker thread.
		/// </summary>
		private void FlushInternal()
		{
			IEnumerable<WorkerQueueItem<T>> jobList;
			lock (m_queueLock)
			{
				jobList = m_queue;
				m_queue = new Queue<WorkerQueueItem<T>>();
				m_queueWeight = 0;
			}

			try
			{
				// Try both processing callbacks, only one should be available
				ProcessItems?.Invoke(jobList);
				if (ProcessItem != null)
				{
					foreach (WorkerQueueItem<T> item in jobList)
					{
						ProcessItem(item.m_item);
					}
				}
			}
			catch (Exception e)
			{
				HandleException(e);
			}

			var onEmptyThreadSafeLocalCopy = OnEmpty;
			if (onEmptyThreadSafeLocalCopy != null)
			{
				try
				{
					onEmptyThreadSafeLocalCopy();
				}
				catch (Exception e)
				{
					HandleException(e);
				}
			}
		}

		public bool AddItem(T item, int weight)
		{
			bool wasAdded = false;
			if (m_canAddItems)
			{
				var overrunAction = WorkerQueueOverrunAction.ExtendQueue;
				if (Count >= m_maxQueueSize)
				{
					overrunAction = OnOverrun(this);
				}

				if (overrunAction == WorkerQueueOverrunAction.ExtendQueue)
				{
					lock (m_queueLock)
					{
						wasAdded = true;
						m_queue.Enqueue(new WorkerQueueItem<T> { m_item = item, m_weight = weight });
						m_queueWeight += weight;
						if (m_queueWeight >= m_weightThreshold)
						{
							m_doWorkEvent.Set();
						}
					}
				}
			}
			return wasAdded;
		}

		public bool AddItem(T item)
		{
			return AddItem(item, 1);
		}

		/// <summary>
		/// The internal working thread method.
		/// </summary>
		private void DoWork()
		{
			var onStartThreadSafeLocalCopy = OnStart;
			if (onStartThreadSafeLocalCopy != null)
			{
				try
				{
					onStartThreadSafeLocalCopy();
				}
				catch (Exception e)
				{
					HandleException(e);
				}
			}
			var waitArray = new[] { m_doWorkEvent, m_cancellationTokenSource.Token.WaitHandle };
			for (; ;)
			{
				WaitHandle.WaitAny(waitArray);
				m_workerIdle.Reset();
				try
				{
					FlushInternal();
				}
				finally
				{
					m_workerIdle.Set();
				}
				if (m_cancellationTokenSource.IsCancellationRequested)
				{
					break;
				}
			}
			var onEndThreadSafeLocalCopy = OnEnd;
			if (onEndThreadSafeLocalCopy != null)
			{
				try
				{
					onEndThreadSafeLocalCopy();
				}
				catch (Exception e)
				{
					HandleException(e);
				}
			}
		}

		public void Dispose()
		{
			DiscardRemainingItemsAndStop();
			if (m_doWorkEvent != null)
			{
				m_doWorkEvent.Dispose();
				m_doWorkEvent = null;
			}
			if (m_workerIdle != null)
			{
				m_workerIdle.Dispose();
				m_workerIdle = null;
			}
			if (m_cancellationTokenSource != null)
			{
				m_cancellationTokenSource.Dispose();
				m_cancellationTokenSource = null;
			}
		}
	}
}
