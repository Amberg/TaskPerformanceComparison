using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Airborne.Generic.Collections;

namespace TaskAndThreadPerformanceComparison
{
	class WorkerQueueTest : IWorkerUnderTest
	{
		private WorkerQueue<byte[]> m_workerQueue;
		private IWorkload m_workload;

		public void ReadAll(IWorkload workload)
		{
			m_workload = workload;
			m_workerQueue = new WorkerQueue<byte[]>("WokerQueue", 0, 2000, OnOverrun, ProcessItems);
			m_workerQueue.Start();
			while (!workload.Cancellation.IsCancellationRequested)
			{
				var buffer = new byte[workload.MaxDataSize];
				workload.Read(buffer, 0, buffer.Length);
				m_workerQueue.AddItem(buffer);
			}
			m_workerQueue.FlushAndStop(false);
			m_workerQueue.Dispose();
		}

		private void ProcessItems(IEnumerable<WorkerQueueItem<byte[]>> data)
		{
			foreach (var item in data)
			{
				m_workload.OnComplete(item.m_item);
			}
		}

		private WorkerQueueOverrunAction OnOverrun(IWorkerQueue<byte[]> data)
		{
			return WorkerQueueOverrunAction.ExtendQueue;
		}

		public override string ToString()
		{
			return "One WorkerQueue";
		}
	}
}
