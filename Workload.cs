using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TaskAndThreadPerformanceComparison
{
	class Workload : IWorkload
	{
		private readonly CancellationTokenSource m_cancellationTokenSource;

		private int m_readCount = 0;
		private int m_completeCount = 0;
		private readonly ManualResetEventSlim m_completed;
		private readonly Stopwatch m_stopWatch;

		public Workload(int messageCount, int bufferSize, TimeSpan readDelay)
		{
			m_stopWatch = new Stopwatch();
			m_completed = new ManualResetEventSlim(false);
			MessageCount = messageCount;
			MaxDataSize = bufferSize;
			ReadDelay = readDelay;
			m_cancellationTokenSource = new CancellationTokenSource();
		}

		public int MessageCount { get; }

		public TimeSpan ReadDelay { get; }

		public int MaxDataSize { get; }

		public CancellationToken Cancellation => m_cancellationTokenSource.Token;

		public TimeSpan Elapsed => m_stopWatch.Elapsed;

		public int Read(byte[] data, int offset, int count)
		{

			if (ReadDelay > TimeSpan.Zero)
			{
				Thread.Sleep(ReadDelay);
			}

			var readCount = Interlocked.Increment(ref m_readCount);
			if (readCount == 1)
			{
				m_stopWatch.Start();
			}
			if (readCount == MessageCount)
			{
				m_cancellationTokenSource.Cancel();
			}
			return MaxDataSize;
		}

		public void OnComplete(byte[] data)
		{
			if (Interlocked.Increment(ref m_completeCount) == MessageCount)
			{
				m_stopWatch.Stop();
				m_cancellationTokenSource.Cancel();
				m_completed.Set();
			}
		}

		public void WaitForCompletion()
		{
			if (!m_completed.Wait(TimeSpan.FromMilliseconds(MessageCount * 1.5 * ReadDelay.TotalMilliseconds)))
			{
				throw new InvalidOperationException("Timeout waiting for completion");
			}

			if (m_readCount < MessageCount)
			{
				throw new InvalidOperationException($"Not all reads {m_readCount} < {MessageCount}");
			}

			if (m_completeCount < MessageCount)
			{
				throw new InvalidOperationException($"Not all messages completed {m_completeCount} < {MessageCount}");
			}
		}

		public override string ToString()
		{
			return
				$"{MessageCount} messages with {MaxDataSize} bytes and a read delay of {ReadDelay.TotalMilliseconds} ms";
		}

		public void Dispose()
		{
			WaitForCompletion();
			m_completed.Dispose();
			m_cancellationTokenSource?.Dispose();
		}
	}
}
