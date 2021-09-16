using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace TaskAndThreadPerformanceComparison
{
	class ActionBlockTest : IWorkerUnderTest
	{
		private IWorkload m_workload;

		public void ReadAll(IWorkload workload)
		{
			m_workload = workload;
			var actionBlock = new ActionBlock<byte[]>(Action);
			while (!workload.Cancellation.IsCancellationRequested)
			{
				var buffer = new byte[workload.MaxDataSize];
				workload.Read(buffer, 0, buffer.Length);
				actionBlock.Post(buffer);
			}
			actionBlock.Complete();
			actionBlock.Completion.Wait();
		}

		private void Action(byte[] obj)
		{
			m_workload.OnComplete(obj);
		}

		public override string ToString()
		{
			return "ActionBlock";
		}
	}
}
