﻿namespace Voron.Impl
{
	using System;
	using System.Collections.Concurrent;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.Linq;
	using System.Threading;
	using System.Threading.Tasks;

	using Extensions;
	using Trees;

	public class TransactionMergingWriter
	{
		private readonly StorageEnvironment _env;

		private readonly ConcurrentQueue<OutstandingWrite> _pendingWrites;

		internal readonly SemaphoreSlim _semaphore;

		internal TransactionMergingWriter(StorageEnvironment env)
		{
			_env = env;
			_pendingWrites = new ConcurrentQueue<OutstandingWrite>();
			_semaphore = new SemaphoreSlim(1, 1);
		}

		public void Write(WriteBatch batch)
		{
			if (batch.Operations.Count == 0)
				return;

			using (batch)
			{
				var mine = new OutstandingWrite(batch);
				_pendingWrites.Enqueue(mine);

				_semaphore.Wait();

				HandleActualWrites(mine);
			}
		}

		private void HandleActualWrites(OutstandingWrite mine)
		{
			List<OutstandingWrite> writes = null;
			try
			{
				if (mine.Done())
					return;

				writes = BuildBatchGroup(mine);

				using (var tx = _env.NewTransaction(TransactionFlags.ReadWrite))
				{
					HandleOperations(tx, writes.SelectMany(x => x.Batch.Operations));

					tx.Commit();
				}

				foreach (var write in writes)
					write.SetSuccess();
			}
			catch (Exception)
			{
				if (writes == null)
					throw;

				SplitWrites(writes);
			}
			finally
			{
				Finalize(writes);

				_semaphore.Release();
			}

			Debug.Assert(mine.Status != OutstandingWriteStatus.Pending);
			mine.Done();
		}

		private void Finalize(IEnumerable<OutstandingWrite> writes)
		{
			if (writes != null)
			{
				foreach (var write in writes)
				{
					Debug.Assert(_pendingWrites.Peek() == write);

					OutstandingWrite pendingWrite;
					_pendingWrites.TryDequeue(out pendingWrite);
				}
			}
		}

		private void HandleOperations(Transaction tx, IEnumerable<WriteBatch.BatchOperation> operations)
		{
			foreach (var g in operations.GroupBy(x => x.TreeName))
			{
				var tree = GetTree(g.Key);
				foreach (var operation in g)
				{
					operation.Reset();

					switch (operation.Type)
					{
						case WriteBatch.BatchOperationType.Add:
							tree.Add(tx, operation.Key, operation.Value, operation.Version);
							break;
						case WriteBatch.BatchOperationType.Delete:
							tree.Delete(tx, operation.Key, operation.Version);
							break;
					}
				}
			}
		}

		private void SplitWrites(IEnumerable<OutstandingWrite> writes)
		{
			foreach (var write in writes)
			{
				try
				{
					using (var tx = _env.NewTransaction(TransactionFlags.ReadWrite))
					{
						HandleOperations(tx, write.Batch.Operations);
						tx.Commit();

						write.SetSuccess();
					}
				}
				catch (Exception e)
				{
					write.SetError(e);
				}
			}
		}

		private List<OutstandingWrite> BuildBatchGroup(OutstandingWrite mine)
		{
			// Allow the group to grow up to a maximum size, but if the
			// original write is small, limit the growth so we do not slow
			// down the small write too much.
			long maxSize = 1024 * 1024; // 1 MB by default
			if (mine.Size < 128 * 1024)
				maxSize = mine.Size + (128 * 1024);

			var list = new List<OutstandingWrite> { mine };

			foreach (var write in _pendingWrites)
			{
				if (maxSize <= 0)
					break;

				if (write == mine)
					continue;

				list.Add(write);

				maxSize -= write.Size;
			}

			return list;
		}

		private Tree GetTree(string treeName)
		{
			if (treeName == null)
				return _env.Root;

			return _env.GetTree(treeName);
		}

		private class OutstandingWrite
		{
			private Exception exception;

			public OutstandingWrite(WriteBatch batch)
			{
				Batch = batch;
				Size = batch.Size;
				Status = OutstandingWriteStatus.Pending;
			}

			public WriteBatch Batch { get; private set; }

			public long Size { get; private set; }

			public OutstandingWriteStatus Status { get; private set; }

			public void SetSuccess()
			{
				Status = OutstandingWriteStatus.Success;
				exception = null;
			}

			public void SetError(Exception e)
			{
				Status = OutstandingWriteStatus.Error;
				exception = e;
			}

			public bool Done()
			{
				if (Status == OutstandingWriteStatus.Success)
					return true;

				if (Status == OutstandingWriteStatus.Pending)
					return false;

				throw exception;
			}
		}

		private enum OutstandingWriteStatus
		{
			Pending,
			Success,
			Error
		}
	}
}