using System.Runtime.InteropServices;

namespace SQLDbgEng
{
	public class AutoPinMemory : IDisposable
	{
		GCHandle _data;

		public IntPtr MemoryPtr { get { return _data.AddrOfPinnedObject(); } }

		public static implicit operator IntPtr(AutoPinMemory obj)
		{
			return obj.MemoryPtr;
		}

		public AutoPinMemory(object obj)
		{
			_data = GCHandle.Alloc(obj, GCHandleType.Pinned);
		}

		~AutoPinMemory()
		{
			Dispose();
		}

		public void Dispose()
		{
			if (_data.IsAllocated)
			{
				_data.Free();
			}
		}
	}
}
