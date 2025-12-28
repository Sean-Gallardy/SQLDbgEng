using SQLDbgEng.Minidump;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SQLDbgEng
{
	public class DumpLoadedModuleInfo
	{
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

		private MINIDUMP_MODULE m_loadedModule;

		public DumpLoadedModuleInfo(MINIDUMP_MODULE loadedModule, string moduleName)
		{
			m_loadedModule = loadedModule;
			ModuleName = moduleName;
		}

		public string ModuleName { get; private set; }

		public bool IsAKnownBadModule
		{
			get
			{
				return _knownBadModules.Any(s => ModuleName.Contains(s, StringComparison.InvariantCultureIgnoreCase));
			}
		}
	}
}
