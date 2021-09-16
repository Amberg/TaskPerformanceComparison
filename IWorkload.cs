using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TaskAndThreadPerformanceComparison
{
	interface IWorkload : IDisposable
	{
		CancellationToken Cancellation
		{
			get;
		}

		TimeSpan Elapsed
		{
			get;
		}

		int MaxDataSize { get; }

		TimeSpan ReadDelay { get; }

		int MessageCount { get; }

		int Read(byte[] data, int offset, int count);

		void OnComplete(byte[] data);

		void WaitForCompletion();
	}
}
