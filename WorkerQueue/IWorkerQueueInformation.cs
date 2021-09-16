namespace Airborne.Generic.Collections
{
	/// <summary>
	/// Information about the worker queue.
	/// Without using generics.
	/// </summary>
	public interface IWorkerQueueInformation
	{
		/// <summary>
		/// The queue name.
		/// </summary>
		string Name
		{
			get;
		}

		/// <summary>
		/// Number of entries in the worker queue
		/// </summary>
		int Count
		{
			get;
		}
	}
}