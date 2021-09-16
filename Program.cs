using System;

namespace TaskAndThreadPerformanceComparison
{
	class Program
	{
		static void Main(string[] args)
		{
			Console.WriteLine("Hello World!");
			MessageThroughput();
		}

		private static void MessageThroughput()
		{
			using (var workload = new Workload(1000 * 1000, 1024, TimeSpan.Zero))
			{
				var worker = new WorkerQueueTest();
				RunWorkload(worker, workload);
			}

			using (var workload = new Workload(1000 * 1000, 1024, TimeSpan.Zero))
			{
				var worker = new ActionBlockTest();
				RunWorkload(worker, workload);
			}

			using (var workload = new Workload(500, 1024, TimeSpan.FromMilliseconds(10)))
			{
				var worker = new WorkerQueueTest();
				RunWorkload(worker, workload);
			}

			using (var workload = new Workload(500, 1024, TimeSpan.FromMilliseconds(10)))
			{
				var worker = new ActionBlockTest();
				RunWorkload(worker, workload);
			}

		}

		private static void RunWorkload(IWorkerUnderTest worker, IWorkload workload)
		{
			worker.ReadAll(workload);
			workload.WaitForCompletion();
			Console.WriteLine($"{worker} took {workload.Elapsed.TotalMilliseconds:F3} ms for {workload.MessageCount} messages (size: {workload.MaxDataSize} read delay {workload.ReadDelay.TotalMilliseconds:F3} ms) - {workload.Elapsed.TotalMilliseconds / workload.MessageCount:F6} ms / message");
		}
	}
}
