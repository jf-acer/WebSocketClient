using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleApp2.Buffers
{
	internal sealed class DefaultArrayPool<T> : ArrayPool<T>
	{
		[EventSource(Name = "System.Buffers.ArrayPoolEventSource")]
		internal sealed class ArrayPoolEventSource : EventSource
		{
			internal enum BufferAllocatedReason
			{
				Pooled,
				OverMaximumSize,
				PoolExhausted
			}

			internal static readonly ArrayPoolEventSource Log = new ArrayPoolEventSource();

			[Event(1, Level = EventLevel.Verbose)]
			internal unsafe void BufferRented(int bufferId, int bufferSize, int poolId, int bucketId)
			{
				EventData* ptr = stackalloc EventData[4];
				ptr->Size = 4;
				ptr->DataPointer = (IntPtr)(&bufferId);
				ptr[1].Size = 4;
				ptr[1].DataPointer = (IntPtr)(&bufferSize);
				ptr[2].Size = 4;
				ptr[2].DataPointer = (IntPtr)(&poolId);
				ptr[3].Size = 4;
				ptr[3].DataPointer = (IntPtr)(&bucketId);
				WriteEventCore(1, 4, ptr);
			}

			[Event(2, Level = EventLevel.Informational)]
			internal unsafe void BufferAllocated(int bufferId, int bufferSize, int poolId, int bucketId, BufferAllocatedReason reason)
			{
				EventData* ptr = stackalloc EventData[5];
				ptr->Size = 4;
				ptr->DataPointer = (IntPtr)(&bufferId);
				ptr[1].Size = 4;
				ptr[1].DataPointer = (IntPtr)(&bufferSize);
				ptr[2].Size = 4;
				ptr[2].DataPointer = (IntPtr)(&poolId);
				ptr[3].Size = 4;
				ptr[3].DataPointer = (IntPtr)(&bucketId);
				ptr[4].Size = 4;
				ptr[4].DataPointer = (IntPtr)(&reason);
				WriteEventCore(2, 5, ptr);
			}

			[Event(3, Level = EventLevel.Verbose)]
			internal void BufferReturned(int bufferId, int bufferSize, int poolId)
			{
				WriteEvent(3, bufferId, bufferSize, poolId);
			}
		}

		private sealed class Bucket
		{
			internal readonly int _bufferLength;

			private readonly T[][] _buffers;

			private readonly int _poolId;

			private SpinLock _lock;

			private int _index;

			internal int Id => GetHashCode();

			internal Bucket(int bufferLength, int numberOfBuffers, int poolId)
			{
				_lock = new SpinLock(Debugger.IsAttached);
				_buffers = new T[numberOfBuffers][];
				_bufferLength = bufferLength;
				_poolId = poolId;
			}

			internal T[] Rent()
			{
				T[][] buffers = _buffers;
				T[] array = null;
				bool lockTaken = false;
				bool flag = false;
				try
				{
					_lock.Enter(ref lockTaken);
					if (_index < buffers.Length)
					{
						array = buffers[_index];
						buffers[_index++] = null;
						flag = array == null;
					}
				}
				finally
				{
					if (lockTaken)
					{
						_lock.Exit(useMemoryBarrier: false);
					}
				}
				if (flag)
				{
					array = new T[_bufferLength];
					ArrayPoolEventSource log = ArrayPoolEventSource.Log;
					if (log.IsEnabled())
					{
						log.BufferAllocated(array.GetHashCode(), _bufferLength, _poolId, Id, ArrayPoolEventSource.BufferAllocatedReason.Pooled);
					}
				}
				return array;
			}

			internal void Return(T[] array)
			{
				if (array.Length != _bufferLength)
				{
					throw new ArgumentException(/*System.SR.ArgumentException_BufferNotFromPool, "array"*/);
				}
				bool lockTaken = false;
				try
				{
					_lock.Enter(ref lockTaken);
					if (_index != 0)
					{
						_buffers[--_index] = array;
					}
				}
				finally
				{
					if (lockTaken)
					{
						_lock.Exit(useMemoryBarrier: false);
					}
				}
			}
		}

		private const int DefaultMaxArrayLength = 1048576;

		private const int DefaultMaxNumberOfArraysPerBucket = 50;

		private static T[] s_emptyArray;

		private readonly Bucket[] _buckets;

		private int Id => GetHashCode();

		internal DefaultArrayPool()
			: this(1048576, 50)
		{
		}

		internal DefaultArrayPool(int maxArrayLength, int maxArraysPerBucket)
		{
			if (maxArrayLength <= 0)
			{
				throw new ArgumentOutOfRangeException("maxArrayLength");
			}
			if (maxArraysPerBucket <= 0)
			{
				throw new ArgumentOutOfRangeException("maxArraysPerBucket");
			}
			if (maxArrayLength > 1073741824)
			{
				maxArrayLength = 1073741824;
			}
			else if (maxArrayLength < 16)
			{
				maxArrayLength = 16;
			}
			int id = Id;
			int num = Utilities.SelectBucketIndex(maxArrayLength);
			Bucket[] array = new Bucket[num + 1];
			for (int i = 0; i < array.Length; i++)
			{
				array[i] = new Bucket(Utilities.GetMaxSizeForBucket(i), maxArraysPerBucket, id);
			}
			_buckets = array;
		}

		public override T[] Rent(int minimumLength)
		{
			if (minimumLength < 0)
			{
				throw new ArgumentOutOfRangeException("minimumLength");
			}
			if (minimumLength == 0)
			{
				return s_emptyArray ?? (s_emptyArray = new T[0]);
			}
			ArrayPoolEventSource log = ArrayPoolEventSource.Log;
			T[] array = null;
			int num = Utilities.SelectBucketIndex(minimumLength);
			if (num < _buckets.Length)
			{
				int num2 = num;
				do
				{
					array = _buckets[num2].Rent();
					if (array != null)
					{
						if (log.IsEnabled())
						{
							log.BufferRented(array.GetHashCode(), array.Length, Id, _buckets[num2].Id);
						}
						return array;
					}
				}
				while (++num2 < _buckets.Length && num2 != num + 2);
				array = new T[_buckets[num]._bufferLength];
			}
			else
			{
				array = new T[minimumLength];
			}
			if (log.IsEnabled())
			{
				int hashCode = array.GetHashCode();
				int bucketId = -1;
				log.BufferRented(hashCode, array.Length, Id, bucketId);
				log.BufferAllocated(hashCode, array.Length, Id, bucketId, (num >= _buckets.Length) ? ArrayPoolEventSource.BufferAllocatedReason.OverMaximumSize : ArrayPoolEventSource.BufferAllocatedReason.PoolExhausted);
			}
			return array;
		}

		public override void Return(T[] array, bool clearArray = false)
		{
			if (array == null)
			{
				throw new ArgumentNullException("array");
			}
			if (array.Length == 0)
			{
				return;
			}
			int num = Utilities.SelectBucketIndex(array.Length);
			if (num < _buckets.Length)
			{
				if (clearArray)
				{
					Array.Clear(array, 0, array.Length);
				}
				_buckets[num].Return(array);
			}
			ArrayPoolEventSource log = ArrayPoolEventSource.Log;
			if (log.IsEnabled())
			{
				log.BufferReturned(array.GetHashCode(), array.Length, Id);
			}
		}
	}

	internal static class Utilities
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal static int SelectBucketIndex(int bufferSize)
		{
			uint num = (uint)(bufferSize - 1) >> 4;
			int num2 = 0;
			if (num > 65535)
			{
				num >>= 16;
				num2 = 16;
			}
			if (num > 255)
			{
				num >>= 8;
				num2 += 8;
			}
			if (num > 15)
			{
				num >>= 4;
				num2 += 4;
			}
			if (num > 3)
			{
				num >>= 2;
				num2 += 2;
			}
			if (num > 1)
			{
				num >>= 1;
				num2++;
			}
			return num2 + (int)num;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal static int GetMaxSizeForBucket(int binIndex)
		{
			return 16 << binIndex;
		}
	}

	public abstract class ArrayPool<T>
	{
		private static ArrayPool<T> s_sharedInstance;

		public static ArrayPool<T> Shared
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get
			{
				return Volatile.Read(ref s_sharedInstance) ?? EnsureSharedCreated();
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static ArrayPool<T> EnsureSharedCreated()
		{
			Interlocked.CompareExchange(ref s_sharedInstance, Create(), null);
			return s_sharedInstance;
		}

		public static ArrayPool<T> Create()
		{
			return new DefaultArrayPool<T>();
		}

		public static ArrayPool<T> Create(int maxArrayLength, int maxArraysPerBucket)
		{
			return new DefaultArrayPool<T>(maxArrayLength, maxArraysPerBucket);
		}

		public abstract T[] Rent(int minimumLength);

		public abstract void Return(T[] array, bool clearArray = false);
	}
}
