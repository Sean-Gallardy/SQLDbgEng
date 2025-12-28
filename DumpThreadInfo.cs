using SQLDbgEng.Minidump;
using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SQLDbgEng
{
	public class DumpThreadInfo
	{
		private readonly string _threadStart = "ntdll!RtlUserThreadStart";
		private readonly List<string> _knownBadModules = new List<string>()
		{
			"CYINJCT" //Cylance
			,"ENTAPI", "HIPI", "HcSQL", "HcApi", "HcThe", "MFEBOPK", "MFETDIK" //McAfee
			,"SOPHOS_DETOURED", "SWI_IFSLSP_64", "SOPHOS_DETOURED_x64" //Sophos
			,"PIOLEDB", "PISDK" //PI OLEDB
			,"UMPPC", "SCRIPTCONTROL" //Crowstrike
			,"perfiCrcPerfMonMgr" //Trend Micro
			,"NLEMSQL64", "NLEMSQL" //Netlib
		};

		private MINIDUMP_THREAD m_minidumpThreadInfo;

		public DumpThreadInfo(uint debuggerThreadID, MINIDUMP_THREAD threadInfo, string[] symbolizedThreadStack)
		{
			DebuggerThreadID = debuggerThreadID;
			m_minidumpThreadInfo = threadInfo;
			SymbolizedThreadStack = symbolizedThreadStack;
		}

		public uint DebuggerThreadID { get; private set; }
		public uint OSThreadID { get { return m_minidumpThreadInfo.ThreadId; } }
		public string[] SymbolizedThreadStack { get; private set; }

		public uint PriorityClass { get { return m_minidumpThreadInfo.PriorityClass; } }

		public uint Priority { get { return m_minidumpThreadInfo.Priority; } }

		public bool HasProperThreadStart
		{
			get
			{
				return SymbolizedThreadStack[SymbolizedThreadStack.Length - 1].Contains(_threadStart, StringComparison.InvariantCultureIgnoreCase);
			}
		}

		public bool HasBadModule
		{
			get
			{
				string combinedStack = string.Join('\n', SymbolizedThreadStack);
				return _knownBadModules.Any(s => combinedStack.Contains(s, StringComparison.InvariantCultureIgnoreCase));
			}
		}

	}
}
