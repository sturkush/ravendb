namespace RavenFS.Rdc
{
	using System;
	using System.Collections.Concurrent;
	using System.Collections.Generic;
	using System.Linq;
	using Client;
	using Extensions;
	using Storage;

	public class SynchronizationQueue
	{
		private readonly TransactionalStorage storage;
		private const int DefaultLimitOfConcurrentSynchronizations = 5;

		private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> pendingSynchronizations =
			new ConcurrentDictionary<string, ConcurrentQueue<string>>();

		private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, string>> activeSynchronizations =
			new ConcurrentDictionary<string, ConcurrentDictionary<Guid, string>>();

		public SynchronizationQueue(TransactionalStorage storage)
		{
			this.storage = storage;
		}

		public IEnumerable<SynchronizationDetails> Pending
		{
			get
			{
				return from destinationPending in pendingSynchronizations
				       from pendingFile in destinationPending.Value
				       select new SynchronizationDetails
				              	{
				              		DestinationUrl = destinationPending.Key,
				              		FileName = pendingFile
				              	};
			}
		}

		public IEnumerable<SynchronizationDetails> Active
		{
			get
			{
				return from destinationActive in activeSynchronizations
				       from activeFile in destinationActive.Value
				       select new SynchronizationDetails
				       {
				              		DestinationUrl = destinationActive.Key,
				              		FileName = activeFile.Value
				              	};
			}
		}

		private int LimitOfConcurrentSynchronizations()
		{
			bool limit = false;
			int configuredLimit = 0;

			storage.Batch(
				accessor => limit = accessor.TryGetConfigurationValue(SynchronizationConstants.RavenReplicationLimit, out configuredLimit));

			return limit ? configuredLimit : DefaultLimitOfConcurrentSynchronizations;
		}

		private int NumberOfActiveSynchronizationTasksFor(string destination)
		{
			return activeSynchronizations.GetOrAdd(destination, new ConcurrentDictionary<Guid, string>()).Count;
		}

		public bool CanSynchronizeTo(string destination)
		{
			return LimitOfConcurrentSynchronizations() > NumberOfActiveSynchronizationTasksFor(destination);
		}

		public int AvailableSynchronizationRequestsTo(string destination)
		{
			return LimitOfConcurrentSynchronizations() - NumberOfActiveSynchronizationTasksFor(destination);
		}

		public void EnqueueSynchronization(string destination, string fileName)
		{
			var pendingForDestination = pendingSynchronizations.GetOrAdd(destination, new ConcurrentQueue<string>());

			if(pendingForDestination.Contains(fileName)) // if there is a file in pending synchronizations do not add it again
			{
				return;
			}

			pendingForDestination.Enqueue(fileName);
		}

		public bool TryDequeuePendingSynchronization(string destination, out string fileToSynchronize)
		{
			ConcurrentQueue<string> pendingForDestination;
			if (pendingSynchronizations.TryGetValue(destination, out pendingForDestination) == false)
			{
				fileToSynchronize = null;
				return false;
			}

			return pendingForDestination.TryDequeue(out fileToSynchronize);
		}

		public void SynchronizationStarted(string fileName, Guid etag, string destination)
		{
			var activeForDestination = activeSynchronizations.GetOrAdd(destination, new ConcurrentDictionary<Guid, string>());

			activeForDestination.TryAdd(etag, fileName);
		}

		public void SynchronizationFinished(string fileName, Guid etag, string destination)
		{
			ConcurrentDictionary<Guid, string> activeDestinationTasks;

			if (activeSynchronizations.TryGetValue(destination, out activeDestinationTasks) == false)
				return;
			
			string removingItem;

			if (activeDestinationTasks.TryRemove(etag, out removingItem))
			{
				if(removingItem == fileName)
				{
					return;
				}
			}

			throw new SynchronizationException(string.Format("File {0} (ETag: {1}) wasn't synchronized to destination server {2}", fileName, etag, destination));
		}
	}
}