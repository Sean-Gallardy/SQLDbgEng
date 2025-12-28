using SQLDbgEng.Minidump;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SQLDbgEng
{
	public class DumpUnloadedModuleInfo
	{
		private MINIDUMP_UNLOADED_MODULE m_unloadedModule;
		public DumpUnloadedModuleInfo(MINIDUMP_UNLOADED_MODULE unloadedModule, string moduleName)
		{
			m_unloadedModule = unloadedModule;
			ModuleName = moduleName;
		}

		public string ModuleName { get; private set; }

	}
}
