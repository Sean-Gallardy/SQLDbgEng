using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SQLDbgEng.Minidump
{
	[StructLayout(LayoutKind.Sequential)]
	public struct MINIDUMP_HEADER
	{
		public UInt32 Signature;
		public UInt32 Version;
		public UInt32 NumberOfStreams;
		public UInt32 StreamDirectoryRva;
		public UInt32 CheckSum;
		public UInt32 TimeDateStamp;
		public MINIDUMP_TYPE Flags;

		public MINIDUMP_HEADER(){}
		public MINIDUMP_HEADER(BinaryReader reader)
		{
			UInt32 expectedSignature = BitConverter.ToUInt32(Encoding.ASCII.GetBytes("MDMP"));

			Signature = reader.ReadUInt32();

			if (Signature != expectedSignature)
			{
				throw new Exception($"Header signature does not match! Expected {expectedSignature} but read {Signature} !");
			}

			Version = reader.ReadUInt32();
			NumberOfStreams = reader.ReadUInt32();
			StreamDirectoryRva = reader.ReadUInt32();
			CheckSum = reader.ReadUInt32();
			TimeDateStamp = reader.ReadUInt32();
			Flags = (MINIDUMP_TYPE)reader.ReadUInt64();
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct MINIDUMP_LOCATION_DESCRIPTOR
	{
		public UInt32 DataSize;
		public UInt32 Rva;

		public MINIDUMP_LOCATION_DESCRIPTOR(){}
		public MINIDUMP_LOCATION_DESCRIPTOR(BinaryReader reader)
		{
			DataSize = reader.ReadUInt32();
			Rva = reader.ReadUInt32();
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct MINIDUMP_DIRECTORY
	{
		public UInt32 StreamType;
		public MINIDUMP_LOCATION_DESCRIPTOR Location;

		public MINIDUMP_DIRECTORY(){}
		public MINIDUMP_DIRECTORY(BinaryReader reader)
		{
			StreamType = reader.ReadUInt32();
			Location = new MINIDUMP_LOCATION_DESCRIPTOR(reader);
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct MINIDUMP_MEMORY_DESCRIPTOR
	{
		public UInt64 StartOfMemoryRange;
		public MINIDUMP_LOCATION_DESCRIPTOR Memory;

		public MINIDUMP_MEMORY_DESCRIPTOR(){}
		public MINIDUMP_MEMORY_DESCRIPTOR(BinaryReader reader)
		{
			StartOfMemoryRange = reader.ReadUInt64();
			Memory = new MINIDUMP_LOCATION_DESCRIPTOR(reader);
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct MINIDUMP_THREAD
	{
		public UInt32 ThreadId;
		public UInt32 SuspendCount;
		public UInt32 PriorityClass;
		public UInt32 Priority;
		public UInt64 Teb;
		public MINIDUMP_MEMORY_DESCRIPTOR Stack;
		public MINIDUMP_LOCATION_DESCRIPTOR ThreadContext;

		public MINIDUMP_THREAD(){}
		public MINIDUMP_THREAD(BinaryReader reader)
		{
			ThreadId = reader.ReadUInt32();
			SuspendCount = reader.ReadUInt32();
			PriorityClass = reader.ReadUInt32();
			Priority = reader.ReadUInt32();
			Teb = reader.ReadUInt64();
			Stack = new MINIDUMP_MEMORY_DESCRIPTOR(reader);
			ThreadContext = new MINIDUMP_LOCATION_DESCRIPTOR(reader);
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct MINIDUMP_THREAD_LIST
	{
		public UInt32 NumberOfThreads;

		public MINIDUMP_THREAD_LIST(){}
		public MINIDUMP_THREAD_LIST(BinaryReader reader)
		{
			NumberOfThreads = reader.ReadUInt32();
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct MINIDUMP_MODULE_LIST
	{
		public UInt32 NumberOfModules;
		public MINIDUMP_MODULE_LIST(){}
		public MINIDUMP_MODULE_LIST(BinaryReader reader)
		{
			NumberOfModules = reader.ReadUInt32(); 
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct MINIDUMP_MODULE
	{
		public UInt64 BaseOfImage;
		public UInt32 SizeOfImage;
		public UInt32 CheckSum;
		public UInt32 TimeDateStamp;
		public UInt32 ModuleNameRva;
		public VS_FIXEDFILEINFO VersionInfo;
		public MINIDUMP_LOCATION_DESCRIPTOR CvRecord;
		public MINIDUMP_LOCATION_DESCRIPTOR MiscRecord;
		public UInt64 Reserved0;
		public UInt64 Reserved1;

		public MINIDUMP_MODULE(){}
		public MINIDUMP_MODULE(BinaryReader reader)
		{
			BaseOfImage = reader.ReadUInt64();
			SizeOfImage = reader.ReadUInt32();
			CheckSum = reader.ReadUInt32();
			TimeDateStamp = reader.ReadUInt32();
			ModuleNameRva = reader.ReadUInt32();
			VersionInfo = new VS_FIXEDFILEINFO(reader);
			CvRecord = new MINIDUMP_LOCATION_DESCRIPTOR(reader);
			MiscRecord = new MINIDUMP_LOCATION_DESCRIPTOR(reader);
			Reserved0 = reader.ReadUInt64();
			Reserved1 = reader.ReadUInt64();
		}

		private void VerifyChecksum()
		{

		}
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct VS_FIXEDFILEINFO
	{
		public UInt32 dwSignature;
		public UInt32 dwStrucVersion;
		public UInt32 dwFileVersionMS;
		public UInt32 dwFileVersionLS;
		public UInt32 dwProductVersionMS;
		public UInt32 dwProductVersionLS;
		public UInt32 dwFileFlagsMask;
		public VS_FF dwFileFlags;
		public UInt32 dwFileOS;
		public UInt32 dwFileType;
		public UInt32 dwFileSubtype;
		public UInt32 dwFileDateMS;
		public UInt32 dwFileDateLS;

		public VS_FIXEDFILEINFO(){}
		public VS_FIXEDFILEINFO(BinaryReader reader)
		{
			dwSignature = reader.ReadUInt32();
			dwStrucVersion = reader.ReadUInt32();
			dwFileVersionMS = reader.ReadUInt32();
			dwFileVersionLS = reader.ReadUInt32();
			dwProductVersionMS = reader.ReadUInt32();
			dwProductVersionLS = reader.ReadUInt32();
			dwFileFlagsMask = reader.ReadUInt32();
			dwFileFlags = (VS_FF)reader.ReadUInt32();
			dwFileOS = reader.ReadUInt32();
			dwFileType = reader.ReadUInt32();
			dwFileSubtype = reader.ReadUInt32();
			dwFileDateMS = reader.ReadUInt32();
			dwFileDateLS = reader.ReadUInt32();
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct MINIDUMP_STRING
	{
		public UInt32 Length;
		string Buffer;

		public MINIDUMP_STRING(){}
		public MINIDUMP_STRING(BinaryReader reader)
		{
			Length = reader.ReadUInt32();
			Buffer = Encoding.Unicode.GetString(reader.ReadBytes((int)Length));
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct MINIDUMP_UNLOADED_MODULE_LIST
	{
		public UInt32 SizeOfHeader;
		public UInt32 SizeOfEntry;
		public UInt32 NumberOfEntries;

		public MINIDUMP_UNLOADED_MODULE_LIST(){}
		public MINIDUMP_UNLOADED_MODULE_LIST(BinaryReader reader)
		{
			SizeOfHeader = reader.ReadUInt32();
			SizeOfEntry = reader.ReadUInt32();
			NumberOfEntries = reader.ReadUInt32();
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct MINIDUMP_UNLOADED_MODULE
	{
		public UInt64 BaseOfImage;
		public UInt32 SizeOfImage;
		public UInt32 CheckSum;
		public UInt32 TimeDateStamp;
		public UInt32 ModuleNameRva;

		public MINIDUMP_UNLOADED_MODULE(){}
		public MINIDUMP_UNLOADED_MODULE(BinaryReader reader)
		{
			BaseOfImage = reader.ReadUInt64();
			SizeOfImage = reader.ReadUInt32();
			CheckSum = reader.ReadUInt32();
			TimeDateStamp = reader.ReadUInt32();
			ModuleNameRva = reader.ReadUInt32();
		}
	}

	[Flags]
	public enum VS_FF : uint
	{
		DEBUG = 0x00000001,
		PRERELEASE = 0x00000002,
		PATCHED = 0x00000004,
		PRIVATEBUILD = 0x00000008,
		INFOINFERRED = 0x00000010,
		SPECIALBUILD = 0x00000020,
	}

	public enum MINIDUMP_TYPE : ulong
	{
		MiniDumpNormal = 0x00000000,
		MiniDumpWithDataSegs = 0x00000001,
		MiniDumpWithFullMemory = 0x00000002,
		MiniDumpWithHandleData = 0x00000004,
		MiniDumpFilterMemory = 0x00000008,
		MiniDumpScanMemory = 0x00000010,
		MiniDumpWithUnloadedModules = 0x00000020,
		MiniDumpWithIndirectlyReferencedMemory = 0x00000040,
		MiniDumpFilterModulePaths = 0x00000080,
		MiniDumpWithProcessThreadData = 0x00000100,
		MiniDumpWithPrivateReadWriteMemory = 0x00000200,
		MiniDumpWithoutOptionalData = 0x00000400,
		MiniDumpWithFullMemoryInfo = 0x00000800,
		MiniDumpWithThreadInfo = 0x00001000,
		MiniDumpWithCodeSegs = 0x00002000,
		MiniDumpWithoutAuxiliaryState = 0x00004000,
		MiniDumpWithFullAuxiliaryState = 0x00008000,
		MiniDumpWithPrivateWriteCopyMemory = 0x00010000,
		MiniDumpIgnoreInaccessibleMemory = 0x00020000,
		MiniDumpWithTokenInformation = 0x00040000,
		MiniDumpWithModuleHeaders = 0x00080000,
		MiniDumpFilterTriage = 0x00100000,
		MiniDumpWithAvxXStateContext = 0x00200000,
		MiniDumpWithIptTrace = 0x00400000,
		MiniDumpScanInaccessiblePartialPages = 0x00800000,
		MiniDumpFilterWriteCombinedMemory,
		MiniDumpValidTypeFlags = 0x01ffffff
	}

	public enum MINIDUMP_STREAM_TYPE : uint
	{
		UnusedStream = 0,
		ReservedStream0 = 1,
		ReservedStream1 = 2,
		ThreadListStream = 3,
		ModuleListStream = 4,
		MemoryListStream = 5,
		ExceptionStream = 6,
		SystemInfoStream = 7,
		ThreadExListStream = 8,
		Memory64ListStream = 9,
		CommentStreamA = 10,
		CommentStreamW = 11,
		HandleDataStream = 12,
		FunctionTableStream = 13,
		UnloadedModuleListStream = 14,
		MiscInfoStream = 15,
		MemoryInfoListStream = 16,
		ThreadInfoListStream = 17,
		HandleOperationListStream = 18,
		TokenStream = 19,
		JavaScriptDataStream = 20,
		SystemMemoryInfoStream = 21,
		ProcessVmCountersStream = 22,
		IptTraceStream = 23,
		ThreadNamesStream = 24,
		SQLCompressedMemoryStream = 0x7000, //Signature = 0x7A646D7079717375
		ceStreamNull = 0x8000,
		ceStreamSystemInfo = 0x8001,
		ceStreamException = 0x8002,
		ceStreamModuleList = 0x8003,
		ceStreamProcessList = 0x8004,
		ceStreamThreadList = 0x8005,
		ceStreamThreadContextList = 0x8006,
		ceStreamThreadCallStackList = 0x8007,
		ceStreamMemoryVirtualList = 0x8008,
		ceStreamMemoryPhysicalList = 0x8009,
		ceStreamBucketParameters = 0x800A,
		ceStreamProcessModuleMap = 0x800B,
		ceStreamDiagnosisList = 0x800C,
		LastReservedStream = 0xffff
	}
}
